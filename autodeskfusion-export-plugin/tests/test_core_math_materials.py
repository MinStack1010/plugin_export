"""Tests for matrix helpers and material conversion (CAD independent)."""

import math
import unittest

import support  # noqa: F401  (sets sys.path)

from synode_glb.core import matrix as mx
from synode_glb.core.materials import build_material, materials_equivalent, srgb_to_linear
from synode_glb.core.scene import IDENTITY, AlphaMode, ExtractedMaterial


def rot_z(deg, t=(0.0, 0.0, 0.0)):
    c, s = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    return (c, -s, 0.0, t[0], s, c, 0.0, t[1], 0.0, 0.0, 1.0, t[2], 0.0, 0.0, 0.0, 1.0)


class MatrixTests(unittest.TestCase):
    def assertMatrixAlmostEqual(self, a, b, places=9):
        for x, y in zip(a, b):
            self.assertAlmostEqual(x, y, places=places)

    def test_multiply_identity(self):
        m = rot_z(30, (1, 2, 3))
        self.assertMatrixAlmostEqual(mx.multiply(m, IDENTITY), m)
        self.assertMatrixAlmostEqual(mx.multiply(IDENTITY, m), m)

    def test_invert(self):
        m = rot_z(47, (5, -2, 9))
        self.assertMatrixAlmostEqual(mx.multiply(m, mx.invert(m)), IDENTITY)

    def test_invert_singular(self):
        with self.assertRaises(ValueError):
            mx.invert((0.0,) * 16)

    def test_transform_point_uses_translation(self):
        self.assertEqual(mx.transform_point(rot_z(0, (1, 2, 3)), 1, 1, 1), (2, 3, 4))
        self.assertEqual(mx.transform_vector(rot_z(0, (1, 2, 3)), 1, 1, 1), (1, 1, 1))

    def test_column_major(self):
        m = rot_z(0, (7, 8, 9))
        cm = mx.to_gltf_column_major(m)
        self.assertEqual(cm[12:15], [7.0, 8.0, 9.0])

    def test_decompose_rotation_translation(self):
        trs = mx.decompose_trs(rot_z(90, (1, 2, 3)))
        self.assertIsNotNone(trs)
        t, q, s = trs
        self.assertEqual(t, (1, 2, 3))
        self.assertAlmostEqual(q[2], math.sin(math.radians(45)))
        self.assertAlmostEqual(q[3], math.cos(math.radians(45)))
        for v in s:
            self.assertAlmostEqual(v, 1.0)

    def test_decompose_rejects_mirror(self):
        mirror = (-1.0, 0, 0, 0, 0, 1.0, 0, 0, 0, 0, 1.0, 0, 0, 0, 0, 1.0)
        self.assertIsNone(mx.decompose_trs(mirror))

    def test_decompose_rejects_shear(self):
        shear = (1.0, 0.5, 0, 0, 0, 1.0, 0, 0, 0, 0, 1.0, 0, 0, 0, 0, 1.0)
        self.assertIsNone(mx.decompose_trs(shear))


class MaterialTests(unittest.TestCase):
    def test_srgb_to_linear(self):
        self.assertEqual(srgb_to_linear(0.0), 0.0)
        self.assertAlmostEqual(srgb_to_linear(1.0), 1.0)
        self.assertAlmostEqual(srgb_to_linear(0.5), 0.21404, places=4)
        self.assertAlmostEqual(srgb_to_linear(0.04), 0.04 / 12.92)

    def test_build_opaque(self):
        m = build_material("Body1_Material", (255, 0, 0, 255), 0.0, 0.4)
        self.assertEqual(m.name, "Body1_Material")
        self.assertEqual(m.alpha, AlphaMode.OPAQUE)
        self.assertEqual(m.base_colour, (1.0, 0.0, 0.0, 1.0))
        self.assertAlmostEqual(m.roughness, 0.4)

    def test_build_transparent_is_blend(self):
        m = build_material("Glass", (255, 255, 255, 127.5), 0.0, 0.0)
        self.assertEqual(m.alpha, AlphaMode.BLEND)
        self.assertAlmostEqual(m.base_colour[3], 0.5)

    def test_clamping(self):
        m = build_material("X", (300, -5, 0, 255), 2.0, -1.0)
        self.assertEqual(m.base_colour[:3], (1.0, 0.0, 0.0))
        self.assertEqual((m.metallic, m.roughness), (1.0, 0.0))

    def test_equivalence(self):
        a = ExtractedMaterial("a", (0.5, 0.5, 0.5, 1.0), 0.0, 0.2)
        b = ExtractedMaterial("b", (0.50001, 0.5, 0.5, 1.0), 0.0, 0.2)
        c = ExtractedMaterial("c", (0.6, 0.5, 0.5, 1.0), 0.0, 0.2)
        self.assertTrue(materials_equivalent(a, b))
        self.assertFalse(materials_equivalent(a, c))
        self.assertTrue(materials_equivalent(None, None))
        self.assertFalse(materials_equivalent(a, None))


if __name__ == "__main__":
    unittest.main()
