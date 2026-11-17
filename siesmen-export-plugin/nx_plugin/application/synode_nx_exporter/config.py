"""Export options.

Defaults reproduce the behaviour of the SOLIDWORKS plugin
(``ExportOptions`` + ``GltfSceneWriter`` defaults).  Options can be
overridden with an optional JSON file named ``synode_nx_exporter.json``
placed next to the entry journal, or in the folder given by the
environment variable ``SYNODE_NX_EXPORTER_CONFIG`` (full file path).
"""

import json
import os
from dataclasses import dataclass, fields, asdict

CONFIG_FILE_NAME = "synode_nx_exporter.json"
CONFIG_ENV_VAR = "SYNODE_NX_EXPORTER_CONFIG"


@dataclass
class ExportOptions:
    # --- output -----------------------------------------------------------
    # SOLIDWORKS default file name is "solidworks-export.glb".
    default_file_name: str = "nx-export.glb"

    # --- coordinates / units ------------------------------------------------
    # Geometry is converted from the part units (mm, inch, ...) to metres by
    # the extractor (SOLIDWORKS API already reports metres).  ``unit_scale``
    # is an extra factor applied afterwards (SW ExportOptions.UnitScale = 1).
    unit_scale: float = 1.0
    # SOLIDWORKS plugin performs NO axis conversion.  Keep False for parity.
    z_up_to_y_up: bool = False

    # --- tessellation (UF_FACET) ---------------------------------------------
    # Chordal (surface distance) tolerance in millimetres and angular
    # tolerance in degrees.  None = keep NX defaults (UF_FACET_ask_default_parameters).
    chord_tolerance_mm: float = 0.05
    angular_tolerance_deg: float = 15.0

    # --- content selection ----------------------------------------------------
    skip_blanked_bodies: bool = True        # SW: GetBodies2(visibleOnly=true)
    skip_hidden_layer_bodies: bool = True   # NX specific visibility
    skip_sheet_bodies: bool = True          # SW: swSolidBody only
    include_root_part_bodies: bool = True   # bodies modelled directly in the assembly part
    load_partially_loaded_parts: bool = True
    # "path" reproduces SW Component2.Name2 ("Sub-1/Part-1"); "leaf" = "Part-1".
    component_name_style: str = "path"

    # --- materials (SW defaults: metallic 0, roughness 0) ---------------------
    default_metallic: float = 0.0
    default_roughness: float = 0.0

    # --- writer ------------------------------------------------------------
    # SW writes one glTF mesh per occurrence (no sharing).  True = instances of
    # the same part share one glTF mesh (smaller file, same visual result).
    share_instance_meshes: bool = False

    # --- diagnostics -------------------------------------------------------
    validate_transforms: bool = True
    log_directory: str = ""                 # "" = %TEMP%\SynodeNX

    def validate(self):
        from .errors import ExportError
        if not (self.unit_scale > 0):
            raise ExportError("unit_scale must be greater than zero.")
        if self.chord_tolerance_mm is not None and not (self.chord_tolerance_mm > 0):
            raise ExportError("chord_tolerance_mm must be greater than zero.")
        if self.angular_tolerance_deg is not None and not (0 < self.angular_tolerance_deg < 90):
            raise ExportError("angular_tolerance_deg must be between 0 and 90 degrees.")
        if self.component_name_style not in ("path", "leaf"):
            raise ExportError("component_name_style must be 'path' or 'leaf'.")
        return self

    def to_dict(self):
        return asdict(self)

    @classmethod
    def from_dict(cls, data):
        known = {f.name for f in fields(cls)}
        unknown = sorted(set(data) - known)
        if unknown:
            raise ValueError("Unknown option(s): " + ", ".join(unknown))
        return cls(**data).validate()

    @classmethod
    def load(cls, search_dirs=()):
        """Load options from the env var file or the first config file found."""
        candidates = []
        env_path = os.environ.get(CONFIG_ENV_VAR, "").strip()
        if env_path:
            candidates.append(env_path)
        candidates.extend(os.path.join(d, CONFIG_FILE_NAME) for d in search_dirs if d)
        for path in candidates:
            if os.path.isfile(path):
                with open(path, "r", encoding="utf-8") as handle:
                    return cls.from_dict(json.load(handle)), path
        return cls().validate(), None
