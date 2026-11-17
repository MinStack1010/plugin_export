"""Unit tests of the NX independent core (runs with any Python 3.8+)."""

import math
import os
import stat
import struct
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import test_support  # noqa: F401,E402  (sys.path)
from test_support import TempDir

from synode_nx_exporter import matrix as mx
from synode_nx_exporter.config import ExportOptions
from synode_nx_exporter.coordinate_converter import convert_scene, normalize_f32
from synode_nx_exporter.errors import EmptyGeometryError, ExportError, MeshValidationError, OutputPathError
from synode_nx_exporter.exporter import export_scene_to_glb
from synode_nx_exporter.gltf_writer import build_gltf, build_glb
from synode_nx_exporter.materials import material_from_display_color, materials_equivalent, srgb_to_linear
from synode_nx_exporter.mesh_builder import MeshAccumulator, validate_mesh
from synode_nx_exporter.output_path import check_writable, normalize_output_path, write_atomic
from synode_nx_exporter.scene import ExtractedMesh, ExtractedNode, ExtractedScene

import synthetic_scenes as ss
from glb_reader import read_glb, world_matrices


class MatrixTests(unittest.TestCase):
    def test_inverse_and_multiply(self):
        m = ss.rotation_z(37, (1.0, 2.0, 3.0))
        self.assertTrue(mx.nearly_equal(mx.multiply(m, mx.inverse_affine(m)), mx.IDENTITY, 1e-12))

    def test_from_axes_is_column_major(self):
        m = mx.from_axes((0, 1, 0), (-1, 0, 0), (0, 0, 1), (5, 6, 7))
        self.assertEqual(mx.transform_point(m, (1, 0, 0)), (5.0, 7.0, 7.0))
        self.assertEqual(m[12:15], (5.0, 6.0, 7.0))

    def test_rows_vs_axes(self):
        rows = ((0, 1, 0), (-1, 0, 0), (0, 0, 1))
        a = mx.from_axes(rows[0], rows[1], rows[2], (0, 0, 0))
        b = mx.from_rows_3x3(rows, (0, 0, 0))
        self.assertTrue(mx.nearly_equal(mx.multiply(a, b), mx.IDENTITY, 1e-12))

    def test_singular_inverse_raises(self):
        with self.assertRaises(ValueError):
            mx.inverse_affine((0.0,) * 15 + (1.0,))


class MaterialTests(unittest.TestCase):
    def test_srgb_to_linear_matches_sw_formula(self):
        self.assertAlmostEqual(srgb_to_linear(0.5), math.pow((0.5 + 0.055) / 1.055, 2.4))
        self.assertAlmostEqual(srgb_to_linear(0.02), 0.02 / 12.92)

    def test_translucency_to_blend(self):
        m = material_from_display_color("m", (1, 1, 1), 40)
        self.assertEqual(m.alpha_mode, "BLEND")
        self.assertAlmostEqual(m.base_color[3], 0.6)
        self.assertEqual(material_from_display_color("m", (1, 1, 1), 0).alpha_mode, "OPAQUE")

    def test_equivalence_ignores_names(self):
        a = material_from_display_color("a", (0.2, 0.3, 0.4))
        b = material_from_display_color("b", (0.2, 0.3, 0.40001))
        c = material_from_display_color("c", (0.2, 0.3, 0.5))
        self.assertTrue(materials_equivalent(a, b))
        self.assertFalse(materials_equivalent(a, c))
        self.assertFalse(materials_equivalent(a, None))


class MeshBuilderTests(unittest.TestCase):
    def test_dedupe_degenerate_and_winding(self):
        acc = MeshAccumulator("m", None)
        n = (0, 0, 1)
        self.assertTrue(acc.add_triangle((0, 0, 0), (1, 0, 0), (0, 1, 0), n, n, n))
        self.assertTrue(acc.add_triangle((1, 0, 0), (1, 1, 0), (0, 1, 0), n, n, n))
        self.assertFalse(acc.add_triangle((0, 0, 0), (1, 0, 0), (2, 0, 0), n, n, n))  # degenerate
        self.assertEqual(acc.mesh.vertex_count, 4)
        self.assertEqual(acc.degenerate_count, 1)
        # clockwise triangle with +Z normals is flipped (SW: geo . sum(normals) < 0)
        acc2 = MeshAccumulator("m2", None)
        acc2.add_triangle((0, 0, 0), (0, 1, 0), (1, 0, 0), n, n, n)
        p = acc2.mesh.positions
        i = acc2.mesh.indices
        e1 = [p[i[1]][k] - p[i[0]][k] for k in range(3)]
        e2 = [p[i[2]][k] - p[i[0]][k] for k in range(3)]
        self.assertGreater(e1[0] * e2[1] - e1[1] * e2[0], 0)

    def test_zero_normal_replaced_by_facet_normal(self):
        acc = MeshAccumulator("m", None)
        acc.add_triangle((0, 0, 0), (1, 0, 0), (0, 1, 0), (0, 0, 0), None, (0, 0, 2))
        self.assertEqual(acc.zero_normal_count, 2)
        for nrm in acc.mesh.normals:
            self.assertAlmostEqual(nrm[2], 1.0)

    def test_validate_mesh(self):
        with self.assertRaises(MeshValidationError):
            validate_mesh(ExtractedMesh(name="e"))
        bad = ExtractedMesh(name="b", positions=[(0, 0, 0)] * 3, normals=[(0, 0, 1)] * 3, indices=[0, 1, 5])
        with self.assertRaises(MeshValidationError):
            validate_mesh(bad)


