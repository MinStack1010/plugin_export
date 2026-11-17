"""Output path validation and safe (atomic) writing of the GLB file."""

import os
import tempfile

from .errors import OutputPathError

GLB_EXTENSION = ".glb"
_INVALID_CHARS = set('<>"|?*') | {chr(c) for c in range(32)}
_RESERVED = {"CON", "PRN", "AUX", "NUL"} | {"COM%d" % i for i in range(1, 10)} | {"LPT%d" % i for i in range(1, 10)}


def normalize_output_path(path):
    """Validate the path syntactically and return an absolute .glb path.

    Like the SW SaveFileDialog (filter *.glb, AddExtension), a missing
    extension is completed with ".glb"; another extension is rejected.
    """
    if path is None or not str(path).strip():
        raise OutputPathError("No output file was specified.")
    path = os.path.expandvars(str(path).strip().strip('"'))
    drive, rest = os.path.splitdrive(path)
    if any(ch in _INVALID_CHARS for ch in rest) or ":" in rest:
        raise OutputPathError("The output path contains invalid characters:\n{}".format(path))
    base = os.path.basename(rest)
    if not base or base in (".", ".."):
        raise OutputPathError("The output path does not contain a file name:\n{}".format(path))
    stem, ext = os.path.splitext(base)
    if stem.upper().rstrip(" .") in _RESERVED:
        raise OutputPathError("The output file name is reserved by Windows:\n{}".format(path))
    if base.endswith((" ", ".")):
        raise OutputPathError("The output file name cannot end with a space or a dot:\n{}".format(path))
    if ext == "":
        path += GLB_EXTENSION
    elif ext.lower() != GLB_EXTENSION:
        raise OutputPathError("The output file must have the .glb extension:\n{}".format(path))
    return os.path.abspath(path)


def check_writable(path):
    """Check that the folder exists and that a file can be created in it.

    Raises OutputPathError with a user friendly message.  Nothing is left
    behind on disk.
    """
    folder = os.path.dirname(path) or os.getcwd()
    if not os.path.isdir(folder):
        raise OutputPathError("The output folder does not exist:\n{}".format(folder))
    if os.path.isdir(path):
        raise OutputPathError("The output path is a folder, not a file:\n{}".format(path))
    if os.path.exists(path) and not os.access(path, os.W_OK):
        raise OutputPathError("The existing output file is read-only:\n{}".format(path))
    try:
        fd, probe = tempfile.mkstemp(prefix=".synode_probe_", suffix=".tmp", dir=folder)
        os.close(fd)
        os.remove(probe)
    except PermissionError:
        raise OutputPathError("No permission to write to the output folder:\n{}".format(folder))
    except OSError as exc:
        raise OutputPathError("Cannot write to the output folder:\n{}\n{}".format(folder, exc.strerror or exc))


def write_atomic(path, data):
    """Write bytes to a temporary file in the same folder, then replace the target.

    A failed export never leaves a truncated GLB behind.
    """
    if not data:
        raise OutputPathError("Refusing to write an empty file.")
    folder = os.path.dirname(path) or os.getcwd()
    tmp = None
    try:
        fd, tmp = tempfile.mkstemp(prefix=".synode_", suffix=".glb.tmp", dir=folder)
        with os.fdopen(fd, "wb") as handle:
            handle.write(data)
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(tmp, path)
        tmp = None
    except PermissionError:
        raise OutputPathError("No permission to write the output file (it may be read-only or open in another program):\n{}".format(path))
    except OSError as exc:
        raise OutputPathError("Failed to write the output file:\n{}\n{}".format(path, exc.strerror or exc))
    finally:
        if tmp and os.path.exists(tmp):
            try:
                os.remove(tmp)
            except OSError:
                pass
