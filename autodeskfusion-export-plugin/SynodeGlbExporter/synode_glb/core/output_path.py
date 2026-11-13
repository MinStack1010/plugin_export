"""Validation of the output file chosen by the user."""

import os

from .errors import ExportError


def normalize_output_path(path: str) -> str:
    """Returns an absolute .glb path or raises ExportError with a user-facing reason."""
    if path is None or not str(path).strip():
        raise ExportError("No output file was specified.")
    path = os.path.abspath(os.path.expanduser(str(path).strip()))
    if not path.lower().endswith(".glb"):
        path += ".glb"
    if os.path.isdir(path):
        raise ExportError("The output path is a folder, not a file: {}".format(path))
    folder = os.path.dirname(path)
    if not os.path.isdir(folder):
        raise ExportError("The output folder does not exist: {}".format(folder))
    if not os.access(folder, os.W_OK):
        raise ExportError("The output folder is not writable: {}".format(folder))
    if os.path.exists(path) and not os.access(path, os.W_OK):
        raise ExportError("The output file exists and is read-only: {}".format(path))
    return path
