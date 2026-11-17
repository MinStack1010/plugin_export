"""Minimal, dependency-free GLB reader/validator used by the tests.

It parses a binary glTF 2.0 file, validates the container and the parts of
the glTF schema that the exporter uses, and decodes accessors so geometry
can be compared numerically.
"""

import json
import struct

GLB_MAGIC = 0x46546C67
CHUNK_JSON = 0x4E4F534A
CHUNK_BIN = 0x004E4942

_COMPONENT = {5120: ("b", 1), 5121: ("B", 1), 5122: ("h", 2), 5123: ("H", 2), 5125: ("I", 4), 5126: ("f", 4)}
_TYPE_SIZE = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


class GlbError(Exception):
    pass


class Glb:
    def __init__(self, gltf, binary):
        self.gltf = gltf
        self.bin = binary

    def accessor(self, index):
        acc = self.gltf["accessors"][index]
        fmt, size = _COMPONENT[acc["componentType"]]
        ncomp = _TYPE_SIZE[acc["type"]]
        view = self.gltf["bufferViews"][acc["bufferView"]]
        stride = view.get("byteStride", size * ncomp)
        base = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
        out = []
        for i in range(acc["count"]):
            start = base + i * stride
            values = struct.unpack_from("<" + fmt * ncomp, self.bin, start)
            out.append(values if ncomp > 1 else values[0])
        return out


def read_glb(path_or_bytes):
    data = path_or_bytes
    if isinstance(path_or_bytes, str):
        with open(path_or_bytes, "rb") as handle:
            data = handle.read()
    if len(data) < 20:
        raise GlbError("File too small for GLB.")
    magic, version, length = struct.unpack_from("<III", data, 0)
    if magic != GLB_MAGIC:
        raise GlbError("Bad GLB magic.")
    if version != 2:
        raise GlbError("Unsupported GLB version {}.".format(version))
    if length != len(data):
        raise GlbError("GLB length header {} != file size {}.".format(length, len(data)))
    offset = 12
    gltf = None
    binary = b""
    first = True
    while offset < length:
        chunk_len, chunk_type = struct.unpack_from("<II", data, offset)
        if chunk_len % 4 != 0:
            raise GlbError("Chunk length not 4-byte aligned.")
        payload = data[offset + 8: offset + 8 + chunk_len]
        if first:
            if chunk_type != CHUNK_JSON:
                raise GlbError("First chunk must be JSON.")
            gltf = json.loads(payload.decode("utf-8"))
            first = False
        elif chunk_type == CHUNK_BIN:
            binary = payload
        offset += 8 + chunk_len
    if gltf is None:
        raise GlbError("JSON chunk missing.")
    glb = Glb(gltf, binary)
    validate(glb)
    return glb


def validate(glb):
    g = glb.gltf
    if g.get("asset", {}).get("version") != "2.0":
        raise GlbError("asset.version must be 2.0")
    nodes = g.get("nodes", [])
    meshes = g.get("meshes", [])
    accessors = g.get("accessors", [])
    views = g.get("bufferViews", [])
    materials = g.get("materials", [])
    buffers = g.get("buffers", [])
    if buffers:
        if buffers[0].get("byteLength", 0) > len(glb.bin):
            raise GlbError("buffer[0].byteLength exceeds BIN chunk.")
    for v in views:
        if v.get("buffer", 0) >= len(buffers):
            raise GlbError("bufferView references missing buffer.")
        if v.get("byteOffset", 0) + v["byteLength"] > buffers[v.get("buffer", 0)]["byteLength"]:
            raise GlbError("bufferView out of buffer range.")
    for a in accessors:
        if a["bufferView"] >= len(views):
            raise GlbError("accessor references missing bufferView.")
        _, size = _COMPONENT[a["componentType"]]
        view = views[a["bufferView"]]
        stride = view.get("byteStride", size * _TYPE_SIZE[a["type"]])
        end = a.get("byteOffset", 0) + stride * (a["count"] - 1) + size * _TYPE_SIZE[a["type"]]
        if end > view["byteLength"]:
            raise GlbError("accessor exceeds bufferView.")
    seen_children = set()
    for i, n in enumerate(nodes):
        for c in n.get("children", []):
            if c >= len(nodes) or c in seen_children or c == i:
                raise GlbError("Invalid node hierarchy at node {}.".format(i))
            seen_children.add(c)
        if "mesh" in n and n["mesh"] >= len(meshes):
            raise GlbError("node references missing mesh.")
        if "matrix" in n and len(n["matrix"]) != 16:
            raise GlbError("node matrix must have 16 values.")
    for m in meshes:
        for p in m["primitives"]:
            attrs = p["attributes"]
            pos = accessors[attrs["POSITION"]]
            if "min" not in pos or "max" not in pos:
                raise GlbError("POSITION accessor requires min/max.")
            if "material" in p and p["material"] >= len(materials):
                raise GlbError("primitive references missing material.")
            if "indices" in p:
                count = pos["count"]
                for idx in glb.accessor(p["indices"]):
                    if idx >= count:
                        raise GlbError("index out of range.")


def world_matrices(glb):
    """Return {node_index: world matrix (16, column-major)} for the default scene."""
    from synode_nx_exporter import matrix as mx
    g = glb.gltf
    result = {}

    def local(n):
        if "matrix" in n:
            return tuple(n["matrix"])
        t = n.get("translation", (0, 0, 0))
        r = n.get("rotation", (0, 0, 0, 1))
        s = n.get("scale", (1, 1, 1))
        return mx.compose(t, r, s)

    def walk(i, parent):
        w = mx.multiply(parent, local(g["nodes"][i]))
        result[i] = w
        for c in g["nodes"][i].get("children", []):
            walk(c, w)

    scene = g["scenes"][g.get("scene", 0)]
    for root in scene["nodes"]:
        walk(root, mx.IDENTITY)
    return result


def summarize(glb):
    """Semantic summary independent of buffer layout: list of world-space
    triangles grouped per node path with material info."""
    from synode_nx_exporter import matrix as mx
    g = glb.gltf
    worlds = world_matrices(glb)
    parents = {}
    for i, n in enumerate(g.get("nodes", [])):
        for c in n.get("children", []):
            parents[c] = i

    def path(i):
        names = []
        while i is not None:
            names.append(g["nodes"][i].get("name", ""))
            i = parents.get(i)
        return "/".join(reversed(names))

    out = []
    for i, n in enumerate(g.get("nodes", [])):
        if "mesh" not in n:
            continue
        mesh = g["meshes"][n["mesh"]]
        for p in mesh["primitives"]:
            pos = glb.accessor(p["attributes"]["POSITION"])
            nor = glb.accessor(p["attributes"]["NORMAL"])
            idx = glb.accessor(p["indices"]) if "indices" in p else list(range(len(pos)))
            mat = g["materials"][p["material"]] if "material" in p else None
            out.append({"node": path(i), "mesh_name": mesh.get("name"), "world": worlds[i],
                        "positions": pos, "normals": nor, "indices": idx, "material": mat})
    return out
