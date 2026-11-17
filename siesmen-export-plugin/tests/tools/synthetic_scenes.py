"""Synthetic scenes (test data) built with the plugin's own MeshAccumulator.

These are NOT exported from NX; they are deterministic inputs used to compare
the NX plugin GLB writer with the original SOLIDWORKS writer (SharpGLTF).
"""

import math

from synode_nx_exporter import matrix
from synode_nx_exporter.materials import material_from_display_color
from synode_nx_exporter.mesh_builder import MeshAccumulator
from synode_nx_exporter.scene import ExtractedNode, ExtractedScene, ExtractedMaterial

# 6 faces of a unit cube: (normal, 4 corners counter-clockwise seen from outside)
_CUBE_FACES = [
    ((1, 0, 0), [(1, 0, 0), (1, 1, 0), (1, 1, 1), (1, 0, 1)]),
    ((-1, 0, 0), [(0, 0, 0), (0, 0, 1), (0, 1, 1), (0, 1, 0)]),
    ((0, 1, 0), [(0, 1, 0), (0, 1, 1), (1, 1, 1), (1, 1, 0)]),
    ((0, -1, 0), [(0, 0, 0), (1, 0, 0), (1, 0, 1), (0, 0, 1)]),
    ((0, 0, 1), [(0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1)]),
    ((0, 0, -1), [(0, 0, 0), (0, 1, 0), (1, 1, 0), (1, 0, 0)]),
]


def box_triangles(size=(0.1, 0.05, 0.02), offset=(0.0, 0.0, 0.0)):
    tris = []
    for normal, quad in _CUBE_FACES:
        q = [tuple(offset[i] + c[i] * size[i] for i in range(3)) for c in quad]
        tris.append(((q[0], q[1], q[2]), (normal,) * 3))
        tris.append(((q[0], q[2], q[3]), (normal,) * 3))
    return tris


def cylinder_triangles(radius=0.02, height=0.05, segments=24):
    tris = []
    for i in range(segments):
        a0 = 2 * math.pi * i / segments
        a1 = 2 * math.pi * (i + 1) / segments
        p0 = (radius * math.cos(a0), radius * math.sin(a0), 0.0)
        p1 = (radius * math.cos(a1), radius * math.sin(a1), 0.0)
        p2 = (p1[0], p1[1], height)
        p3 = (p0[0], p0[1], height)
        n0 = (math.cos(a0), math.sin(a0), 0.0)
        n1 = (math.cos(a1), math.sin(a1), 0.0)
        tris.append(((p0, p1, p2), (n0, n1, n1)))
        tris.append(((p0, p2, p3), (n0, n1, n0)))
        tris.append((((0, 0, height), p3, p2), ((0, 0, 1),) * 3))
        tris.append((((0, 0, 0), p1, p0), ((0, 0, -1),) * 3))
    return tris


def mesh_from_triangles(name, material, triangles):
    acc = MeshAccumulator(name, material)
    for (p0, p1, p2), (n0, n1, n2) in triangles:
        acc.add_triangle(p0, p1, p2, n0, n1, n2)
    return acc.mesh


def rotation_z(deg, t=(0, 0, 0)):
    a = math.radians(deg)
    return matrix.from_axes((math.cos(a), math.sin(a), 0), (-math.sin(a), math.cos(a), 0), (0, 0, 1), t)


def rotation_x(deg, t=(0, 0, 0)):
    a = math.radians(deg)
    return matrix.from_axes((1, 0, 0), (0, math.cos(a), math.sin(a)), (0, -math.sin(a), math.cos(a)), t)


def red():
    return material_from_display_color("Body1_Material", (1.0, 0.0, 0.0))


def blue_translucent():
    return material_from_display_color("Body2_Face_Material", (0.0, 0.0, 1.0), translucency_percent=40)


