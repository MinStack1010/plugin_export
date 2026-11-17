"""User interaction inside Siemens NX (file dialog + message boxes).

Equivalent of the SOLIDWORKS command UI:
  SaveFileDialog (*.glb, OverwritePrompt) -> UF_UI_create_filebox + overwrite question
  SendMsgToUser2(...)                     -> NXMessageBox.Show(...)

Verified signatures (NXOpen_UF.pyd / NXOpen.pyd docstrings, NX 2606):
  UFUi.CreateFilebox(prompt, title, filterString, defaultName) -> (filter, filename, response)
  UFUi.LockUgAccess(UF_UI_FROM_CUSTOM) / UnlockUgAccess(UF_UI_FROM_CUSTOM)
  NXMessageBox.Show(title, NXMessageBox.DialogType, message) -> int (Question: 1 = Yes)
In batch mode (no UI) messages are written to stdout / the NX log file.
"""

import os

from .. import PRODUCT_NAME

UF_UI_OK_DEFAULT = 2          # UF_UI_OK in uf_ui.h
UF_UI_FROM_CUSTOM_DEFAULT = 1  # UF_UI_FROM_CUSTOM in uf_ui.h


class NxUi:
    def __init__(self, ctx, log=None):
        self.ctx = ctx
        self.log = log
        self.interactive = ctx.ui is not None

    # ------------------------------------------------------------ dialogs
    def ask_output_path(self, default_dir, default_name):
        """Show the NX file box. Returns the chosen path or None if cancelled."""
        uf_ui = self.ctx.uf.Ui
        folder = default_dir if default_dir and os.path.isdir(default_dir) else os.getcwd()
        filter_string = os.path.join(folder, "*.glb")
        default_path = os.path.join(folder, default_name)
        ok = self.ctx.uf_constant("UF_UI_OK", UF_UI_OK_DEFAULT)
        lock = self.ctx.uf_constant("UF_UI_FROM_CUSTOM", UF_UI_FROM_CUSTOM_DEFAULT)
        locked = False
        try:
            try:
                uf_ui.LockUgAccess(lock)
                locked = True
            except Exception:
                locked = False
            try:
                result = uf_ui.CreateFilebox("Export to GLB", "Export to GLB", filter_string, default_path)
            except TypeError:
                result = uf_ui.CreateFilebox("Export to GLB", "Export to GLB", [filter_string], default_path)
        finally:
            if locked:
                try:
                    uf_ui.UnlockUgAccess(lock)
                except Exception:
                    pass
        filename, response = _parse_filebox_result(result)
        if response != ok or not filename:
            return None
        return filename

    def confirm_overwrite(self, path):
        if not self.interactive:
            return True  # batch mode: explicit path given by the caller
        answer = self._show("Export to GLB", "Question",
                            "The file already exists:\n{}\n\nDo you want to replace it?".format(path))
        return answer == 1

    def info(self, message):
        self._notify("Information", message)

    def warning(self, message):
        self._notify("Warning", message)

    def error(self, message):
        self._notify("Error", message)

    # ------------------------------------------------------------ helpers
    def _notify(self, kind, message):
        if self.interactive:
            try:
                self._show(PRODUCT_NAME, kind, message)
                return
            except Exception as exc:
                if self.log:
                    self.log.warning("NXMessageBox failed: {}".format(exc))
        self._print("[{}] {}".format(kind.upper(), message))

    def _show(self, title, kind, message):
        box = self.ctx.ui.NXMessageBox
        dialog_type = getattr(self.ctx.nx.NXMessageBox.DialogType, kind)
        return box.Show(title, dialog_type, message)

    def _print(self, text):
        try:
            print(text)
        except Exception:
            pass
        try:
            self.ctx.session.LogFile.WriteLine(text)
        except Exception:
            pass


def _parse_filebox_result(result):
    """(filter, filename, response) where filename may be a str or a list."""
    if not isinstance(result, (tuple, list)) or len(result) < 2:
        return None, None
    response = result[-1]
    filename = result[-2] if len(result) >= 3 else result[0]
    if isinstance(filename, (list, tuple)):
        filename = filename[0] if filename else None
    if filename is not None:
        filename = str(filename).strip()
    return filename, response
