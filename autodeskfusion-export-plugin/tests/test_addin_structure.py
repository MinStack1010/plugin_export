"""Static checks of the add-in package (manifest, files, syntax, entry points)."""

import ast
import json
import os
import py_compile
import re
import tempfile
import unittest

import support

ADDIN = support.ADDIN_DIR
NAME = "SynodeGlbExporter"


def python_files():
    for folder, _, files in os.walk(ADDIN):
        for name in files:
            if name.endswith(".py"):
                yield os.path.join(folder, name)


class AddinStructureTests(unittest.TestCase):
    def test_manifest(self):
        path = os.path.join(ADDIN, NAME + ".manifest")
        with open(path, "r", encoding="utf-8") as handle:
            manifest = json.load(handle)
        self.assertEqual(manifest["autodeskProduct"], "Fusion")
        self.assertEqual(manifest["type"], "addin")
        self.assertRegex(manifest["id"], r"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")
        self.assertIn("windows", manifest["supportedOS"])
        self.assertIn("version", manifest)

    def test_required_files(self):
        required = [NAME + ".py", NAME + ".manifest", "config.json"]
        required += [os.path.join("resources", "export_glb", f)
                     for f in ("16x16.png", "16x16@2x.png", "32x32.png", "32x32@2x.png")]
        for rel in required:
            with self.subTest(file=rel):
                self.assertTrue(os.path.isfile(os.path.join(ADDIN, rel)))

    def test_icons_are_png(self):
        folder = os.path.join(ADDIN, "resources", "export_glb")
        for name in os.listdir(folder):
            with open(os.path.join(folder, name), "rb") as handle:
                self.assertEqual(handle.read(8), b"\x89PNG\r\n\x1a\n", name)

    def test_entry_point_defines_run_and_stop(self):
        with open(os.path.join(ADDIN, NAME + ".py"), "r", encoding="utf-8") as handle:
            tree = ast.parse(handle.read())
        functions = {n.name: n for n in tree.body if isinstance(n, ast.FunctionDef)}
        for name in ("run", "stop"):
            self.assertIn(name, functions)
            self.assertEqual(len(functions[name].args.args), 1)

    def test_all_python_files_compile(self):
        with tempfile.TemporaryDirectory() as out:
            for i, path in enumerate(python_files()):
                with self.subTest(file=os.path.relpath(path, ADDIN)):
                    py_compile.compile(path, cfile=os.path.join(out, "{}.pyc".format(i)), doraise=True)

    def test_core_does_not_import_fusion(self):
        core = os.path.join(ADDIN, "synode_glb", "core")
        for name in os.listdir(core):
            if name.endswith(".py"):
                with open(os.path.join(core, name), "r", encoding="utf-8") as handle:
                    self.assertNotRegex(handle.read(), r"^\s*(import|from)\s+adsk", name)

    def test_no_solidworks_or_third_party_dependencies(self):
        allowed_top = {"adsk", "ast", "dataclasses", "json", "math", "os", "struct", "sys", "tempfile",
                       "traceback", "typing", "uuid", "array", "urllib"}
        for path in python_files():
            with open(path, "r", encoding="utf-8") as handle:
                source = handle.read()
            self.assertIsNone(re.search(r"SolidWorks\.Interop|SldWorks|ISwAddin", source), path)
            for node in ast.walk(ast.parse(source)):
                if isinstance(node, ast.Import):
                    names = [a.name.split(".")[0] for a in node.names]
                elif isinstance(node, ast.ImportFrom) and node.level == 0:
                    names = [node.module.split(".")[0]]
                else:
                    continue
                for name in names:
                    self.assertIn(name, allowed_top, "{} imports {}".format(path, name))

    def test_no_hardcoded_user_paths(self):
        for path in list(python_files()) + [os.path.join(ADDIN, "config.json")]:
            with open(path, "r", encoding="utf-8") as handle:
                self.assertIsNone(re.search(r"[A-Za-z]:\\\\Users|[A-Za-z]:\\Users|/Users/|/home/", handle.read()), path)


if __name__ == "__main__":
    unittest.main()
