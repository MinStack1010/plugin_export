"""Dependency-free GLB (binary glTF 2.0) writer.

Replaces the SharpGLTF-based GltfSceneWriter of the SolidWorks plugin and keeps
the same content contract:

* one scene whose single root node is the document (name = document title),
* one glTF node per component occurrence, preserving the hierarchy and the local
  transform of every occurrence,
* one glTF mesh per ExtractedMesh (one material group of one body) with a single
  triangle primitive that has POSITION + NORMAL attributes and indices,
* PBR metallic-roughness materials, double sided, with OPAQUE/BLEND/MASK alpha,
* meshes without vertices or triangles are skipped.

glTF allows only one mesh per node. When a component node owns exactly one mesh
it is attached directly to that node; when it owns several, each mesh is attached
to its own child node (named after the mesh, identity transform).
"""

import json
import math
import os
import struct
import sys
import tempfile
from array import array
from typing import Optional

from . import matrix as mx
from .errors import ExportError
from .scene import AlphaMode, ExtractedMaterial, ExtractedMesh, ExtractedNode, ExtractedScene

GENERATOR = "Synode Fusion GLB Exporter 1.0.0"

_GLB_MAGIC = 0x46546C67  # 'glTF'
_CHUNK_JSON = 0x4E4F534A
_CHUNK_BIN = 0x004E4942

_FLOAT = 5126
_UNSIGNED_SHORT = 5123
_UNSIGNED_INT = 5125
_ARRAY_BUFFER = 34962
_ELEMENT_ARRAY_BUFFER = 34963


def _to_bytes(type_code: str, values) -> bytes:
    data = array(type_code, values)
    if sys.byteorder != "little":
        data.byteswap()
    return data.tobytes()


class _GltfBuilder:
    def __init__(self, generator: str):
        self.gltf = {
            "asset": {"version": "2.0", "generator": generator},
            "scene": 0,
            "scenes": [{"nodes": [0]}],
            "nodes": [],
        }
        self.bin = bytearray()
        self.buffer_views = []
        self.accessors = []
        self.meshes = []
        self.materials = []
        self._material_lookup = {}

    # -- binary helpers ---------------------------------------------------
    def _add_buffer_view(self, data: bytes, target: int) -> int:
        while len(self.bin) % 4:
            self.bin.append(0)
        offset = len(self.bin)
        self.bin.extend(data)
        self.buffer_views.append(
            {"buffer": 0, "byteOffset": offset, "byteLength": len(data), "target": target}
        )
        return len(self.buffer_views) - 1

    def _add_accessor(self, view: int, component_type: int, count: int, type_name: str,
                      minimum=None, maximum=None) -> int:
        accessor = {"bufferView": view, "componentType": component_type, "count": count, "type": type_name}
        if minimum is not None:
            accessor["min"] = minimum
            accessor["max"] = maximum
        self.accessors.append(accessor)
        return len(self.accessors) - 1

    # -- materials ----------------------------------------------------------
    def add_material(self, source: Optional[ExtractedMaterial]) -> int:
        if source is None:
            source = ExtractedMaterial()
        name = source.name or "Material"
        colour = [float(min(max(c, 0.0), 1.0)) for c in source.base_colour]
        key = (
            name,
            tuple(round(c, 6) for c in colour),
            round(source.metallic, 6),
            round(source.roughness, 6),
            source.alpha,
        )
        if key in self._material_lookup:
            return self._material_lookup[key]

        material = {
            "name": name,
            "pbrMetallicRoughness": {
                "baseColorFactor": colour,
                "metallicFactor": float(min(max(source.metallic, 0.0), 1.0)),
                "roughnessFactor": float(min(max(source.roughness, 0.0), 1.0)),
            },
            "doubleSided": True,
        }
        if source.alpha == AlphaMode.BLEND:
            material["alphaMode"] = "BLEND"
        elif source.alpha == AlphaMode.MASK:
            material["alphaMode"] = "MASK"
        self.materials.append(material)
        index = len(self.materials) - 1
        self._material_lookup[key] = index
        return index

    # -- meshes ---------------------------------------------------------------
    def add_mesh(self, mesh: ExtractedMesh) -> Optional[int]:
        if mesh.vertex_count == 0 or mesh.triangle_count == 0:
            return None

        positions = mesh.positions
        for value in positions:
            if not math.isfinite(value):
                raise ExportError("Mesh '{}' contains non-finite vertex coordinates.".format(mesh.name))
        for value in mesh.normals:
            if not math.isfinite(value):
                raise ExportError("Mesh '{}' contains non-finite normals.".format(mesh.name))

        count = mesh.vertex_count
        mins = [min(positions[axis::3]) for axis in range(3)]
        maxs = [max(positions[axis::3]) for axis in range(3)]
        # Bounds must describe the float32 values actually written.
        mins = list(array("f", mins))
        maxs = list(array("f", maxs))

        pos_view = self._add_buffer_view(_to_bytes("f", positions), _ARRAY_BUFFER)
        pos_acc = self._add_accessor(pos_view, _FLOAT, count, "VEC3", mins, maxs)
        nrm_view = self._add_buffer_view(_to_bytes("f", mesh.normals), _ARRAY_BUFFER)
        nrm_acc = self._add_accessor(nrm_view, _FLOAT, count, "VEC3")

        if count <= 0xFFFF:
            idx_bytes, idx_type = _to_bytes("H", mesh.indices), _UNSIGNED_SHORT
        else:
            idx_bytes, idx_type = _to_bytes("I", mesh.indices), _UNSIGNED_INT
        idx_view = self._add_buffer_view(idx_bytes, _ELEMENT_ARRAY_BUFFER)
        idx_acc = self._add_accessor(idx_view, idx_type, len(mesh.indices), "SCALAR")

        self.meshes.append({
            "name": mesh.name or "Mesh",
            "primitives": [{
                "attributes": {"POSITION": pos_acc, "NORMAL": nrm_acc},
                "indices": idx_acc,
                "material": self.add_material(mesh.material),
                "mode": 4,
            }],
        })
        return len(self.meshes) - 1

    # -- nodes ----------------------------------------------------------------
    def add_node(self, node: ExtractedNode, default_name: str) -> int:
        nodes = self.gltf["nodes"]
        gltf_node = {"name": node.name or default_name}
        _apply_transform(gltf_node, node.local_transform)
        index = len(nodes)
        nodes.append(gltf_node)

        children = []
        mesh_indices = [i for i in (self.add_mesh(m) for m in node.meshes) if i is not None]
        if len(mesh_indices) == 1:
            gltf_node["mesh"] = mesh_indices[0]
        else:
            for mesh_index in mesh_indices:
                nodes.append({"name": self.meshes[mesh_index]["name"], "mesh": mesh_index})
                children.append(len(nodes) - 1)

        for child in node.children:
            children.append(self.add_node(child, "Component"))

        if children:
            gltf_node["children"] = children
        return index

    def to_glb(self) -> bytes:
        gltf = self.gltf
        if self.meshes:
            gltf["meshes"] = self.meshes
        if self.materials:
            gltf["materials"] = self.materials
        if self.accessors:
            gltf["accessors"] = self.accessors
            gltf["bufferViews"] = self.buffer_views
            while len(self.bin) % 4:
                self.bin.append(0)
            gltf["buffers"] = [{"byteLength": len(self.bin)}]

        json_bytes = json.dumps(gltf, separators=(",", ":"), ensure_ascii=False, allow_nan=False).encode("utf-8")
        while len(json_bytes) % 4:
            json_bytes += b" "

        total = 12 + 8 + len(json_bytes)
        if self.bin:
            total += 8 + len(self.bin)

        out = bytearray()
        out += struct.pack("<III", _GLB_MAGIC, 2, total)
        out += struct.pack("<II", len(json_bytes), _CHUNK_JSON)
        out += json_bytes
        if self.bin:
            out += struct.pack("<II", len(self.bin), _CHUNK_BIN)
            out += self.bin
        return bytes(out)


