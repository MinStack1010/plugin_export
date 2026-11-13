"""Extracts the active Fusion design into the CAD-independent scene model.

Port of SolidWorksGeometryExtractor:

SolidWorks                                   Fusion
-------------------------------------------  ---------------------------------------------
Part document -> root node with meshes       root component bodies -> meshes of root node
Assembly -> root node + component children   root component occurrences -> child nodes
Component2.Name2                             Occurrence.name ("Component:1")
Component2.Transform2 (world) relative to    native Occurrence.transform2, which is already
parent world transform                       relative to the parent component
Suppressed components skipped                Fusion has no occurrence suppression in the API
PartDoc.GetBodies2(swSolidBody, visible)     BRepBody.isSolid and BRepBody.isLightBulbOn
ITessellation (ImprovedQuality)              TriangleMeshCalculator (configurable quality)
Face/feature/body/part render material       face appearance in the assembly context (covers
                                             face, body, occurrence and material sources)
Facets grouped by equivalent material        same grouping and equivalence tolerance

Geometry is read in component space (cm) from the native bodies so that each
occurrence node keeps only its local transform, exactly like the SolidWorks
output. Appearances are read from assembly-context proxies so that occurrence
level appearance overrides are honoured. The design is never modified.
"""

import math
from dataclasses import dataclass, field
from typing import Callable, Dict, List, Optional, Tuple

import adsk.core
import adsk.fusion

from ..core.errors import ExportError
from ..core.materials import build_material, materials_equivalent
from ..core.scene import ExtractedMesh, ExtractedNode, ExtractedScene, validate_mesh
from .appearance_reader import read_appearance

# SolidWorks skips facets whose |cross|^2 < 1e-20 m^4. Fusion works in cm, so the
# equivalent threshold is 1e-20 * 100^4 = 1e-12 cm^4.
_DEGENERATE_THRESHOLD = 1e-12

QUALITY_OPTIONS = {
    "low": "LowQualityTriangleMesh",
    "normal": "NormalQualityTriangleMesh",
    "high": "HighQualityTriangleMesh",
    "very_high": "VeryHighQualityTriangleMesh",
}

RawMesh = Tuple[list, list, list]  # coordinates (cm), normals, indices


@dataclass
class ExtractionReport:
    occurrences: int = 0
    body_instances: int = 0
    skipped_hidden_bodies: List[str] = field(default_factory=list)
    skipped_surface_bodies: List[str] = field(default_factory=list)
    skipped_mesh_bodies: List[str] = field(default_factory=list)
    degenerate_triangles: int = 0
    repaired_normals: int = 0
    unreadable_appearances: List[str] = field(default_factory=list)
    appearance_warnings: List[str] = field(default_factory=list)

    def warnings(self) -> List[str]:
        result = []
        if self.skipped_hidden_bodies:
            result.append("{} hidden body instance(s) skipped: {}".format(
                len(self.skipped_hidden_bodies), _short_list(self.skipped_hidden_bodies)))
        if self.skipped_surface_bodies:
            result.append("{} surface (non-solid) body instance(s) skipped: {}".format(
                len(self.skipped_surface_bodies), _short_list(self.skipped_surface_bodies)))
        if self.skipped_mesh_bodies:
            result.append("{} mesh body instance(s) skipped (only B-Rep solids are exported): {}".format(
                len(self.skipped_mesh_bodies), _short_list(self.skipped_mesh_bodies)))
        if self.degenerate_triangles:
            result.append("{} degenerate triangle(s) removed".format(self.degenerate_triangles))
        if self.repaired_normals:
            result.append("{} zero-length vertex normal(s) recomputed from triangle geometry".format(
                self.repaired_normals))
        if self.unreadable_appearances:
            result.append("default grey material used for unreadable appearance(s): {}".format(
                _short_list(self.unreadable_appearances)))
        result.extend(self.appearance_warnings)
        return result


def _short_list(items, limit=5):
    unique = list(dict.fromkeys(items))
    text = ", ".join(unique[:limit])
    if len(unique) > limit:
        text += ", ... (+{} more)".format(len(unique) - limit)
    return text