class ConverterTests(unittest.TestCase):
    def test_normalize_f32_bit_exact_single_precision(self):
        n = normalize_f32((3.0, 4.0, 0.0))
        self.assertEqual(n, (struct.unpack("<f", struct.pack("<f", 0.6))[0],
                             struct.unpack("<f", struct.pack("<f", 0.8))[0], 0.0))
        self.assertIsNone(normalize_f32((0, 0, 0)))

    def test_unit_scale_scales_positions_and_translations(self):
        scene = ss.assembly_scene()
        out = convert_scene(scene, ExportOptions(unit_scale=1000.0))
        a = out.root.children[0]
        self.assertAlmostEqual(a.local_transform[12], 200.0)
        self.assertAlmostEqual(out.root.children[1].children[0].meshes[0].positions[1][0],
                               scene.root.children[1].children[0].meshes[0].positions[1][0] * 1000.0, places=3)

    def test_no_axis_conversion_by_default(self):
        out = convert_scene(ss.part_scene(), ExportOptions())
        self.assertEqual(out.root.local_transform, mx.IDENTITY)
        out = convert_scene(ss.part_scene(), ExportOptions(z_up_to_y_up=True))
        self.assertNotEqual(out.root.local_transform, mx.IDENTITY)


class WriterTests(unittest.TestCase):
    def test_assembly_structure(self):
        doc, binary = build_gltf(convert_scene(ss.assembly_scene(), ExportOptions()), ExportOptions())
        names = [n.get("name") for n in doc["nodes"]]
        self.assertEqual(names[:7], ["main_assembly", "sub_a-1", "sub_a-1/bolt-1", "sub_a-1/bolt-2",
                                     "sub_b-1", "sub_b-1/plate-1", "bracket-1"])
        self.assertEqual(doc["nodes"][0]["children"], [1, 4, 6])
        self.assertNotIn("matrix", doc["nodes"][0])
        # second body/material mesh -> unnamed child node
        self.assertEqual(doc["nodes"][5]["children"], [7])
        self.assertEqual(doc["nodes"][7], {"mesh": 3})
        self.assertEqual(len(doc["meshes"]), 6)
        self.assertEqual({m["name"] for m in doc["materials"]},
                         {"Body1_Material", "Body2_Face_Material", "Material", "Body2_Material"})
        blend = [m for m in doc["materials"] if m.get("alphaMode") == "BLEND"]
        self.assertEqual(len(blend), 1)

    def test_glb_roundtrip_and_world_transforms(self):
        scene = ss.assembly_scene()
        glb = read_glb(build_glb(convert_scene(scene, ExportOptions()), ExportOptions()))
        worlds = world_matrices(glb)
        expected = mx.multiply(scene.root.children[0].local_transform,
                               scene.root.children[0].children[1].local_transform)
        self.assertTrue(mx.nearly_equal(worlds[3], expected, 1e-6))

    def test_uint32_indices_for_large_meshes(self):
        doc, _ = build_gltf(convert_scene(ss.large_mesh_scene(20000), ExportOptions()), ExportOptions())
        self.assertEqual(doc["accessors"][2]["componentType"], 5125)
        doc, _ = build_gltf(convert_scene(ss.part_scene(), ExportOptions()), ExportOptions())
        self.assertEqual(doc["accessors"][2]["componentType"], 5123)

    def test_share_instance_meshes_option(self):
        scene = ss.assembly_scene()
        a1, a2 = scene.root.children[0].children
        a1.meshes[0].share_key = a2.meshes[0].share_key = ("body", 0)
        doc, _ = build_gltf(scene, ExportOptions(share_instance_meshes=True))
        self.assertEqual(doc["nodes"][2]["mesh"], doc["nodes"][3]["mesh"])
        doc, _ = build_gltf(scene, ExportOptions())
        self.assertNotEqual(doc["nodes"][2]["mesh"], doc["nodes"][3]["mesh"])