def assembly_scene():
    """Main Assembly / A(A1, A2) / B(B1) / C, shared part instances, multi-body,
    rotations, translucent face material and a default (None) material mesh."""
    root = ExtractedNode(name="main_assembly", local_transform=matrix.IDENTITY)
    a = ExtractedNode(name="sub_a-1", local_transform=rotation_z(90, (0.2, 0.0, 0.0)))
    a1 = ExtractedNode(name="sub_a-1/bolt-1", local_transform=matrix.from_axes((1, 0, 0), (0, 1, 0), (0, 0, 1), (0.0, 0.03, 0.0)))
    a2 = ExtractedNode(name="sub_a-1/bolt-2", local_transform=rotation_x(30, (0.0, -0.03, 0.01)))
    for n in (a1, a2):
        n.meshes.append(mesh_from_triangles(n.name + "_Material_0", red(), cylinder_triangles()))
    b = ExtractedNode(name="sub_b-1", local_transform=rotation_x(-90, (0.0, 0.3, 0.05)))
    b1 = ExtractedNode(name="sub_b-1/plate-1", local_transform=matrix.IDENTITY)
    b1.meshes.append(mesh_from_triangles(b1.name + "_Material_0", red(), box_triangles()))
    b1.meshes.append(mesh_from_triangles(b1.name + "_Material_1", blue_translucent(),
                                         box_triangles((0.02, 0.02, 0.02), (0.0, 0.0, 0.02))))
    c = ExtractedNode(name="bracket-1", local_transform=matrix.from_axes((0, 1, 0), (-1, 0, 0), (0, 0, 1), (-0.1, -0.1, 0.0)))
    c.meshes.append(mesh_from_triangles(c.name + "_Material_0", None, box_triangles((0.03, 0.03, 0.01))))
    # two bodies in one part -> two meshes, same name pattern as SW (index restarts per body)
    c.meshes.append(mesh_from_triangles(c.name + "_Material_0", ExtractedMaterial(name="Body2_Material", base_color=(0.2, 0.7, 0.2, 1.0)),
                                        box_triangles((0.01, 0.01, 0.05), (0.04, 0.0, 0.0))))
    a.children.extend([a1, a2])
    b.children.append(b1)
    root.children.extend([a, b, c])
    return ExtractedScene(root=root)


def part_scene():
    root = ExtractedNode(name="single_part", local_transform=matrix.IDENTITY)
    root.meshes.append(mesh_from_triangles("single_part_Material_0", red(), box_triangles()))
    return ExtractedScene(root=root)


def large_mesh_scene(segments=12000):
    """Mesh with > 65535 vertices (forces 32-bit indices)."""
    root = ExtractedNode(name="large", local_transform=matrix.IDENTITY)
    root.meshes.append(mesh_from_triangles("large_Material_0", red(), cylinder_triangles(segments=segments)))
    return ExtractedScene(root=root)


def strip_triangles(count, offset=0.0):
    """`count` separate triangles (odd counts exercise 2-byte index packing)."""
    tris = []
    for i in range(count):
        x = offset + i * 0.01
        tris.append((((x, 0, 0), (x + 0.005, 0, 0), (x, 0.005, 0)), ((0, 0, 1),) * 3))
    return tris


def packing_scene(with_large=False):
    """Meshes of different sizes in a nested hierarchy (several meshes per node,
    equal sizes, odd triangle counts, optional > 65535 vertex mesh) to check
    the binary buffer layout (block order, index width) against SharpGLTF."""
    root = ExtractedNode(name="packing", local_transform=matrix.IDENTITY)
    root.meshes.append(mesh_from_triangles("packing_Material_0", red(), strip_triangles(2)))
    p = ExtractedNode(name="p-1", local_transform=rotation_z(30, (0.1, 0, 0)))
    for i, n in enumerate((1, 5, 3, 3)):
        p.meshes.append(mesh_from_triangles("p-1_Material_%d" % i, red(), strip_triangles(n, i)))
    c = ExtractedNode(name="p-1/c-1", local_transform=rotation_x(45))
    c.meshes.append(mesh_from_triangles("p-1/c-1_Material_0", blue_translucent(), cylinder_triangles(segments=7)))
    c.meshes.append(mesh_from_triangles("p-1/c-1_Material_1", red(), strip_triangles(3)))
    p.children.append(c)
    q = ExtractedNode(name="q-1", local_transform=matrix.IDENTITY)
    q.meshes.append(mesh_from_triangles("q-1_Material_0", red(),
                                        cylinder_triangles(segments=20000 if with_large else 9)))
    root.children.extend([p, q])
    return ExtractedScene(root=root)
