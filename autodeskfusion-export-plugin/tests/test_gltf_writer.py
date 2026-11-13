"""Tests for the GLB writer, validated with an independent reader."""

import os
import tempfile
import unittest

import support  # noqa: F401
from glb_reader import Glb

from synode_glb.core.errors import ExportError
from synode_glb.core.gltf_writer import GENERATOR, build_glb, write_glb
from synode_glb.core.materials import build_material
from synode_glb.core.scene import AlphaMode, ExtractedMaterial, ExtractedMesh, ExtractedNode, ExtractedScene


def triangle(name, material=None, offset=0.0):
    return ExtractedMesh(
        name=name,
        positions=[offset, 0, 0, offset + 1, 0, 0, offset, 1, 0],
        normals=[0, 0, 1] * 3,
        indices=[0, 1, 2],
        material=material,
    )


def translation(x, y, z):
    return (1.0, 0, 0, x, 0, 1.0, 0, y, 0, 0, 1.0, z, 0, 0, 0, 1.0)


class GltfWriterTests(unittest.TestCase):
    def test_part_single_mesh_on_root(self):
        red = build_material("Body1_Material", (255, 0, 0, 255), 0.0, 0.5)
        scene = ExtractedScene(root=ExtractedNode(name="Bracket v3", meshes=[triangle("Bracket v3_Material_0", red)]))
        glb = Glb(build_glb(scene))
        glb.validate()
        g = glb.json
        self.assertEqual(g["asset"]["generator"], GENERATOR)
        self.assertEqual(g["scenes"][0]["nodes"], [0])
        self.assertEqual(g["nodes"][0]["name"], "Bracket v3")
        self.assertNotIn("translation", g["nodes"][0])
        self.assertEqual(g["nodes"][0]["mesh"], 0)
        self.assertEqual(g["meshes"][0]["name"], "Bracket v3_Material_0")
        mat = g["materials"][0]
        self.assertEqual(mat["name"], "Body1_Material")
        self.assertTrue(mat["doubleSided"])
        self.assertEqual(mat["pbrMetallicRoughness"]["baseColorFactor"], [1.0, 0.0, 0.0, 1.0])
        self.assertNotIn("alphaMode", mat)

    def test_assembly_hierarchy_and_transforms(self):
        leaf = ExtractedNode(name="Bolt:1", local_transform=translation(0, 0, 0.2), meshes=[triangle("Bolt:1_Material_0")])
        sub = ExtractedNode(name="Sub:1", local_transform=translation(1, 0, 0), children=[leaf])
        root = ExtractedNode(name="Asm", children=[sub])
        glb = Glb(build_glb(ExtractedScene(root=root)))
        glb.validate()
        i_sub, n_sub = glb.node_by_name("Sub:1")
        i_leaf, n_leaf = glb.node_by_name("Bolt:1")
        self.assertEqual(glb.json["nodes"][0]["children"], [i_sub])
        self.assertEqual(n_sub["children"], [i_leaf])
        self.assertEqual(n_sub["translation"], [1.0, 0.0, 0.0])
        self.assertNotIn("mesh", n_sub)
        world = glb.world_positions(i_leaf)
        self.assertAlmostEqual(world[1][0], 2.0, places=6)
        self.assertAlmostEqual(world[1][2], 0.2, places=6)

    def test_multiple_meshes_become_child_nodes(self):
        a = build_material("Body1_Material", (255, 0, 0, 255), 0, 0)
        b = build_material("Body1_Face_Material", (0, 0, 255, 255), 0, 0)
        node = ExtractedNode(name="Part:1", meshes=[triangle("Part:1_Material_0", a), triangle("Part:1_Material_1", b, 2)])
        glb = Glb(build_glb(ExtractedScene(root=ExtractedNode(name="Doc", children=[node]))))
        glb.validate()
        i, n = glb.node_by_name("Part:1")
        self.assertNotIn("mesh", n)
        names = [glb.json["nodes"][c]["name"] for c in n["children"]]
        self.assertEqual(names, ["Part:1_Material_0", "Part:1_Material_1"])
        self.assertEqual([m["name"] for m in glb.json["materials"]], ["Body1_Material", "Body1_Face_Material"])

    def test_material_dedupe_default_and_blend(self):
        glass = build_material("Glass_Material", (200, 200, 255, 128), 0, 0)
        same = build_material("Glass_Material", (200, 200, 255, 128), 0, 0)
        nodes = [
            ExtractedNode(name="A:1", meshes=[triangle("A", glass)]),
            ExtractedNode(name="A:2", meshes=[triangle("A", same)]),
            ExtractedNode(name="B:1", meshes=[triangle("B", None)]),
        ]
        glb = Glb(build_glb(ExtractedScene(root=ExtractedNode(name="Doc", children=nodes))))
        glb.validate()
        mats = glb.json["materials"]
        self.assertEqual(len(mats), 2)
        self.assertEqual(mats[0]["alphaMode"], "BLEND")
        self.assertEqual(mats[1]["name"], "Material")
        self.assertEqual(mats[1]["pbrMetallicRoughness"]["baseColorFactor"], [0.8, 0.8, 0.8, 1.0])
        self.assertEqual(mats[1]["pbrMetallicRoughness"]["metallicFactor"], 0.0)
        self.assertEqual(mats[1]["pbrMetallicRoughness"]["roughnessFactor"], 0.0)
        # instances keep separate meshes (same as the SolidWorks plugin)
        self.assertEqual(len(glb.json["meshes"]), 3)

    def test_mask_alpha(self):
        mat = ExtractedMaterial("M", (1, 1, 1, 1), 0, 0, AlphaMode.MASK)
        glb = Glb(build_glb(ExtractedScene(root=ExtractedNode(name="D", meshes=[triangle("T", mat)]))))
        self.assertEqual(glb.json["materials"][0]["alphaMode"], "MASK")

    def test_empty_meshes_skipped(self):
        empty = ExtractedMesh(name="empty")
        glb = Glb(build_glb(ExtractedScene(root=ExtractedNode(name="D", meshes=[empty]))))
        glb.validate()
        self.assertNotIn("meshes", glb.json)
        self.assertNotIn("mesh", glb.json["nodes"][0])
        self.assertEqual(glb.bin, b"")

    def test_non_finite_rejected(self):
        bad = triangle("bad")
        bad.positions[0] = float("nan")
        with self.assertRaises(ExportError):
            build_glb(ExtractedScene(root=ExtractedNode(name="D", meshes=[bad])))

    def test_no_root(self):
        with self.assertRaises(ExportError):
            build_glb(ExtractedScene())

    def test_large_mesh_uses_uint32_indices(self):
        n = 70000
        positions, normals = [], []
        for i in range(n):
            positions += [float(i % 300), float(i // 300), 0.0]
            normals += [0.0, 0.0, 1.0]
        indices = [0, 1, n - 1]
        mesh = ExtractedMesh("big", positions, normals, indices)
        glb = Glb(build_glb(ExtractedScene(root=ExtractedNode(name="D", meshes=[mesh]))))
        glb.validate()
        idx_acc = glb.json["accessors"][glb.json["meshes"][0]["primitives"][0]["indices"]]
        self.assertEqual(idx_acc["componentType"], 5125)
        self.assertEqual(glb.accessor(glb.json["meshes"][0]["primitives"][0]["indices"]), indices)

    def test_rotation_written_as_quaternion(self):
        rot = (0.0, -1.0, 0, 0.5, 1.0, 0.0, 0, 0, 0, 0, 1.0, 0, 0, 0, 0, 1.0)
        node = ExtractedNode(name="R:1", local_transform=rot, meshes=[triangle("R")])
        glb = Glb(build_glb(ExtractedScene(root=ExtractedNode(name="D", children=[node]))))
        i, n = glb.node_by_name("R:1")
        self.assertIn("rotation", n)
        self.assertAlmostEqual(glb.world_positions(i)[1][1], 1.0, places=6)
        self.assertAlmostEqual(glb.world_positions(i)[1][0], 0.5, places=6)

    def test_mirror_written_as_matrix(self):
        mirror = (-1.0, 0, 0, 0, 0, 1.0, 0, 0, 0, 0, 1.0, 0, 0, 0, 0, 1.0)
        node = ExtractedNode(name="M:1", local_transform=mirror, meshes=[triangle("M")])
        glb = Glb(build_glb(ExtractedScene(root=ExtractedNode(name="D", children=[node]))))
        i, n = glb.node_by_name("M:1")
        self.assertIn("matrix", n)
        self.assertAlmostEqual(glb.world_positions(i)[1][0], -1.0, places=6)

    def test_write_glb_file_and_overwrite(self):
        scene = ExtractedScene(root=ExtractedNode(name="D", meshes=[triangle("T")]))
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "out.glb")
            with open(path, "wb") as handle:
                handle.write(b"old")
            size = write_glb(scene, path)
            with open(path, "rb") as handle:
                data = handle.read()
            self.assertEqual(size, len(data))
            Glb(data).validate()
            self.assertEqual(os.listdir(folder), ["out.glb"])

    def test_write_glb_missing_folder(self):
        scene = ExtractedScene(root=ExtractedNode(name="D", meshes=[triangle("T")]))
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(ExportError):
                write_glb(scene, os.path.join(folder, "missing", "out.glb"))


if __name__ == "__main__":
    unittest.main()
