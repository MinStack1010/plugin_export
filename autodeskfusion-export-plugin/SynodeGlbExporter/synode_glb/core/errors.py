"""Exception types shared by the exporter (CAD independent)."""


class ExportError(Exception):
    """An expected export failure with a message that can be shown to the user."""


class ExportCancelled(ExportError):
    """Raised when the user cancels a running export."""


class ConfigError(ExportError):
    """Raised when config.json is missing required values or contains invalid ones."""


class UploadError(ExportError):
    """Raised when uploading the GLB file to the configured server fails."""
