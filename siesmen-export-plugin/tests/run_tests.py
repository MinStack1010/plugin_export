"""Test runner: static checks + unit tests + SOLIDWORKS writer comparison.

Usage (any CPython 3.8+, e.g. the Python runtime shipped with NX through
tests/tools/pyhost):
    python tests/run_tests.py
Environment:
    SYNODE_REFERENCE_SW_EXE  path of the built tests/reference_sw tool (optional)
    SYNODE_TEST_TMP          folder for temporary files (optional)
"""

import ast
import os
import sys
import unittest

TESTS_DIR = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, TESTS_DIR)
import test_support  # noqa: E402


def static_checks():
    """Parse every plugin/test source (syntax) and import every core module."""
    failures = []
    roots = [os.path.join(test_support.PROJECT_DIR, "nx_plugin"), TESTS_DIR]
    count = 0
    for root in roots:
        for folder, dirs, files in os.walk(root):
            dirs[:] = [d for d in dirs if d not in ("bin", "obj", "__pycache__")]
            for name in files:
                if name.endswith(".py"):
                    path = os.path.join(folder, name)
                    count += 1
                    try:
                        with open(path, "r", encoding="utf-8") as handle:
                            ast.parse(handle.read(), path)
                    except SyntaxError as exc:
                        failures.append("{}: {}".format(path, exc))
    modules = ["synode_nx_exporter", "synode_nx_exporter.config", "synode_nx_exporter.errors",
               "synode_nx_exporter.logger", "synode_nx_exporter.scene", "synode_nx_exporter.matrix",
               "synode_nx_exporter.materials", "synode_nx_exporter.mesh_builder",
               "synode_nx_exporter.coordinate_converter", "synode_nx_exporter.gltf_writer",
               "synode_nx_exporter.output_path", "synode_nx_exporter.exporter",
               "synode_nx_exporter.nx", "synode_nx_exporter.nx.units", "synode_nx_exporter.nx.tessellator",
               "synode_nx_exporter.nx.appearance", "synode_nx_exporter.nx.extractor",
               "synode_nx_exporter.nx.ui", "synode_nx_exporter.nx.command"]
    for module in modules:
        try:
            __import__(module)
        except Exception as exc:  # NXOpen must NOT be needed at import time
            failures.append("import {}: {}".format(module, exc))
    # the entry journal must not import NXOpen at module level
    entry = os.path.join(test_support.APP_DIR, "synode_export_glb.py")
    with open(entry, "r", encoding="utf-8") as handle:
        tree = ast.parse(handle.read())
    for node in tree.body:
        if isinstance(node, (ast.Import, ast.ImportFrom)):
            names = [a.name for a in node.names] + [getattr(node, "module", "") or ""]
            if any(n.startswith("NXOpen") for n in names):
                failures.append("entry journal imports NXOpen at module level")
    print("Static checks: {} Python files parsed, {} modules imported, {} problem(s)".format(
        count, len(modules), len(failures)))
    for f in failures:
        print("  FAIL", f)
    return not failures


def main():
    ok = static_checks()
    suite = unittest.TestSuite()
    for sub in ("unit", "compat"):
        suite.addTests(unittest.TestLoader().discover(os.path.join(TESTS_DIR, sub), pattern="test_*.py",
                                                      top_level_dir=os.path.join(TESTS_DIR, sub)))
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    ok = ok and result.wasSuccessful()
    print("OVERALL:", "PASS" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
