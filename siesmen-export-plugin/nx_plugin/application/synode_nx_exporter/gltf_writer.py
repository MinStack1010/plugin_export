"""Pure Python GLB (binary glTF 2.0) writer.

Port of the SOLIDWORKS ``GltfSceneWriter`` (SharpGLTF 1.0.6 SceneBuilder).
The produced document reproduces the structure SharpGLTF generates for the
same scene (verified with tests/reference_sw):

* one scene, root node = scene root; node names, identity matrices omitted,
  non-identity local transforms written as ``matrix``;
* null names -> "Root" (root), "Component" (child), "Mesh", "Material";
* one glTF mesh per ExtractedMesh with a single triangle primitive;
  the first mesh of a node is attached to the node itself, every additional
  mesh to an unnamed child node (appended after all hierarchy nodes);
* POSITION/NORMAL float32 interleaved in one ARRAY_BUFFER view (stride 24),
  indices in one ELEMENT_ARRAY_BUFFER view, uint16 for the whole document,
  or uint32 for all meshes when any mesh needs it; per-mesh blocks packed in
  descending size (SharpGLTF layout, see _Builder.pack_buffers);
* materials: pbrMetallicRoughness baseColorFactor/metallicFactor/
  roughnessFactor, doubleSided true, alphaMode when not OPAQUE; identical
  materials (name + values) are written once;
* meshes without vertices or triangles are skipped.
No external library is needed (NX ships only the Python standard library).
"""

import json
import math
import struct

from . import matrix as mx
from . import PRODUCT_NAME, __version__
from .errors import ExportError
from .scene import ALPHA_OPAQUE, ExtractedMaterial

GLB_MAGIC = 0x46546C67
CHUNK_JSON = 0x4E4F534A
CHUNK_BIN = 0x004E4942
ARRAY_BUFFER = 34962
ELEMENT_ARRAY_BUFFER = 34963
FLOAT = 5126
UNSIGNED_SHORT = 5123
UNSIGNED_INT = 5125

_F32 = struct.Struct("<f")


def _f32(value):
    return _F32.unpack(_F32.pack(value))[0]


class _Num:
    """A JSON number with a fixed textual representation."""
    __slots__ = ("text", "value")

    def __init__(self, text):
        self.text = text
        self.value = float(text)

    def __eq__(self, other):
        return float(self.value) == float(other.value if isinstance(other, _Num) else other)

    def __hash__(self):
        return hash(self.value)

    def __repr__(self):
        return self.text


def _net_exponent(text):
    """Python "1e-05" -> .NET "1E-05" (System.Text.Json number style)."""
    return text.replace("e", "E")


def _short_f32(value):
    """Shortest decimal that round-trips to the same float32 (like SharpGLTF)."""
    v = _f32(value)
    if not math.isfinite(v):
        raise ExportError("Non finite number in glTF document.")
    if v == 0:
        return 0
    if v == int(v) and abs(v) < 1e15:
        return int(v)
    for precision in range(6, 18):
        text = "%.*g" % (precision, v)
        if _f32(float(text)) == v:
            return _Num(_net_exponent(text))
    return _Num(_net_exponent(repr(v)))


def _double(value):
    """Shortest round-trip text of a double (accessor min/max), .NET style."""
    v = float(value)
    if not math.isfinite(v):
        raise ExportError("Non finite number in glTF document.")
    if v == 0:
        return 0
    if v == int(v) and abs(v) < 1e15:
        return int(v)
    return _Num(_net_exponent(repr(v)))


def _dumps(o):
    """Compact JSON serializer that keeps _Num text verbatim."""
    if isinstance(o, dict):
        return "{" + ",".join(json.dumps(str(k), ensure_ascii=False) + ":" + _dumps(v) for k, v in o.items()) + "}"
    if isinstance(o, (list, tuple)):
        return "[" + ",".join(_dumps(v) for v in o) + "]"
    if isinstance(o, _Num):
        return o.text
    if isinstance(o, bool) or o is None or isinstance(o, str):
        return json.dumps(o, ensure_ascii=False)
    if isinstance(o, int):
        return str(o)
    if isinstance(o, float):
        if not math.isfinite(o):
            raise ExportError("Non finite number in glTF document.")
        return repr(o)
    raise TypeError("Unsupported JSON value: {!r}".format(o))


def _material_doc(material):
    if material is None:
        material = ExtractedMaterial()  # SW: new ExtractedMaterial() -> grey (0.8) "Material"
    doc = {"name": material.name if material.name is not None else "Material"}
    if material.alpha_mode != ALPHA_OPAQUE:
        doc["alphaMode"] = material.alpha_mode
    doc["doubleSided"] = True
    doc["pbrMetallicRoughness"] = {
        "baseColorFactor": [_short_f32(c) for c in material.base_color],
        "metallicFactor": _short_f32(material.metallic),
        "roughnessFactor": _short_f32(material.roughness),
    }
    return doc


