"""Tests for unit scaling and the optional Z-up -> Y-up conversion."""

import math
import unittest

import support  # noqa: F401

from synode_glb.core import matrix as mx
from synode_glb.core.coordinate_converter import ConversionOptions, convert_fusion_scene_to_gltf
from synode_glb.core.scene import IDENTITY, ExtractedMesh, ExtractedNode, ExtractedScene


def translation(x, y, z):
    return (1.0, 0, 0, x, 0, 1.0, 0, y, 0, 0, 1.0, z, 0, 0, 0, 1.0)


def make_scene():
    mesh = ExtractedMesh(
        name="Part_Material_0",
        positions=[0, 0, 0, 10, 0, 0, 0, 0, 10],
        normals=[0, -2, 0, 0, -1, 0, 0, -1, 0],
        indices=[0, 1, 2],
    )
    child = ExtractedNode(name="Part:1", local_transform=translation(100, 0, 50), meshes=[mesh])
    return ExtractedScene(root=ExtractedNode(name="Doc", children=[child]))


def world_points(node_chain):
    m = IDENTITY
    for node in node_chain:
        m = mx.multiply(m, node.local_transform)
    mesh = node_chain[-1].meshes[0]
    p = mesh.positions
    return [mx.transform_point(m, p[i], p[i + 1], p[i + 2]) for i in range(0, len(p), 3)]


class CoordinateConverterTests(unittest.TestCase):
    def test_scales_positions_and_translations(self):
        out = convert_fusion_scene_to_gltf(make_scene(), ConversionOptions(0.01, False))
        child = out.root.children[0]
        self.assertEqual(out.root.local_transform, IDENTITY)
        self.assertAlmostEqual(child.local_transform[3], 1.0)
        self.assertAlmostEqual(child.local_transform[11], 0.5)
        self.assertAlmostEqual(child.meshes[0].positions[3], 0.1)
        # normals are unit length and not scaled
        self.assertAlmostEqual(child.meshes[0].normals[1], -1.0)
        self.assertEqual(child.meshes[0].indices, [0, 1, 2])

    def test_source_not_modified(self):
        scene = make_scene()
        convert_fusion_scene_to_gltf(scene, ConversionOptions(0.01, True))
        self.assertEqual(scene.root.children[0].meshes[0].positions[3], 10)
        self.assertEqual(scene.root.children[0].local_transform[3], 100)

    def test_z_up_to_y_up_world_positions(self):
        source = make_scene()
        out = convert_fusion_scene_to_gltf(source, ConversionOptions(0.01, True))
        self.assertEqual(out.root.local_transform, IDENTITY)
        src_pts = world_points([source.root, source.root.children[0]])
        out_pts = world_points([out.root, out.root.children[0]])
        for (x, y, z), (ox, oy, oz) in zip(src_pts, out_pts):
            # (x, y, z) Z-up cm -> (x, z, -y) Y-up m
            self.assertAlmostEqual(ox, x * 0.01)
            self.assertAlmostEqual(oy, z * 0.01)
            self.assertAlmostEqual(oz, -y * 0.01)
        n = out.root.children[0].meshes[0].normals
        self.assertAlmostEqual(n[2], 1.0)  # -Y normal becomes +Z

    def test_rotated_occurrence_z_up(self):
        c, s = math.cos(0.7), math.sin(0.7)
        rotated = (c, -s, 0, 3, s, c, 0, 4, 0, 0, 1, 5, 0, 0, 0, 1)
        scene = make_scene()
        scene.root.children[0].local_transform = rotated
        out = convert_fusion_scene_to_gltf(scene, ConversionOptions(1.0, True))
        src_pts = world_points([scene.root, scene.root.children[0]])
        out_pts = world_points([out.root, out.root.children[0]])
        for (x, y, z), (ox, oy, oz) in zip(src_pts, out_pts):
            self.assertAlmostEqual(ox, x)
            self.assertAlmostEqual(oy, z)
            self.assertAlmostEqual(oz, -y)

    def test_invalid_arguments(self):
        with self.assertRaises(ValueError):
            convert_fusion_scene_to_gltf(make_scene(), ConversionOptions(0.0, False))
        with self.assertRaises(ValueError):
            convert_fusion_scene_to_gltf(make_scene(), ConversionOptions(float("inf"), False))
        with self.assertRaises(ValueError):
            convert_fusion_scene_to_gltf(ExtractedScene(), ConversionOptions())
        with self.assertRaises(ValueError):
            convert_fusion_scene_to_gltf(None, ConversionOptions())


if __name__ == "__main__":
    unittest.main()
