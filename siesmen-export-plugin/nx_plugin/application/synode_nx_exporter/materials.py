"""Material helpers (ports of the SW extractor colour/material logic)."""

import math

from .scene import ExtractedMaterial, ALPHA_OPAQUE, ALPHA_BLEND

EQUIVALENCE_TOLERANCE = 0.0001  # SW AreMaterialsEquivalent tolerance


def clamp01(value):
    if value < 0.0:
        return 0.0
    if value > 1.0:
        return 1.0
    return float(value)


def srgb_to_linear(value):
    """Same formula as SolidWorksGeometryExtractor.SrgbToLinear."""
    value = clamp01(value)
    if value <= 0.04045:
        return value / 12.92
    return math.pow((value + 0.055) / 1.055, 2.4)


def materials_equivalent(a, b, tolerance=EQUIVALENCE_TOLERANCE):
    """Port of AreMaterialsEquivalent (names are ignored, like in SW)."""
    if a is b:
        return True
    if a is None or b is None:
        return False
    distance = math.sqrt(sum((x - y) ** 2 for x, y in zip(a.base_color, b.base_color)))
    return (distance < tolerance
            and abs(a.metallic - b.metallic) < tolerance
            and abs(a.roughness - b.roughness) < tolerance
            and a.alpha_mode == b.alpha_mode)


def material_from_display_color(name, srgb, translucency_percent=0, metallic=0.0, roughness=0.0):
    """Build a material from an NX display colour.

    ``srgb``: (r, g, b) in 0..1 as shown on screen (sRGB), converted to the
    linear factors required by glTF like SW does for DSPBR albedo.
    ``translucency_percent``: NX translucency 0..100; converted like the SW
    DSPBR transparency (alpha = 1 - transparency, BLEND when non zero).
    """
    r, g, b = (srgb_to_linear(c) for c in srgb)
    transparency = clamp01((translucency_percent or 0) / 100.0)
    alpha = 1.0
    mode = ALPHA_OPAQUE
    if transparency != 0:
        alpha = 1.0 - transparency
        mode = ALPHA_BLEND
    return ExtractedMaterial(name=name, base_color=(r, g, b, alpha),
                             metallic=clamp01(metallic), roughness=clamp01(roughness),
                             alpha_mode=mode)