def _material_key(doc):
    return json.dumps(doc, sort_keys=True, default=repr)


class _Builder:
    def __init__(self, options):
        self.share = bool(getattr(options, "share_instance_meshes", False))
        self.nodes = []
        self.meshes = []
        self.materials = []
        self.material_index = {}
        self.accessors = []
        self.vertex_bytes = bytearray()
        self.index_bytes = bytearray()
        self.vertex_blocks = []     # (bytes, POSITION accessor, NORMAL accessor) in mesh order
        self.index_blocks = []      # (indices, max index, accessor) in mesh order
        self.shared_mesh_index = {}
        self.extra_mesh_nodes = []  # (parent node index, mesh index)

    # -- materials -----------------------------------------------------
    def material(self, material):
        doc = _material_doc(material)
        key = _material_key(doc)
        index = self.material_index.get(key)
        if index is None:
            index = len(self.materials)
            self.materials.append(doc)
            self.material_index[key] = index
        return index

    # -- meshes ------------------------------------------------------------
    def mesh(self, mesh):
        if mesh.vertex_count == 0 or mesh.triangle_count == 0:
            return None
        if self.share and mesh.share_key is not None:
            existing = self.shared_mesh_index.get(mesh.share_key)
            if existing is not None:
                return existing
        positions = [(_f32(p[0]), _f32(p[1]), _f32(p[2])) for p in mesh.positions]
        normals = [(_f32(n[0]), _f32(n[1]), _f32(n[2])) for n in mesh.normals]
        if len(normals) != len(positions):
            raise ExportError("Mesh '{}' has mismatched normal count.".format(mesh.name))
        pack = struct.Struct("<6f").pack
        chunk = bytearray()
        for p, n in zip(positions, normals):
            chunk += pack(p[0], p[1], p[2], n[0], n[1], n[2])
        count = len(positions)
        pos_acc = self._accessor({
            "name": "POSITION", "bufferView": 0, "byteOffset": 0, "componentType": FLOAT,
            "count": count, "max": [_double(max(p[i] for p in positions)) for i in range(3)],
            "min": [_double(min(p[i] for p in positions)) for i in range(3)], "type": "VEC3"})
        nor_acc = self._accessor({
            "name": "NORMAL", "bufferView": 0, "byteOffset": 12, "componentType": FLOAT,
            "count": count, "max": [_double(max(n[i] for n in normals)) for i in range(3)],
            "min": [_double(min(n[i] for n in normals)) for i in range(3)], "type": "VEC3"})
        max_index = max(mesh.indices)
        if max_index >= count or min(mesh.indices) < 0:
            raise ExportError("Mesh '{}' has an out of range index.".format(mesh.name))
        idx_acc = self._accessor({"bufferView": 1, "byteOffset": 0, "componentType": UNSIGNED_SHORT,
                                  "count": len(mesh.indices), "type": "SCALAR"})
        self.vertex_blocks.append((bytes(chunk), pos_acc, nor_acc))
        self.index_blocks.append((list(mesh.indices), max_index, idx_acc))
        primitive = {"attributes": {"POSITION": pos_acc, "NORMAL": nor_acc}, "indices": idx_acc,
                     "material": self.material(mesh.material)}
        index = len(self.meshes)
        self.meshes.append({"name": mesh.name if mesh.name is not None else "Mesh", "primitives": [primitive]})
        if self.share and mesh.share_key is not None:
            self.shared_mesh_index[mesh.share_key] = index
        return index

    def _accessor(self, doc):
        self.accessors.append(doc)
        return len(self.accessors) - 1

    # -- binary packing ----------------------------------------------------
    def pack_buffers(self):
        """Lay out the binary buffer exactly like SharpGLTF 1.0.6 does.

        Verified with tests/reference_sw: one index component type for the
        whole document (uint32 as soon as any mesh needs it), and the per-mesh
        vertex / index blocks of each merged buffer view are placed in
        descending byte size (stable for equal sizes).  Accessor indices keep
        the mesh order; only their byteOffsets follow the packed layout.
        """
        wide = any(max_index >= 65535 for _, max_index, _ in self.index_blocks)
        component, fmt = (UNSIGNED_INT, "I") if wide else (UNSIGNED_SHORT, "H")
        vertex_bytes = bytearray()
        for chunk, pos_acc, nor_acc in sorted(self.vertex_blocks, key=lambda b: -len(b[0])):
            self._set_offset(pos_acc, len(vertex_bytes))
            self._set_offset(nor_acc, len(vertex_bytes) + 12)
            vertex_bytes += chunk
        index_bytes = bytearray()
        for indices, _, acc in sorted(self.index_blocks, key=lambda b: -len(b[0])):
            self._set_offset(acc, len(index_bytes))
            self.accessors[acc]["componentType"] = component
            index_bytes += struct.pack("<%d%s" % (len(indices), fmt), *indices)
        self.vertex_bytes = vertex_bytes
        self.index_bytes = index_bytes

    def _set_offset(self, accessor_index, offset):
        doc = self.accessors[accessor_index]
        if offset:
            doc["byteOffset"] = offset   # key keeps its original position
        else:
            doc.pop("byteOffset", None)

    # -- nodes -------------------------------------------------------------
    def node(self, node, default_name):
        index = len(self.nodes)
        doc = {"name": node.name if node.name is not None else default_name}
        self.nodes.append(doc)
        # SharpGLTF (AddNodeRecursive) adds the node meshes before recursing
        # into the children, so mesh indices follow that order.
        first = True
        for mesh in node.meshes:
            mesh_index = self.mesh(mesh)
            if mesh_index is None:
                continue
            if first:
                doc["mesh"] = mesh_index
                first = False
            else:
                self.extra_mesh_nodes.append((index, mesh_index))
        children = []
        for child in node.children:
            children.append(self.node(child, "Component"))
        if children:
            doc["children"] = children
        transform = tuple(node.local_transform)
        if len(transform) != 16 or not all(math.isfinite(v) for v in transform):
            raise ExportError("Node '{}' has an invalid transform.".format(doc["name"]))
        if not mx.is_identity(transform):
            doc["matrix"] = [_short_f32(v) for v in transform]
        return index

    def finish_extra_nodes(self):
        for parent, mesh_index in self.extra_mesh_nodes:
            child = len(self.nodes)
            self.nodes.append({"mesh": mesh_index})
            self.nodes[parent].setdefault("children", []).append(child)
        order = ("name", "children", "matrix", "mesh")  # SharpGLTF property order
        self.nodes = [{k: n[k] for k in order if k in n} for n in self.nodes]