def _apply_transform(gltf_node: dict, m) -> None:
    if mx.is_identity(m):
        return
    trs = mx.decompose_trs(m)
    if trs is None:
        gltf_node["matrix"] = mx.to_gltf_column_major(m)
        return
    translation, rotation, scale = trs
    if any(abs(v) > 1e-12 for v in translation):
        gltf_node["translation"] = [float(v) for v in translation]
    if abs(rotation[3]) < 1.0 - 1e-12:
        gltf_node["rotation"] = [float(v) for v in rotation]
    if any(abs(s - 1.0) > 1e-9 for s in scale):
        gltf_node["scale"] = [float(v) for v in scale]


def build_glb(scene: ExtractedScene, generator: str = GENERATOR) -> bytes:
    if scene is None:
        raise ValueError("scene must not be None")
    if scene.root is None:
        raise ExportError("The scene contains no root node.")
    builder = _GltfBuilder(generator)
    builder.add_node(scene.root, "Root")
    return builder.to_glb()


def write_glb(scene: ExtractedScene, output_path: str, generator: str = GENERATOR) -> int:
    """Writes the GLB atomically (temp file + rename). Returns the file size."""
    data = build_glb(scene, generator)
    folder = os.path.dirname(os.path.abspath(output_path))
    temp_path = None
    try:
        fd, temp_path = tempfile.mkstemp(prefix=".synode-", suffix=".glb.tmp", dir=folder)
        with os.fdopen(fd, "wb") as handle:
            handle.write(data)
        os.replace(temp_path, output_path)
        temp_path = None
    except OSError as exc:
        raise ExportError("Cannot write GLB file '{}': {}".format(output_path, exc.strerror or exc))
    finally:
        if temp_path and os.path.exists(temp_path):
            try:
                os.remove(temp_path)
            except OSError:
                pass
    return len(data)
