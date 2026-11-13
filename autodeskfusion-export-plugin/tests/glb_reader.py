"""Minimal GLB reader / validator used by the tests (independent from the writer)."""

import json
import math
import struct
from array import array

COMPONENT_FORMATS = {5123: "H", 5125: "I", 5126: "f"}
TYPE_SIZES = {"SCALAR": 1, "VEC3": 3, "VEC4": 4, "MAT4": 16}


class Glb:
    def __init__(self, data: bytes):
        magic, version, length = struct.unpack_from("<III", data, 0)
        assert magic == 0x46546C67, "bad magic"
        assert version == 2, "bad version"
        assert length == len(data), "header length {} != file length {}".format(length, len(data))
        offset = 12
        json_len, json_type = struct.unpack_from("<II", data, offset)
        assert json_type == 0x4E4F534A, "first chunk must be JSON"
        assert json_len % 4 == 0, "JSON chunk must be 4-byte aligned"
        self.json = json.loads(data[offset + 8:offset + 8 + json_len].decode("utf-8"))
        offset += 8 + json_len
        self.bin = b""
        if offset < len(data):
            bin_len, bin_type = struct.unpack_from("<II", data, offset)
            assert bin_type == 0x004E4942, "second chunk must be BIN"
            assert bin_len % 4 == 0, "BIN chunk must be 4-byte aligned"
            self.bin = data[offset + 8:offset + 8 + bin_len]
            offset += 8 + bin_len
        assert offset == len(data), "trailing bytes"

    def accessor(self, index):
        acc = self.json["accessors"][index]
        view = self.json["bufferViews"][acc["bufferView"]]
        fmt = COMPONENT_FORMATS[acc["componentType"]]
        n = acc["count"] * TYPE_SIZES[acc["type"]]
        start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
        values = array(fmt)
        values.frombytes(self.bin[start:start + n * values.itemsize])
        return list(values)

    def validate(self):
        g = self.json
        assert g["asset"]["version"] == "2.0"
        nodes = g.get("nodes", [])
        seen_children = set()
        for node in nodes:
            for child in node.get("children", []):
                assert child not in seen_children, "node {} has two parents".format(child)
                seen_children.add(child)
        roots = g["scenes"][g.get("scene", 0)]["nodes"]
        for r in roots:
            assert r not in seen_children, "scene root is also a child"
        if "buffers" in g:
            assert g["buffers"][0]["byteLength"] == len(self.bin)
        for view in g.get("bufferViews", []):
            assert view.get("byteOffset", 0) % 4 == 0
            assert view.get("byteOffset", 0) + view["byteLength"] <= len(self.bin)
        for mesh in g.get("meshes", []):
            for prim in mesh["primitives"]:
                pos = self.accessor(prim["attributes"]["POSITION"])
                nrm = self.accessor(prim["attributes"]["NORMAL"])
                idx = self.accessor(prim["indices"])
                count = len(pos) // 3
                assert len(nrm) == len(pos)
                assert len(idx) % 3 == 0
                assert all(0 <= i < count for i in idx)
                acc = g["accessors"][prim["attributes"]["POSITION"]]
                for axis in range(3):
                    values = pos[axis::3]
                    assert math.isclose(acc["min"][axis], min(values), rel_tol=0, abs_tol=1e-6)
                    assert math.isclose(acc["max"][axis], max(values), rel_tol=0, abs_tol=1e-6)
                for i in range(0, len(nrm), 3):
                    length = math.sqrt(nrm[i] ** 2 + nrm[i + 1] ** 2 + nrm[i + 2] ** 2)
                    assert abs(length - 1.0) < 1e-3, "normal not unit length"
                if "material" in prim:
                    assert 0 <= prim["material"] < len(g["materials"])
        return True

    # -- helpers for assertions -------------------------------------------------
    def node_by_name(self, name):
        for i, node in enumerate(self.json["nodes"]):
            if node.get("name") == name:
                return i, node
        raise KeyError(name)

    def local_matrix(self, node):
        if "matrix" in node:
            m = node["matrix"]
            return [m[c * 4 + r] for r in range(4) for c in range(4)]
        t = node.get("translation", [0, 0, 0])
        x, y, z, w = node.get("rotation", [0, 0, 0, 1])
        s = node.get("scale", [1, 1, 1])
        r = [
            [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
            [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
            [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)],
        ]
        return [
            r[0][0] * s[0], r[0][1] * s[1], r[0][2] * s[2], t[0],
            r[1][0] * s[0], r[1][1] * s[1], r[1][2] * s[2], t[1],
            r[2][0] * s[0], r[2][1] * s[1], r[2][2] * s[2], t[2],
            0, 0, 0, 1,
        ]

    def world_matrix(self, node_index):
        parents = {}
        for i, node in enumerate(self.json["nodes"]):
            for c in node.get("children", []):
                parents[c] = i
        chain = []
        current = node_index
        while current is not None:
            chain.append(current)
            current = parents.get(current)
        m = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]
        for index in reversed(chain):
            m = _mul(m, self.local_matrix(self.json["nodes"][index]))
        return m

    def world_positions(self, node_index):
        node = self.json["nodes"][node_index]
        mesh = self.json["meshes"][node["mesh"]]
        pos = self.accessor(mesh["primitives"][0]["attributes"]["POSITION"])
        m = self.world_matrix(node_index)
        out = []
        for i in range(0, len(pos), 3):
            x, y, z = pos[i:i + 3]
            out.append((
                m[0] * x + m[1] * y + m[2] * z + m[3],
                m[4] * x + m[5] * y + m[6] * z + m[7],
                m[8] * x + m[9] * y + m[10] * z + m[11],
            ))
        return out


def _mul(a, b):
    return [sum(a[r * 4 + k] * b[k * 4 + c] for k in range(4)) for r in range(4) for c in range(4)]
