"""NX model -> ExtractedScene (port of SolidWorksGeometryExtractor).

Mapping SOLIDWORKS -> NX:
  ActiveDoc                      -> Session.Parts.Display
  part document                  -> Part without ComponentAssembly.RootComponent
  assembly / root component      -> Part.ComponentAssembly.RootComponent
  Component2.GetChildren()       -> Component.GetChildren()
  Component2.IsSuppressed()      -> Component.IsSuppressed
  Component2.Name2 ("Sub-1/P-1") -> "<prototype leaf>-<n>" (path joined with "/")
  Component2.Transform2          -> Component.GetPosition() (absolute in display part)
  GetBodies2(solid, visibleOnly) -> Part.Bodies (IsSolidBody, not blanked, visible
                                    layer, in the component reference set)
  Body2.GetTessellation()        -> UF_FACET (see tessellator.py)
  DSPBR render materials         -> NX display colour / translucency

Part definitions vs occurrences: each prototype body is tessellated once
(cache keyed by the prototype body tag) in its own part coordinates; every
component occurrence becomes its own node with its own local transform, and
colours are read from the occurrence objects (assembly colour overrides).
"""

from .. import matrix as mx
from ..errors import NoActivePartError, UnsupportedModelError, TessellationError
from ..mesh_builder import MeshAccumulator, validate_mesh
from ..materials import materials_equivalent
from ..scene import ExtractedNode, ExtractedScene
from .appearance import AppearanceResolver
from .tessellator import Tessellator
from .units import UnitResolver

NO_ACTIVE_PART_MESSAGE = "No active NX part. Open a part or an assembly first."
UNSUPPORTED_MESSAGE = "Only parts and assemblies are supported."


class _NameAllocator:
    """Unique instance names per parent: "bolt-1", "bolt-2", ... (like SW "Part1-1")."""

    def __init__(self):
        self._counts = {}

    def allocate(self, base):
        n = self._counts.get(base, 0) + 1
        self._counts[base] = n
        return "{}-{}".format(base, n)


