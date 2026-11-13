"""Test helpers: puts the add-in on sys.path and installs a minimal fake ``adsk`` API.

The fake only provides the enum/cast members the add-in uses, so that the Fusion
specific extraction logic can be exercised outside Fusion with fake model objects.
It is not a Fusion emulator: real API behaviour must still be verified in Fusion.
"""

import os
import sys
import types

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ADDIN_DIR = os.path.join(ROOT, "SynodeGlbExporter")

if ADDIN_DIR not in sys.path:
    sys.path.insert(0, ADDIN_DIR)


def install_fake_adsk():
    existing = sys.modules.get("adsk")
    if existing is not None:
        if getattr(existing, "_is_test_fake", False):
            return existing
        raise RuntimeError("A real 'adsk' module is loaded; run the tests outside Fusion.")

    adsk = types.ModuleType("adsk")
    adsk._is_test_fake = True
    core = types.ModuleType("adsk.core")
    fusion = types.ModuleType("adsk.fusion")

    class AppearanceSourceTypes:
        MaterialAppearanceSource = 0
        BodyAppearanceSource = 1
        OccurrenceAppearanceSource = 2
        FaceAppearanceSource = 3
        OverrideAppearanceSource = 4

    class Color:
        def __init__(self, red, green, blue, opacity=255):
            self.red, self.green, self.blue, self.opacity = red, green, blue, opacity

    class _Property:
        def __init__(self, property_id, value, name=""):
            self.id, self.value, self.name = property_id, value, name

        @classmethod
        def cast(cls, obj):
            return obj if isinstance(obj, cls) else None

    class ColorProperty(_Property):
        pass

    class FloatProperty(_Property):
        pass

    class TriangleMeshQualityOptions:
        LowQualityTriangleMesh = 8
        NormalQualityTriangleMesh = 11
        HighQualityTriangleMesh = 13
        VeryHighQualityTriangleMesh = 15

    class Design:
        @staticmethod
        def cast(obj):
            return obj

    core.AppearanceSourceTypes = AppearanceSourceTypes
    core.Color = Color
    core.ColorProperty = ColorProperty
    core.FloatProperty = FloatProperty
    fusion.TriangleMeshQualityOptions = TriangleMeshQualityOptions
    fusion.Design = Design

    adsk.core = core
    adsk.fusion = fusion
    adsk.doEvents = lambda: None
    sys.modules["adsk"] = adsk
    sys.modules["adsk.core"] = core
    sys.modules["adsk.fusion"] = fusion
    return adsk