def matrix_from_fusion(matrix3d) -> Tuple[float, ...]:
    """Converts adsk.core.Matrix3D to the row-major tuple used by the scene model."""
    return tuple(float(matrix3d.getCell(r, c)) for r in range(4) for c in range(4))


def occurrence_local_transform(native_occurrence) -> Tuple[float, ...]:
    """Transform of a native (non-proxy) occurrence relative to its parent component."""
    try:
        matrix3d = native_occurrence.transform2
    except AttributeError:  # Fusion releases older than transform2
        matrix3d = native_occurrence.transform
    return matrix_from_fusion(matrix3d)


class FusionGeometryExtractor:
    def __init__(self, mesh_quality: str = "high",
                 progress: Optional[Callable[[int, int, str], None]] = None):
        if mesh_quality not in QUALITY_OPTIONS:
            raise ValueError("Unknown mesh quality '{}'.".format(mesh_quality))
        self._quality = getattr(adsk.fusion.TriangleMeshQualityOptions, QUALITY_OPTIONS[mesh_quality])
        self._progress = progress
        self._mesh_cache: Dict[tuple, RawMesh] = {}
        self._appearance_cache: Dict[str, object] = {}
        self._face_sources = {
            adsk.core.AppearanceSourceTypes.FaceAppearanceSource,
            adsk.core.AppearanceSourceTypes.OverrideAppearanceSource,
        }
        self._done = 0
        self._total = 0
        self.report = ExtractionReport()

    # ------------------------------------------------------------------ entry
    def extract(self, design, document_name: Optional[str] = None) -> ExtractedScene:
        if design is None:
            raise ExportError("No active Fusion design.")
        root_component = design.rootComponent
        if root_component is None:
            raise ExportError("The active design has no root component.")

        self._total = self._count_body_instances(root_component)
        self._done = 0

        root = ExtractedNode(name=document_name or root_component.name)
        self._extract_component_bodies(root_component, None, root)
        for occurrence in _items(root_component.occurrences):
            root.children.append(self._extract_occurrence(occurrence, None))
        return ExtractedScene(root=root)

    # -------------------------------------------------------------- hierarchy
    def _count_body_instances(self, component) -> int:
        total = sum(1 for b in _items(component.bRepBodies) if b.isSolid and b.isLightBulbOn)
        for occurrence in _items(component.occurrences):
            total += self._count_body_instances(occurrence.component)
        return total

    def _extract_occurrence(self, native_occurrence, parent_proxy) -> ExtractedNode:
        if parent_proxy is None:
            context = native_occurrence
        else:
            context = native_occurrence.createForAssemblyContext(parent_proxy)
            if context is None:
                raise ExportError("Unable to resolve occurrence '{}' in its assembly context.".format(
                    native_occurrence.name))

        self.report.occurrences += 1
        node = ExtractedNode(
            name=native_occurrence.name,
            local_transform=occurrence_local_transform(native_occurrence),
        )
        component = native_occurrence.component
        self._extract_component_bodies(component, context, node)
        for child in _items(component.occurrences):
            node.children.append(self._extract_occurrence(child, context))
        return node

    def _extract_component_bodies(self, component, occurrence_context, node: ExtractedNode) -> None:
        path = node.name or component.name
        try:
            mesh_bodies = list(_items(component.meshBodies))
        except AttributeError:
            mesh_bodies = []
        for mesh_body in mesh_bodies:
            self.report.skipped_mesh_bodies.append("{}/{}".format(path, mesh_body.name))

        for index, body in enumerate(_items(component.bRepBodies)):
            label = "{}/{}".format(path, body.name)
            if not body.isSolid:
                self.report.skipped_surface_bodies.append(label)
                continue
            if not body.isLightBulbOn:
                self.report.skipped_hidden_bodies.append(label)
                continue

            self._report_progress(label)
            context_body = body
            if occurrence_context is not None:
                context_body = body.createForAssemblyContext(occurrence_context)
                if context_body is None:
                    raise ExportError("Unable to resolve body '{}' in its assembly context.".format(label))

            for mesh in self._extract_body_meshes(component, index, body, context_body, node.name or "Mesh"):
                if mesh.vertex_count > 0 and mesh.triangle_count > 0:
                    validate_mesh(mesh)
                    node.meshes.append(mesh)
            self.report.body_instances += 1
            self._done += 1

    # ------------------------------------------------------------------ meshes
    def _extract_body_meshes(self, component, body_index, body, context_body, node_name) -> List[ExtractedMesh]:
        body_name = body.name
        native_faces = list(_items(body.faces))
        context_faces = list(_items(context_body.faces))
        if len(native_faces) != len(context_faces):
            raise ExportError("Face mismatch while reading appearances of body '{}'.".format(body_name))

        groups = []          # list of (material, mesh, face_indices)
        face_groups = []     # group index per face
        for face_index, context_face in enumerate(context_faces):
            material = self._face_material(context_face, body_name)
            group_index = -1
            for i, group in enumerate(groups):
                if materials_equivalent(material, group[0]):
                    group_index = i
                    break
            if group_index == -1:
                group_index = len(groups)
                mesh = ExtractedMesh(name="{}_Material_{}".format(node_name, group_index), material=material)
                groups.append((material, mesh, []))
            groups[group_index][2].append(face_index)
            face_groups.append(group_index)

        if not groups:
            return []

        cache_prefix = (_component_key(component), body_index)
        if len(groups) == 1:
            raw = self._tessellate(cache_prefix, body, body_name)
            self._append_raw_mesh(groups[0][1], raw, body_name)
        else:
            for face_index, face in enumerate(native_faces):
                raw = self._tessellate(cache_prefix + (face_index,), face, body_name)
                self._append_raw_mesh(groups[face_groups[face_index]][1], raw, body_name)

        result = []
        for _, mesh, _ in groups:
            self._repair_normals(mesh, body_name)
            if mesh.indices:
                result.append(mesh)
        return result

    def _face_material(self, context_face, body_name):
        appearance = context_face.appearance
        if appearance is None:
            return None
        try:
            source = context_face.appearanceSourceType
        except Exception:
            source = None
        suffix = "_Face" if source in self._face_sources else ""

        key = getattr(appearance, "id", None) or getattr(appearance, "name", "")
        if key not in self._appearance_cache:
            values = read_appearance(appearance)
            self._appearance_cache[key] = values
            if values is None:
                self.report.unreadable_appearances.append(getattr(appearance, "name", key))
            else:
                for warning in values.warnings:
                    if warning not in self.report.appearance_warnings:
                        self.report.appearance_warnings.append(warning)
        values = self._appearance_cache[key]
        if values is None:
            return None
        return build_material(body_name + suffix + "_Material", values.rgba255, values.metallic, values.roughness)

    def _tessellate(self, cache_key, entity, body_name) -> RawMesh:
        cached = self._mesh_cache.get(cache_key)
        if cached is not None:
            return cached
        calculator = entity.meshManager.createMeshCalculator()
        if calculator is None:
            raise ExportError("Unable to create a mesh calculator for body '{}'.".format(body_name))
        calculator.setQuality(self._quality)
        triangle_mesh = calculator.calculate()
        if triangle_mesh is None:
            raise ExportError("Body tessellation failed: {}".format(body_name))
        raw = (
            list(triangle_mesh.nodeCoordinatesAsDouble),
            list(triangle_mesh.normalVectorsAsDouble),
            list(triangle_mesh.nodeIndices),
        )
        if len(raw[0]) % 3 or len(raw[1]) != len(raw[0]) or len(raw[2]) % 3:
            raise ExportError("Fusion returned an inconsistent mesh for body '{}'.".format(body_name))
        self._mesh_cache[cache_key] = raw
        return raw

    def _append_raw_mesh(self, mesh: ExtractedMesh, raw: RawMesh, body_name: str) -> None:
        coords, normals, indices = raw
        offset = mesh.vertex_count
        vertex_count = len(coords) // 3
        mesh.positions.extend(coords)
        for i in range(0, len(normals), 3):
            x, y, z = normals[i], normals[i + 1], normals[i + 2]
            length = math.sqrt(x * x + y * y + z * z)
            if length < 1e-10:
                mesh.normals.extend((0.0, 0.0, 0.0))
            else:
                mesh.normals.extend((x / length, y / length, z / length))

        p = mesh.positions
        n = mesh.normals
        for t in range(0, len(indices), 3):
            a, b, c = indices[t], indices[t + 1], indices[t + 2]
            if not (0 <= a < vertex_count and 0 <= b < vertex_count and 0 <= c < vertex_count):
                raise ExportError("Fusion returned an invalid triangle index for body '{}'.".format(body_name))
            i0, i1, i2 = a + offset, b + offset, c + offset
            ux, uy, uz = (p[3 * i1] - p[3 * i0], p[3 * i1 + 1] - p[3 * i0 + 1], p[3 * i1 + 2] - p[3 * i0 + 2])
            vx, vy, vz = (p[3 * i2] - p[3 * i0], p[3 * i2 + 1] - p[3 * i0 + 1], p[3 * i2 + 2] - p[3 * i0 + 2])
            gx, gy, gz = uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx
            if gx * gx + gy * gy + gz * gz < _DEGENERATE_THRESHOLD:
                self.report.degenerate_triangles += 1
                continue
            ax = n[3 * i0] + n[3 * i1] + n[3 * i2]
            ay = n[3 * i0 + 1] + n[3 * i1 + 1] + n[3 * i2 + 1]
            az = n[3 * i0 + 2] + n[3 * i1 + 2] + n[3 * i2 + 2]
            if gx * ax + gy * ay + gz * az < 0:
                i1, i2 = i2, i1
            mesh.indices.extend((i0, i1, i2))

    def _repair_normals(self, mesh: ExtractedMesh, body_name: str) -> None:
        n = mesh.normals
        broken = [v for v in range(mesh.vertex_count) if n[3 * v] == 0.0 and n[3 * v + 1] == 0.0 and n[3 * v + 2] == 0.0]
        if not broken:
            return
        broken_set = set(broken)
        sums = {v: [0.0, 0.0, 0.0] for v in broken}
        p = mesh.positions
        idx = mesh.indices
        for t in range(0, len(idx), 3):
            tri = idx[t:t + 3]
            if not broken_set.intersection(tri):
                continue
            i0, i1, i2 = tri
            ux, uy, uz = p[3 * i1] - p[3 * i0], p[3 * i1 + 1] - p[3 * i0 + 1], p[3 * i1 + 2] - p[3 * i0 + 2]
            vx, vy, vz = p[3 * i2] - p[3 * i0], p[3 * i2 + 1] - p[3 * i0 + 1], p[3 * i2 + 2] - p[3 * i0 + 2]
            g = (uy * vz - uz * vy, uz * vx - ux * vz, ux * vy - uy * vx)
            for v in tri:
                if v in sums:
                    s = sums[v]
                    s[0] += g[0]
                    s[1] += g[1]
                    s[2] += g[2]
        referenced = None
        for v, (x, y, z) in sums.items():
            length = math.sqrt(x * x + y * y + z * z)
            if length < 1e-20:
                if referenced is None:
                    referenced = set(idx)
                if v in referenced:
                    raise ExportError("Zero normal for a vertex of body '{}'.".format(body_name))
                # Vertex left unused by a removed degenerate triangle: it is never rendered,
                # but glTF requires unit-length normals for every vertex.
                n[3 * v], n[3 * v + 1], n[3 * v + 2] = 0.0, 0.0, 1.0
                continue
            n[3 * v], n[3 * v + 1], n[3 * v + 2] = x / length, y / length, z / length
            self.report.repaired_normals += 1

    def _report_progress(self, label: str) -> None:
        if self._progress is not None:
            self._progress(self._done, self._total, label)


def _component_key(component):
    try:
        return component.id
    except Exception:
        return component.name


def _items(collection):
    """Iterates a Fusion collection via count/item (works for all collection types)."""
    if collection is None:
        return
    for i in range(collection.count):
        item = collection.item(i)
        if item is not None:
            yield item
