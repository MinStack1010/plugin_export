"""Tests for config.json parsing and output path validation."""

import json
import os
import tempfile
import unittest

import support

from synode_glb.core.config import ExportConfig, load_config, parse_config
from synode_glb.core.errors import ConfigError, ExportError
from synode_glb.core.output_path import normalize_output_path


class ConfigTests(unittest.TestCase):
    def test_defaults(self):
        config = parse_config({})
        self.assertEqual(config, ExportConfig())
        self.assertEqual(config.unit_scale, 0.01)
        self.assertEqual(config.upload_url, "")
        self.assertEqual(config.mesh_quality, "high")

    def test_shipped_config_is_valid(self):
        config = load_config(os.path.join(support.ADDIN_DIR, "config.json"))
        self.assertEqual(config.unit_scale, 0.01)
        self.assertEqual(config.upload_url, "")

    def test_missing_file_gives_defaults(self):
        self.assertEqual(load_config(os.path.join(tempfile.gettempdir(), "does-not-exist-synode.json")), ExportConfig())

    def test_values_normalised(self):
        config = parse_config({"default_file_name": "model", "mesh_quality": " Very_High ", "up_axis": "Z",
                               "upload_url": " https://example.com/upload "})
        self.assertEqual(config.default_file_name, "model.glb")
        self.assertEqual(config.mesh_quality, "very_high")
        self.assertEqual(config.up_axis, "z")
        self.assertEqual(config.upload_url, "https://example.com/upload")

    def test_invalid_values(self):
        for data in ({"unit_scale": 0}, {"unit_scale": -1}, {"unit_scale": "1"}, {"unit_scale": True},
                     {"mesh_quality": "ultra"}, {"up_axis": "x"}, {"api_key": 5},
                     {"upload_timeout_seconds": 0}):
            with self.subTest(data=data):
                with self.assertRaises(ConfigError):
                    parse_config(data)
        with self.assertRaises(ConfigError):
            parse_config([1, 2])

    def test_bad_json(self):
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "config.json")
            with open(path, "w", encoding="utf-8") as handle:
                handle.write("{ not json")
            with self.assertRaises(ConfigError) as ctx:
                load_config(path)
            self.assertIn("not valid JSON", str(ctx.exception))

    def test_utf8_bom_accepted(self):
        with tempfile.TemporaryDirectory() as folder:
            path = os.path.join(folder, "config.json")
            with open(path, "w", encoding="utf-8-sig") as handle:
                json.dump({"unit_scale": 1}, handle)
            self.assertEqual(load_config(path).unit_scale, 1.0)


class OutputPathTests(unittest.TestCase):
    def test_adds_extension(self):
        with tempfile.TemporaryDirectory() as folder:
            self.assertEqual(normalize_output_path(os.path.join(folder, "model")), os.path.join(folder, "model.glb"))
            self.assertEqual(normalize_output_path(os.path.join(folder, "a.GLB")), os.path.join(folder, "a.GLB"))

    def test_empty(self):
        for value in (None, "", "   "):
            with self.assertRaises(ExportError):
                normalize_output_path(value)

    def test_missing_folder(self):
        with tempfile.TemporaryDirectory() as folder:
            with self.assertRaises(ExportError) as ctx:
                normalize_output_path(os.path.join(folder, "missing", "a.glb"))
            self.assertIn("does not exist", str(ctx.exception))

    def test_directory_path(self):
        with tempfile.TemporaryDirectory() as folder:
            target = os.path.join(folder, "dir.glb")
            os.mkdir(target)
            with self.assertRaises(ExportError) as ctx:
                normalize_output_path(target)
            self.assertIn("folder", str(ctx.exception))


if __name__ == "__main__":
    unittest.main()
