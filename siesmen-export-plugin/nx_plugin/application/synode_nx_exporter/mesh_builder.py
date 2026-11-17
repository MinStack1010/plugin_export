"""Triangle accumulation, winding fix and mesh validation.

Reproduces SolidWorksGeometryExtractor.AddTessellationFacet +
SharpGLTF PrimitiveBuilder.AddTriangle behaviour:
* normals are normalised;
* degenerate triangles (|cross|^2 < 1e-20) are skipped;
* winding is flipped when the geometric normal opposes the vertex normals;
* identical (position, normal) vertices are shared (float32 identity, like
  SharpGLTF's vertex dictionary);
* triangles that collapse after vertex sharing are dropped.
"""

import math
import struct

from .errors import MeshValidationError
from .scene import ExtractedMesh

DEGENERATE_EPSILON = 1e-20
_F32 = struct.Struct("<f")


def _f32(value):
    return _F32.unpack(_F32.pack(value))[0]


def _sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def normalize(v):
    length = math.sqrt(_dot(v, v))
    if length < 1e-10:
        return None
    return (v[0] / length, v[1] / length, v[2] / length)


class MeshAccumulator:
    def __init__(self, name, material):
        self.mesh = ExtractedMesh(name=name, material=material)
        self._vertex_map = {}
        self.degenerate_count = 0
        self.zero_normal_count = 0

    def _vertex(self, p, n):
        key = (_f32(p[0]), _f32(p[1]), _f32(p[2]), _f32(n[0]), _f32(n[1]), _f32(n[2]))
        index = self._vertex_map.get(key)
        if index is None:
            index = len(self.mesh.positions)
            self._vertex_map[key] = index
            self.mesh.positions.append(key[0:3])
            self.mesh.normals.append(key[3:6])
        return index

    def add_triangle(self, p0, p1, p2, n0, n1, n2):
        """Add one triangle; positions in metres, normals any length."""
        geometric = _cross(_sub(p1, p0), _sub(p2, p0))
        if _dot(geometric, geometric) < DEGENERATE_EPSILON:
            self.degenerate_count += 1
            return False
        unit_geometric = normalize(geometric)
        normals = []
        for n in (n0, n1, n2):
            un = normalize(n) if n is not None else None
            if un is None:
                # SW throws on a zero normal; NX facets can rarely contain one
                # (singular points) so the facet normal is used instead.
                self.zero_normal_count += 1
                un = unit_geometric
            normals.append(un)
        average = (normals[0][0] + normals[1][0] + normals[2][0],
                   normals[0][1] + normals[1][1] + normals[2][1],
                   normals[0][2] + normals[1][2] + normals[2][2])
        if _dot(geometric, average) < 0:
            p1, p2 = p2, p1
            normals[1], normals[2] = normals[2], normals[1]
        i0 = self._vertex(p0, normals[0])
        i1 = self._vertex(p1, normals[1])
        i2 = self._vertex(p2, normals[2])
        if i0 == i1 or i1 == i2 or i0 == i2:
            self.degenerate_count += 1
            return False
        self.mesh.indices.extend((i0, i1, i2))
        return True


def validate_mesh(mesh):
    """Port of ValidateExtractedMesh plus finiteness checks."""
    if mesh.vertex_count == 0:
        raise MeshValidationError("Mesh '{}' has no vertices.".format(mesh.name))
    if mesh.triangle_count == 0:
        raise MeshValidationError("Mesh '{}' has no triangles.".format(mesh.name))
    if len(mesh.normals) != len(mesh.positions):
        raise MeshValidationError("Mesh '{}' has mismatched normal count.".format(mesh.name))
    if len(mesh.indices) % 3 != 0:
        raise MeshValidationError("Mesh '{}' has an invalid index count.".format(mesh.name))
    count = mesh.vertex_count
    for index in mesh.indices:
        if index < 0 or index >= count:
            raise MeshValidationError("Mesh '{}' has an out of range index {}.".format(mesh.name, index))
    for p in mesh.positions:
        if not all(math.isfinite(c) for c in p):
            raise MeshValidationError("Mesh '{}' has a non finite position.".format(mesh.name))
    for n in mesh.normals:
        if not all(math.isfinite(c) for c in n):
            raise MeshValidationError("Mesh '{}' has a non finite normal.".format(mesh.name))