def build_gltf(scene, options=None):
    """Return (gltf_dict, binary_buffer) for an ExtractedScene."""
    if scene is None:
        raise ExportError("The scene is missing.")
    if scene.root is None:
        raise ExportError("The scene contains no root node.")
    b = _Builder(options)
    b.node(scene.root, "Root")
    b.finish_extra_nodes()
    b.pack_buffers()
    vertex_len = len(b.vertex_bytes)
    binary = bytes(b.vertex_bytes) + bytes(b.index_bytes)
    views = []
    if vertex_len:
        views.append({"buffer": 0, "byteLength": vertex_len, "byteStride": 24, "target": ARRAY_BUFFER})
        views.append({"buffer": 0, "byteLength": len(b.index_bytes), "byteOffset": vertex_len,
                      "target": ELEMENT_ARRAY_BUFFER})
    # Top level key order as written by SharpGLTF (System.Text.Json).
    doc = {"asset": {"copyright": "", "generator": "{} {}".format(PRODUCT_NAME, __version__), "version": "2.0"}}
    if b.meshes:
        doc["accessors"] = b.accessors
        doc["bufferViews"] = views
        doc["buffers"] = [{"byteLength": len(binary) + (4 - len(binary) % 4) % 4}]  # SharpGLTF: padded to 4
    if b.materials:
        doc["materials"] = b.materials
    if b.meshes:
        doc["meshes"] = b.meshes
    doc["nodes"] = b.nodes
    doc["scene"] = 0
    doc["scenes"] = [{"nodes": [0]}]
    return doc, binary


def encode_glb(doc, binary):
    json_bytes = _dumps(doc).encode("utf-8")
    json_bytes += b" " * ((4 - len(json_bytes) % 4) % 4)
    bin_bytes = binary + b"\x00" * ((4 - len(binary) % 4) % 4)
    total = 12 + 8 + len(json_bytes) + (8 + len(bin_bytes) if bin_bytes else 0)
    out = bytearray(struct.pack("<III", GLB_MAGIC, 2, total))
    out += struct.pack("<II", len(json_bytes), CHUNK_JSON) + json_bytes
    if bin_bytes:
        out += struct.pack("<II", len(bin_bytes), CHUNK_BIN) + bin_bytes
    return bytes(out)


def build_glb(scene, options=None):
    doc, binary = build_gltf(scene, options)
    return encode_glb(doc, binary)
