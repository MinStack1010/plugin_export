"""Exception types.  The message of an ExportError is shown to the NX user."""


class ExportError(Exception):
    """Base class for all expected export failures (shown to the user)."""


class NoActivePartError(ExportError):
    """No part is displayed in the NX session (SW: "No active SOLIDWORKS document.")."""


class UnsupportedModelError(ExportError):
    """The displayed part is not a part/assembly that can be exported."""


class OutputPathError(ExportError):
    """The output path is invalid or not writable."""


class TessellationError(ExportError):
    """NX could not tessellate a body."""


class EmptyGeometryError(ExportError):
    """No exportable triangles were produced (an empty GLB is never written)."""


class MeshValidationError(ExportError):
    """An extracted mesh is internally inconsistent."""
