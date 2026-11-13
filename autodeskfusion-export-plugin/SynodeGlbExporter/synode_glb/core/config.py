"""Add-in configuration (replaces the hard-coded ExportOptions of the SolidWorks plugin)."""

import json
import math
import os
from dataclasses import dataclass

from .errors import ConfigError

MESH_QUALITIES = ("low", "normal", "high", "very_high")
UP_AXES = ("auto", "y", "z")


@dataclass
class ExportConfig:
    # Multiplier from Fusion internal length unit (cm) to output units. 0.01 -> metres,
    # which matches the SolidWorks plugin output (SolidWorks API is in metres, UnitScale = 1).
    unit_scale: float = 0.01
    upload_url: str = ""
    api_key: str = "API_KEY_DEV"
    upload_timeout_seconds: float = 120.0
    default_file_name: str = "fusion-export.glb"
    mesh_quality: str = "high"
    up_axis: str = "auto"


def _number(data, key, default):
    value = data.get(key, default)
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value <= 0:
        raise ConfigError("config.json: '{}' must be a positive number.".format(key))
    return float(value)


def _text(data, key, default):
    value = data.get(key, default)
    if not isinstance(value, str):
        raise ConfigError("config.json: '{}' must be a string.".format(key))
    return value


def _choice(data, key, default, choices):
    value = _text(data, key, default).strip().lower()
    if value not in choices:
        raise ConfigError("config.json: '{}' must be one of: {}.".format(key, ", ".join(choices)))
    return value


def parse_config(data: dict) -> ExportConfig:
    if not isinstance(data, dict):
        raise ConfigError("config.json must contain a JSON object.")
    defaults = ExportConfig()
    file_name = _text(data, "default_file_name", defaults.default_file_name).strip() or defaults.default_file_name
    if not file_name.lower().endswith(".glb"):
        file_name += ".glb"
    return ExportConfig(
        unit_scale=_number(data, "unit_scale", defaults.unit_scale),
        upload_url=_text(data, "upload_url", defaults.upload_url).strip(),
        api_key=_text(data, "api_key", defaults.api_key),
        upload_timeout_seconds=_number(data, "upload_timeout_seconds", defaults.upload_timeout_seconds),
        default_file_name=file_name,
        mesh_quality=_choice(data, "mesh_quality", defaults.mesh_quality, MESH_QUALITIES),
        up_axis=_choice(data, "up_axis", defaults.up_axis, UP_AXES),
    )


def load_config(path: str) -> ExportConfig:
    """Loads config.json; a missing file yields the defaults."""
    if not os.path.exists(path):
        return ExportConfig()
    try:
        with open(path, "r", encoding="utf-8-sig") as handle:
            data = json.load(handle)
    except json.JSONDecodeError as exc:
        raise ConfigError("config.json is not valid JSON (line {}, column {}): {}".format(exc.lineno, exc.colno, exc.msg))
    except OSError as exc:
        raise ConfigError("Cannot read config.json: {}".format(exc))
    return parse_config(data)