class OutputPathTests(unittest.TestCase):
    def test_extension_rules(self):
        self.assertTrue(normalize_output_path(r"C:\out\model").lower().endswith("model.glb"))
        self.assertTrue(normalize_output_path(r"C:\out\model.GLB").endswith("model.GLB"))
        for bad in ("", "   ", r"C:\out\model.stl", r"C:\out\mo|del.glb", r"C:\out\CON.glb", r"C:\out\a:b.glb",
                    "C:\\out\\"):
            with self.assertRaises(OutputPathError, msg=bad):
                normalize_output_path(bad)

    def test_missing_folder_and_folder_path(self):
        with TempDir() as tmp:
            with self.assertRaises(OutputPathError):
                check_writable(os.path.join(tmp, "missing", "x.glb"))
            os.mkdir(os.path.join(tmp, "d.glb"))
            with self.assertRaises(OutputPathError):
                check_writable(os.path.join(tmp, "d.glb"))
            check_writable(os.path.join(tmp, "ok.glb"))
            self.assertEqual(os.listdir(tmp), ["d.glb"])  # probe file removed

    def test_read_only_existing_file(self):
        with TempDir() as tmp:
            target = os.path.join(tmp, "ro.glb")
            with open(target, "wb") as handle:
                handle.write(b"old")
            os.chmod(target, stat.S_IREAD)
            try:
                with self.assertRaises(OutputPathError):
                    check_writable(target)
                with self.assertRaises(OutputPathError):
                    write_atomic(target, b"new data")
                with open(target, "rb") as handle:
                    self.assertEqual(handle.read(), b"old")   # untouched
                self.assertEqual(os.listdir(tmp), ["ro.glb"])  # no temp file left
            finally:
                os.chmod(target, stat.S_IWRITE | stat.S_IREAD)

    def test_write_atomic_rejects_empty(self):
        with TempDir() as tmp:
            with self.assertRaises(OutputPathError):
                write_atomic(os.path.join(tmp, "e.glb"), b"")
            self.assertEqual(os.listdir(tmp), [])


class ExporterTests(unittest.TestCase):
    def test_empty_scene_writes_nothing(self):
        with TempDir() as tmp:
            target = os.path.join(tmp, "empty.glb")
            with self.assertRaises(EmptyGeometryError):
                export_scene_to_glb(ExtractedScene(root=ExtractedNode(name="r")), target, ExportOptions())
            self.assertFalse(os.path.exists(target))

    def test_export_writes_valid_glb(self):
        with TempDir() as tmp:
            target = os.path.join(tmp, "asm.glb")
            result = export_scene_to_glb(ss.assembly_scene(), target, ExportOptions())
            glb = read_glb(target)
            self.assertEqual(result.node_count, 7)
            self.assertEqual(result.mesh_count, 6)
            self.assertEqual(len(glb.gltf["nodes"]), 9)
            self.assertGreater(result.triangle_count, 0)


class ConfigTests(unittest.TestCase):
    def test_defaults_match_sw(self):
        o = ExportOptions()
        self.assertEqual((o.unit_scale, o.z_up_to_y_up, o.default_metallic, o.default_roughness),
                         (1.0, False, 0.0, 0.0))

    def test_validation_and_unknown_keys(self):
        with self.assertRaises(ExportError):
            ExportOptions(unit_scale=0).validate()
        with self.assertRaises(ExportError):
            ExportOptions(component_name_style="x").validate()
        with self.assertRaises(ValueError):
            ExportOptions.from_dict({"nope": 1})

    def test_load_from_file(self):
        with TempDir() as tmp:
            with open(os.path.join(tmp, "synode_nx_exporter.json"), "w") as handle:
                handle.write('{"chord_tolerance_mm": 0.01, "share_instance_meshes": true}')
            old = os.environ.pop("SYNODE_NX_EXPORTER_CONFIG", None)
            try:
                options, path = ExportOptions.load([tmp])
            finally:
                if old is not None:
                    os.environ["SYNODE_NX_EXPORTER_CONFIG"] = old
            self.assertEqual(options.chord_tolerance_mm, 0.01)
            self.assertTrue(options.share_instance_meshes)
            self.assertTrue(path.endswith("synode_nx_exporter.json"))


if __name__ == "__main__":
    unittest.main()
