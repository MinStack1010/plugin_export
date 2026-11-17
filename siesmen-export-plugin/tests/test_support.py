"""Shared test setup: import paths, temporary folders, GLB helpers."""

import os
import shutil
import sys
import tempfile

TESTS_DIR = os.path.dirname(os.path.abspath(__file__))
PROJECT_DIR = os.path.dirname(TESTS_DIR)
APP_DIR = os.path.join(PROJECT_DIR, "nx_plugin", "application")

for _p in (APP_DIR, os.path.join(TESTS_DIR, "tools"), os.path.join(TESTS_DIR, "fakes"),
           os.path.join(TESTS_DIR, "unit")):
    if _p not in sys.path:
        sys.path.insert(0, _p)


class TempDir:
    """Temporary folder (honours SYNODE_TEST_TMP so tests can run in a sandbox)."""

    def __enter__(self):
        base = os.environ.get("SYNODE_TEST_TMP") or None
        if base:
            os.makedirs(base, exist_ok=True)
        self.path = tempfile.mkdtemp(prefix="synode_test_", dir=base)
        return self.path

    def __exit__(self, *exc):
        shutil.rmtree(self.path, ignore_errors=True)


class NullLog:
    def __init__(self):
        self.warnings = []
        self.infos = []
        self.errors = []
        self.path = None

    def info(self, m):
        self.infos.append(m)

    def warning(self, m):
        self.warnings.append(m)

    def error(self, m):
        self.errors.append(m)