class NxSceneExtractor:
    def __init__(self, ctx, options, log):
        self.ctx = ctx
        self.nx = ctx.nx
        self.options = options
        self.log = log
        self.units = UnitResolver(ctx, log)
        self.tessellator = Tessellator(ctx, options, log)
        self._facet_cache = {}       # prototype body tag -> triangles | TessellationError
        self._face_cache = {}        # prototype body tag -> {face tag: face}
        self._convention = "rows"    # orientation matrix rows = axes
        self._convention_confirmed = False
        self.display_part = None
        self.display_scale = 0.001
        self.appearance = None
        self.stats = {"bodies": 0, "skipped_bodies": 0, "failed_bodies": 0, "components": 0}

    # ------------------------------------------------------------------ entry
    def extract_display_part(self):
        part = self.ctx.session.Parts.Display
        if part is None:
            raise NoActivePartError(NO_ACTIVE_PART_MESSAGE)
        return self.extract(part)

    def extract(self, part):
        if part is None:
            raise NoActivePartError(NO_ACTIVE_PART_MESSAGE)
        part_type = getattr(self.nx, "Part", None)
        if part_type is not None and not isinstance(part, part_type):
            raise UnsupportedModelError(UNSUPPORTED_MESSAGE)
        self._ensure_loaded(part)
        self.display_part = part
        self.display_scale = self.units.scale_to_meters(part)
        self.appearance = AppearanceResolver(self.ctx, part, self.options, self.log)

        root = ExtractedNode(name=self._part_title(part), local_transform=mx.IDENTITY)
        root_component = self._root_component(part)
        self.log.info("Exporting '{}' ({}; units -> m factor {})".format(
            root.name, "assembly" if root_component is not None else "part", self.display_scale))

        if root_component is None or self.options.include_root_part_bodies:
            self._add_part_meshes(root, part, None, mx.IDENTITY)

        if root_component is not None:
            if self.options.validate_transforms:
                self._detect_convention(root_component)
            names = _NameAllocator()
            for child in self._children(root_component):
                root.children.append(self._extract_component(child, mx.IDENTITY, "", names))

        self.log.info("Extraction finished: {}".format(self.stats))
        return ExtractedScene(root=root)

    # ------------------------------------------------------------- hierarchy
    def _root_component(self, part):
        try:
            assembly = part.ComponentAssembly
        except Exception:
            return None
        if assembly is None:
            return None
        try:
            return assembly.RootComponent
        except Exception:
            return None

    def _children(self, component):
        try:
            children = component.GetChildren() or []
        except Exception as exc:
            self.log.warning("GetChildren failed for '{}': {}".format(self._safe(component, "DisplayName"), exc))
            return []
        result = []
        for child in children:
            if child is None:
                continue
            try:
                suppressed = bool(child.IsSuppressed)
            except Exception:
                suppressed = False
            if suppressed:  # SW: child.IsSuppressed() -> skipped
                self.log.info("Suppressed component skipped: {}".format(self._safe(child, "DisplayName")))
                continue
            result.append(child)
        return result

    def _extract_component(self, component, parent_world, parent_name, names):
        self.stats["components"] += 1
        prototype = self._prototype(component)
        world = self._component_world(component)
        local = mx.multiply(mx.inverse_affine(parent_world), world)

        leaf = names.allocate(self._component_base_name(component, prototype))
        if self.options.component_name_style == "path" and parent_name:
            name = parent_name + "/" + leaf
        else:
            name = leaf
        node = ExtractedNode(name=name, local_transform=local)

        if prototype is not None:
            self._add_part_meshes(node, prototype, component, world)

        child_names = _NameAllocator()
        for child in self._children(component):
            node.children.append(self._extract_component(child, world, name, child_names))
        return node

    def _prototype(self, component):
        try:
            prototype = component.Prototype
        except Exception:
            prototype = None
        if prototype is None:
            self.log.warning("Component '{}' is not loaded; exported without geometry.".format(
                self._safe(component, "DisplayName")))
            return None
        part_type = getattr(self.nx, "Part", None)
        if part_type is not None and not isinstance(prototype, part_type):
            self.log.warning("Component '{}' does not reference an NX part; exported without geometry.".format(
                self._safe(component, "DisplayName")))
            return None
        self._ensure_loaded(prototype)
        return prototype

    def _component_base_name(self, component, prototype):
        for getter in (lambda: prototype.Leaf if prototype is not None else None,
                       lambda: component.DisplayName,
                       lambda: component.Name):
            try:
                value = getter()
            except Exception:
                value = None
            if value:
                return str(value)
        return "Component"

    # ------------------------------------------------------------- transforms
    def _position(self, component):
        point, orientation = component.GetPosition()
        s = self.display_scale
        origin = (point.X * s, point.Y * s, point.Z * s)
        o = orientation
        rows = ((o.Xx, o.Xy, o.Xz), (o.Yx, o.Yy, o.Yz), (o.Zx, o.Zy, o.Zz))
        return origin, rows

    def _world_from(self, origin, rows, convention):
        if convention == "rows":
            return mx.from_axes(rows[0], rows[1], rows[2], origin)
        return mx.from_rows_3x3(rows, origin)

    def _component_world(self, component):
        origin, rows = self._position(component)
        return self._world_from(origin, rows, self._convention)

    def _detect_convention(self, root_component, max_components=200):
        """Decide once, before any node is built, how GetPosition's orientation
        matrix must be read (rows = axes, or columns = axes).

        Walks the assembly depth-first and uses the first component whose
        rotation is not symmetric and which has a body edge with an occurrence:
        a vertex of the prototype edge transformed by the component matrix must
        coincide with the same vertex of the occurrence edge (occurrence
        geometry is reported in display-part coordinates).
        """
        stack = list(reversed(self._raw_children(root_component)))
        visited = 0
        while stack and visited < max_components:
            component = stack.pop()
            visited += 1
            verdict = self._convention_from_geometry(component)
            if verdict is not None:
                if verdict != self._convention:
                    self.log.warning("Component orientation uses the '{}' convention; switched.".format(verdict))
                else:
                    self.log.info("Component orientation convention confirmed: {}".format(verdict))
                self._convention = verdict
                self._convention_confirmed = True
                return verdict
            stack.extend(reversed(self._raw_children(component)))
        self.log.info("Component orientation convention not decisive (no rotated component with geometry); "
                      "using '{}'.".format(self._convention))
        return None

    @staticmethod
    def _raw_children(component):
        try:
            return [c for c in (component.GetChildren() or []) if c is not None]
        except Exception:
            return []

    def _convention_from_geometry(self, component):
        """Return "rows", "columns" or None (not decisive) for one component."""
        try:
            prototype = component.Prototype
        except Exception:
            return None
        if prototype is None:
            return None
        try:
            origin, rows = self._position(component)
        except Exception:
            return None
        world_rows = self._world_from(origin, rows, "rows")
        world_cols = self._world_from(origin, rows, "columns")
        if mx.nearly_equal(world_rows, world_cols, 1e-9):
            return None  # symmetric rotation: not decisive
        part_scale = self.units.scale_to_meters(prototype)
        for body in self._iter_bodies(prototype):
            try:
                edges = list(body.GetEdges() or [])[:4]
            except Exception:
                continue
            for edge in edges:
                try:
                    occ_edge = component.FindOccurrence(edge)
                    if occ_edge is None:
                        continue
                    pv = edge.GetVertices()[0]
                    ov = occ_edge.GetVertices()[0]
                except Exception:
                    continue
                local_pt = (pv.X * part_scale, pv.Y * part_scale, pv.Z * part_scale)
                expected = (ov.X * self.display_scale, ov.Y * self.display_scale, ov.Z * self.display_scale)
                tol = 1e-6 + 1e-6 * max(abs(c) for c in expected)
                if _close(mx.transform_point(world_rows, local_pt), expected, tol):
                    return "rows"
                if _close(mx.transform_point(world_cols, local_pt), expected, tol):
                    return "columns"
                self.log.warning("Transform validation inconclusive for '{}'.".format(
                    self._safe(component, "DisplayName")))
                return None
        return None

    # ------------------------------------------------------------- geometry
    def _iter_bodies(self, part):
        try:
            return list(part.Bodies)
        except Exception as exc:
            self.log.warning("Cannot read bodies of '{}': {}".format(self._part_title(part), exc))
            return []

    def _collect_bodies(self, part, component):
        """Return [(prototype_body, shown_body)] honouring visibility rules."""
        result = []
        for body in self._iter_bodies(part):
            try:
                is_solid = bool(body.IsSolidBody)
            except Exception:
                is_solid = False
            if not is_solid and self.options.skip_sheet_bodies:
                self.stats["skipped_bodies"] += 1
                self.log.info("Non-solid body skipped (only solid bodies are exported, like SW): {}".format(
                    self._body_name(body, 0)))
                continue
            shown = body
            if component is not None:
                try:
                    shown = component.FindOccurrence(body)
                except Exception:
                    shown = None
                if shown is None:  # not in the component reference set
                    continue
            if self.options.skip_blanked_bodies and self._safe(shown, "IsBlanked", False):
                continue
            if self.options.skip_hidden_layer_bodies and self._layer_hidden(shown):
                continue
            result.append((body, shown))
        return result

    def _layer_hidden(self, obj):
        try:
            layer = obj.Layer
            state = self.display_part.Layers.GetState(layer)
            return state == self.nx.Layer.State.Hidden
        except Exception:
            return False

    def _add_part_meshes(self, node, part, component, world):
        part_scale = self.units.scale_to_meters(part)
        bodies = self._collect_bodies(part, component)
        for body_index, (body, shown) in enumerate(bodies):
            body_name = self._body_name(body, body_index)
            triangles, shareable = self._body_triangles(body, shown, part_scale, component, world, body_name)
            if not triangles:
                continue
            self.stats["bodies"] += 1
            for mesh in self._material_meshes(node.name, body, shown, component, body_name, triangles, shareable):
                validate_mesh(mesh)
                node.meshes.append(mesh)

    def _body_triangles(self, body, shown, part_scale, component, world, body_name):
        tag = body.Tag
        cached = self._facet_cache.get(tag)
        if cached is None:
            try:
                cached = self.tessellator.facet_body(tag, part_scale)
            except TessellationError as exc:
                cached = exc
            self._facet_cache[tag] = cached
        if not isinstance(cached, TessellationError):
            return cached, True
        # Fallback: tessellate the occurrence (assembly coordinates) and bring it
        # back into the component frame.
        if component is not None and shown is not None and shown is not body:
            try:
                absolute = self.tessellator.facet_body(shown.Tag, self.display_scale)
                inverse = mx.inverse_affine(world)
                local = []
                for face_tag, pts, nrms in absolute:
                    local.append((face_tag,
                                  tuple(mx.transform_point(inverse, p) for p in pts),
                                  tuple(mx.transform_vector(inverse, n) if n is not None else None for n in nrms)))
                self.log.info("Body '{}' tessellated through its occurrence.".format(body_name))
                return local, False
            except TessellationError as exc:
                self.log.warning("Occurrence tessellation failed for '{}': {}".format(body_name, exc))
        self.stats["failed_bodies"] += 1
        self.log.warning("Body '{}' could not be tessellated and was skipped: {}".format(body_name, cached))
        return [], False

    def _faces_by_tag(self, body):
        faces = self._face_cache.get(body.Tag)
        if faces is None:
            faces = {}
            try:
                for face in body.GetFaces() or []:
                    faces[face.Tag] = face
            except Exception as exc:
                self.log.warning("GetFaces failed: {}".format(exc))
            self._face_cache[body.Tag] = faces
        return faces

    def _material_meshes(self, node_name, body, shown, component, body_name, triangles, shareable):
        """Port of ExtractBodyMesh: faces grouped into meshes per equivalent material."""
        body_material = self.appearance.body_material(shown, body_name)
        proto_faces = self._faces_by_tag(body)
        groups = []          # [(material, accumulator)]
        face_group = {}
        for face_tag, pts, nrms in triangles:
            index = face_group.get(face_tag)
            if index is None:
                face = proto_faces.get(face_tag)
                if face is not None and component is not None:
                    try:
                        face = component.FindOccurrence(face)
                    except Exception:
                        face = None
                material = self.appearance.face_material(face, shown, body_name, body_material)
                index = -1
                for j, (existing, _) in enumerate(groups):
                    if materials_equivalent(material, existing):
                        index = j
                        break
                if index == -1:
                    index = len(groups)
                    groups.append((material, MeshAccumulator(node_name + "_Material_" + str(index), material)))
                face_group[face_tag] = index
            groups[index][1].add_triangle(pts[0], pts[1], pts[2], nrms[0], nrms[1], nrms[2])
        meshes = []
        for j, (material, acc) in enumerate(groups):
            if acc.zero_normal_count:
                self.log.info("{} zero-length facet normal(s) replaced in '{}'.".format(acc.zero_normal_count, acc.mesh.name))
            if acc.mesh.indices:
                if shareable:
                    acc.mesh.share_key = (body.Tag, j, material.base_color, material.metallic,
                                          material.roughness, material.alpha_mode, material.name)
                meshes.append(acc.mesh)
        return meshes

    # ------------------------------------------------------------- helpers
    def _ensure_loaded(self, part):
        try:
            if not part.IsFullyLoaded and self.options.load_partially_loaded_parts:
                self.log.info("Loading part fully: {}".format(self._part_title(part)))
                part.LoadThisPartFully()
        except Exception as exc:
            self.log.warning("Could not fully load '{}': {}".format(self._part_title(part), exc))

    @staticmethod
    def _part_title(part):
        for attr in ("Leaf", "Name"):
            try:
                value = getattr(part, attr)
                if value:
                    return str(value)
            except Exception:
                pass
        return "Root"

    @staticmethod
    def _body_name(body, index):
        for attr in ("Name", "JournalIdentifier"):
            try:
                value = getattr(body, attr)
                if value:
                    return str(value)
            except Exception:
                pass
        return "Body{}".format(index + 1)

    @staticmethod
    def _safe(obj, attr, default="?"):
        try:
            return getattr(obj, attr)
        except Exception:
            return default


def _close(a, b, tol):
    return all(abs(x - y) <= tol for x, y in zip(a, b))
