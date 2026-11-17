"""The "Export to GLB" command (port of ExportGlbCommand.Execute).

Modes (arguments passed to the entry journal):
  no argument            interactive: Display Part, NX file box, message boxes
  <output.glb>           Display Part -> given file (no file box)
  <model.prt> <out.glb>  batch: open the part as display part, export, no UI

The CAD model is never modified or saved: only read-only queries and a
temporary facet model (deleted immediately) are used.
"""

import os
import traceback

from .. import PRODUCT_NAME, __version__
from ..config import ExportOptions
from ..errors import ExportError, NoActivePartError
from ..exporter import export_scene_to_glb
from ..logger import ExportLogger
from ..output_path import normalize_output_path, check_writable
from . import NxContext
from .extractor import NxSceneExtractor, NO_ACTIVE_PART_MESSAGE
from .ui import NxUi

SUCCESS_MESSAGE = "GLB exported successfully:\n{}"


class ExportCommand:
    def __init__(self, ctx, options, log, ui):
        self.ctx = ctx
        self.options = options
        self.log = log
        self.ui = ui

    def run(self, args):
        """Return the written path, or None when cancelled / failed (already reported)."""
        try:
            return self._run(list(args or []))
        except ExportError as exc:
            self.log.error(str(exc))
            self.ui.error(str(exc))
        except Exception as exc:  # unexpected: keep NX alive, show + log details
            self.log.error("Unexpected error: {}\n{}".format(exc, traceback.format_exc()))
            message = "Export failed: {}".format(exc)
            if self.log.path:
                message += "\n\nDetails: {}".format(self.log.path)
            self.ui.error(message)
        return None

    def _run(self, args):
        session = self.ctx.session
        if len(args) >= 2:
            part = self._open_part(args[0])
            requested = args[1]
        else:
            part = session.Parts.Display
            requested = args[0] if args else None
        if part is None:
            raise NoActivePartError(NO_ACTIVE_PART_MESSAGE)

        if requested is None:
            chosen = self.ui.ask_output_path(_part_folder(part), self.options.default_file_name)
            if chosen is None:
                self.log.info("Export cancelled by the user.")
                return None
            path = normalize_output_path(chosen)
            if os.path.exists(path) and not self.ui.confirm_overwrite(path):
                self.log.info("Export cancelled (file exists): {}".format(path))
                return None
        else:
            path = normalize_output_path(requested)

        check_writable(path)  # fail fast before the (possibly long) extraction

        extractor = NxSceneExtractor(self.ctx, self.options, self.log)
        mark = None
        try:
            mark = session.SetUndoMark(self.ctx.nx.Session.MarkVisibility.Invisible, "Synode GLB export")
        except Exception:
            mark = None
        try:
            scene = extractor.extract(part)
        finally:
            if mark is not None:
                try:
                    session.DeleteUndoMark(mark, None)
                except Exception:
                    pass

        result = export_scene_to_glb(scene, path, self.options, self.log)
        message = SUCCESS_MESSAGE.format(result.path)
        if result.warnings:
            message += "\n\n{} warning(s) - see log: {}".format(len(result.warnings), self.log.path or "NX log file")
        self.ui.info(message)
        return result.path

    def _open_part(self, part_path):
        if not os.path.isfile(part_path):
            raise ExportError("The part file does not exist:\n{}".format(part_path))
        self.log.info("Opening part: {}".format(part_path))
        try:
            opened = self.ctx.session.Parts.OpenBaseDisplay(os.path.abspath(part_path))
        except Exception as exc:
            raise ExportError("NX could not open the part:\n{}\n{}".format(part_path, exc))
        part = opened[0] if isinstance(opened, (tuple, list)) else opened
        return part


def _part_folder(part):
    try:
        full = part.FullPath
        if full:
            return os.path.dirname(str(full))
    except Exception:
        pass
    return ""


def main(args=None, app_dir=None):
    """Entry point called by the journal ``synode_export_glb.py``."""
    ctx = NxContext.from_running_nx(need_ui=True)
    batch = args is not None and len(args) >= 2
    if batch:
        ctx.ui = None
    try:
        options, config_path = ExportOptions.load([app_dir] if app_dir else [])
    except Exception as exc:
        options, config_path = None, exc
    log = ExportLogger(options.log_directory if options else "")
    try:
        log.add_sink(ctx.session.LogFile.WriteLine)
    except Exception:
        pass
    ui = NxUi(ctx, log)
    log.info("{} {} started (args={})".format(PRODUCT_NAME, __version__, list(args or [])))
    if options is None:
        ui.error("Invalid configuration file: {}".format(config_path))
        return None
    if config_path:
        log.info("Configuration: {}".format(config_path))
    return ExportCommand(ctx, options, log, ui).run(args or [])
