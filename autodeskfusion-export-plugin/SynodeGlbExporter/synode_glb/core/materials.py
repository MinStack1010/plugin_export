"""Material helpers ported from SolidWorksGeometryExtractor (CAD independent)."""

import math
from typing import Optional, Sequence

from .scene import AlphaMode, ExtractedMaterial

EQUIVALENCE_TOLERANCE = 0.0001


def clamp01(value: float) -> float:
    if value < 0.0:
        return 0.0
    if value > 1.0:
        return 1.0
    return float(value)


def srgb_to_linear(value: float) -> float:
    value = clamp01(value)
    if value <= 0.04045:
        return value / 12.92
    return math.pow((value + 0.055) / 1.055, 2.4)


def materials_equivalent(a: Optional[ExtractedMaterial], b: Optional[ExtractedMaterial]) -> bool:
    """Same rule as AreMaterialsEquivalent in the SolidWorks plugin."""
    if a is b:
        return True
    if a is None or b is None:
        return False
    distance = math.sqrt(sum((x - y) ** 2 for x, y in zip(a.base_colour, b.base_colour)))
    return (
        distance < EQUIVALENCE_TOLERANCE
        and abs(a.metallic - b.metallic) < EQUIVALENCE_TOLERANCE
        and abs(a.roughness - b.roughness) < EQUIVALENCE_TOLERANCE
        and a.alpha == b.alpha
    )


def build_material(
    name: str,
    rgba255: Sequence[float],
    metallic: float,
    roughness: float,
) -> ExtractedMaterial:
    """Builds a glTF-ready material from an sRGB 0..255 RGBA colour.

    Mirrors ConvertDspbrMaterial: the colour is converted from sRGB to linear,
    and any transparency switches the material to BLEND with alpha = opacity.
    """
    r, g, b = (srgb_to_linear(float(c) / 255.0) for c in rgba255[:3])
    opacity = clamp01(float(rgba255[3]) / 255.0) if len(rgba255) > 3 else 1.0
    alpha = AlphaMode.OPAQUE
    if opacity < 1.0:
        alpha = AlphaMode.BLEND
    return ExtractedMaterial(
        name=name,
        base_colour=(r, g, b, opacity if alpha == AlphaMode.BLEND else 1.0),
        metallic=clamp01(metallic),
        roughness=clamp01(roughness),
        alpha=alpha,
    )
