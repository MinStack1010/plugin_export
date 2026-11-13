"""Reads glTF-relevant values from a Fusion Appearance.

The SolidWorks plugin reads the DSPBR material (albedo, metallic, roughness,
transparency). Fusion appearances use Autodesk Prism / Generic schemas instead,
so the equivalent values are looked up as follows:

* colour:    Appearance.color (newer API) or the colour property of the surface
             type: opaque_albedo, metal_f0, transparent_color, generic_diffuse.
* metallic:  1.0 when the appearance is a Prism metal (has metal_f0), else 0.0.
* roughness: Appearance.roughness (newer API) or surface_roughness, or
             1 - generic_glossiness for Generic appearances.
* opacity:   alpha of the colour; for Generic appearances 1 - generic_transparency.

Anything that cannot be read is reported through ``warnings`` instead of being
silently replaced.
"""

from dataclasses import dataclass, field
from typing import List, Optional, Tuple

import adsk.core

_COLOUR_PROPERTY_IDS = ("opaque_albedo", "metal_f0", "transparent_color", "generic_diffuse")


@dataclass
class AppearanceValues:
    name: str
    rgba255: Tuple[float, float, float, float]
    metallic: float
    roughness: float
    warnings: List[str] = field(default_factory=list)


def _property(props, property_id):
    try:
        return props.itemById(property_id)
    except Exception:
        return None


def _colour_from_property(prop) -> Optional["adsk.core.Color"]:
    colour_prop = adsk.core.ColorProperty.cast(prop) if prop is not None else None
    if colour_prop is None:
        return None
    try:
        return colour_prop.value
    except Exception:
        return None


def _float_from_property(prop) -> Optional[float]:
    float_prop = adsk.core.FloatProperty.cast(prop) if prop is not None else None
    if float_prop is None:
        return None
    try:
        return float(float_prop.value)
    except Exception:
        return None


def read_appearance(appearance) -> Optional[AppearanceValues]:
    """Returns the PBR values of an appearance, or None if no colour can be read."""
    if appearance is None:
        return None

    name = getattr(appearance, "name", "") or "Appearance"
    warnings = []

    try:
        props = appearance.appearanceProperties
    except Exception:
        props = None

    colour = None
    try:
        colour = appearance.color  # available in recent Fusion releases
    except Exception:
        colour = None

    if colour is None and props is not None:
        for property_id in _COLOUR_PROPERTY_IDS:
            colour = _colour_from_property(_property(props, property_id))
            if colour is not None:
                break
        if colour is None:
            try:
                for prop in props:
                    if getattr(prop, "name", "") == "Color":
                        colour = _colour_from_property(prop)
                        if colour is not None:
                            break
            except Exception:
                colour = None

    if colour is None:
        return None

    metallic = 1.0 if props is not None and _property(props, "metal_f0") is not None else 0.0

    roughness = None
    try:
        roughness = float(appearance.roughness)
    except Exception:
        roughness = None
    if roughness is None and props is not None:
        roughness = _float_from_property(_property(props, "surface_roughness"))
        if roughness is None:
            glossiness = _float_from_property(_property(props, "generic_glossiness"))
            if glossiness is not None:
                roughness = 1.0 - glossiness
    if roughness is None:
        roughness = 0.0
        warnings.append("roughness of appearance '{}' could not be read; 0 was used".format(name))

    opacity = float(colour.opacity)
    if opacity >= 255 and props is not None:
        transparency = _float_from_property(_property(props, "generic_transparency"))
        if transparency is not None and transparency > 0:
            opacity = max(0.0, min(1.0, 1.0 - transparency)) * 255.0

    try:
        if appearance.hasTexture:
            warnings.append("appearance '{}' uses a texture; only its base colour was exported".format(name))
    except Exception:
        pass

    return AppearanceValues(
        name=name,
        rgba255=(float(colour.red), float(colour.green), float(colour.blue), opacity),
        metallic=metallic,
        roughness=roughness,
        warnings=warnings,
    )
