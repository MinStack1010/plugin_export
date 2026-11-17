"""Compatibility test: NX plugin GLB writer vs the ORIGINAL SOLIDWORKS writer.

tests/reference_sw builds a small console tool that compiles the unmodified
SOLIDWORKS plugin sources (Export/*.cs: ExtractedScene, CoordinateConverter,
GltfSceneWriter with SharpGLTF) and writes a GLB from a scene JSON.  The same
scene is exported by the Python pipeline and both files are compared:

* BIN chunk: byte identical (positions, normals, indices, layout);
* JSON chunk: identical text except asset.generator.

Set SYNODE_REFERENCE_SW_EXE to the built ReferenceSw.exe (run_tests.ps1 does
it); the test is skipped otherwise.
"""

import json
import os
import struct
import subprocess
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import test_support  # noqa: F401,E402
from test_support import TempDir  # noqa: E402

import synthetic_scenes as ss  # noqa: E402
from scene_json import write_scene_json  # noqa: E402
from glb_reader import read_glb  # noqa: E402
from synode_nx_exporter.config import ExportOptions  # noqa: E402
from synode_nx_exporter.exporter import export_scene_to_glb  # noqa: E402

REFERENCE_EXE = os.environ.get("SYNODE_REFERENCE_SW_EXE", "")


def _json_chunk(path):
    with open(path, "rb") as handle:
        data = handle.read()
    length = struct.unpack_from("<I", data, 12)[0]
    return data[20:20 + length].decode("utf-8").rstrip(" ")


SCENES = {
    "part": ss.part_scene,
    "assembly": ss.assembly_scene,
    "uint16_limit": lambda: ss.large_mesh_scene(12000),
    "uint32": lambda: ss.large_mesh_scene(20000),
    "packing": ss.packing_scene,
    "packing_uint32": lambda: ss.packing_scene(with_large=True),
}


@unittest.skipUnless(REFERENCE_EXE and os.path.isfile(REFERENCE_EXE),
                     "SYNODE_REFERENCE_SW_EXE not set (build tests/reference_sw first)")
class ReferenceSolidWorksWriterTests(unittest.TestCase):
    def compare(self, name, scene_factory):
        with TempDir() as tmp:
            scene_path = os.path.join(tmp, name + ".json")
            ref_path = os.path.join(tmp, name + "_sw.glb")
            nx_path = os.path.join(tmp, name + "_nx.glb")
            write_scene_json(scene_factory(), scene_path)
            proc = subprocess.run([REFERENCE_EXE, scene_path, ref_path], capture_output=True, text=True)
            self.assertEqual(proc.returncode, 0, proc.stderr)
            export_scene_to_glb(scene_factory(), nx_path, ExportOptions())
            ref, nx = read_glb(ref_path), read_glb(nx_path)
            self.assertEqual(ref.bin, nx.bin, "BIN chunk differs for " + name)
            ref_json, nx_json = _json_chunk(ref_path), _json_chunk(nx_path)
            nx_json = nx_json.replace(json.dumps(nx.gltf["asset"]["generator"]),
                                      json.dumps(ref.gltf["asset"]["generator"]))
            self.assertEqual(ref_json, nx_json, "JSON chunk differs for " + name)
            return ref

    def test_part(self):
        self.compare("part", SCENES["part"])

    def test_assembly_hierarchy_materials_multibody(self):
        ref = self.compare("assembly", SCENES["assembly"])
        self.assertEqual(len(ref.gltf["nodes"]), 9)

    def test_index_width_uint16(self):
        ref = self.compare("uint16_limit", SCENES["uint16_limit"])
        self.assertEqual(ref.gltf["accessors"][2]["componentType"], 5123)

    def test_index_width_uint32(self):
        ref = self.compare("uint32", SCENES["uint32"])
        self.assertEqual(ref.gltf["accessors"][2]["componentType"], 5125)

    def test_buffer_layout_several_meshes_per_node(self):
        ref = self.compare("packing", SCENES["packing"])
        self.assertEqual(len(ref.gltf["meshes"]), 8)

    def test_buffer_layout_mixed_index_width(self):
        ref = self.compare("packing_uint32", SCENES["packing_uint32"])
        # SharpGLTF uses one index width for the whole document
        self.assertEqual({a["componentType"] for a in ref.gltf["accessors"] if a["type"] == "SCALAR"}, {5125})

    def test_scene_extracted_from_nx_test_doubles(self):
        import fake_nx as fx
        import test_nx_adapter as t
        nx = fx.FakeNx()
        t.assembly(nx)
        scene_holder = {}

        def factory():
            if "scene" not in scene_holder:
                scene_holder["scene"] = t.extract(nx)[0]
            return scene_holder["scene"]
        self.compare("nx_fake_assembly", factory)


if __name__ == "__main__":
    unittest.main()
