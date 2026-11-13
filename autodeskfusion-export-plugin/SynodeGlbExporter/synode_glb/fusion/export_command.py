"""Fusion UI integration: toolbar button, command dialog and export workflow.

Equivalent of SwAddin.cs (command group / toolbar button) and
Commands/ExportGlbCommand.cs (save dialog, export, success / error messages).
"""

import os
import tempfile
import traceback
import uuid

import adsk.core
import adsk.fusion

from ..core import config as config_module
from ..core.coordinate_converter import ConversionOptions, convert_fusion_scene_to_gltf
from ..core.errors import ConfigError, ExportCancelled, ExportError
from ..core.gltf_writer import write_glb
from ..core.output_path import normalize_output_path
from ..core.scene import scene_statistics
from ..core.uploader import upload_glb
from .geometry_extractor import FusionGeometryExtractor

CMD_ID = "SynodeGlbExporter_ExportGlb"
CMD_NAME = "Export to GLB"
CMD_TOOLTIP = "Export active Fusion design to GLB"
CMD_DESCRIPTION = "Export the active Fusion design (components, bodies, transforms and appearances) to a GLB file."
WORKSPACE_ID = "FusionSolidEnvironment"
PANEL_ID = "SolidScriptsAddinsPanel"
LOG_PREFIX = "[Synode GLB] "

DEST_FILE = "Save to file"
DEST_UPLOAD = "Upload to server"
QUALITY_LABELS = [("low", "Low"), ("normal", "Normal"), ("high", "High"), ("very_high", "Very high")]
AXIS_Y = "Y-up (no conversion)"
AXIS_Z = "Z-up -> rotate to glTF Y-up"

_ADDIN_DIR = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
RESOURCE_DIR = os.path.join(_ADDIN_DIR, "resources", "export_glb")
CONFIG_PATH = os.path.join(_ADDIN_DIR, "config.json")

# Fusion only keeps weak references to event handlers: they must stay alive here.
_app_handlers = []
_command_handlers = []


# ---------------------------------------------------------------- lifecycle
def start():
    app = adsk.core.Application.get()
    ui = app.userInterface

    stale = ui.commandDefinitions.itemById(CMD_ID)
    if stale:
        stale.deleteMe()

    command_definition = ui.commandDefinitions.addButtonDefinition(CMD_ID, CMD_NAME, CMD_TOOLTIP, RESOURCE_DIR)
    command_definition.tooltipDescription = CMD_DESCRIPTION
    handler = _CommandCreatedHandler()
    command_definition.commandCreated.add(handler)
    _app_handlers.append(handler)

    panel = _toolbar_panel(ui)
    if panel is None:
        raise RuntimeError("Fusion toolbar panel '{}' was not found in workspace '{}'.".format(PANEL_ID, WORKSPACE_ID))
    existing = panel.controls.itemById(CMD_ID)
    if existing:
        existing.deleteMe()
    control = panel.controls.addCommand(command_definition)
    control.isPromoted = True


def stop():
    try:
        ui = adsk.core.Application.get().userInterface
        panel = _toolbar_panel(ui)
        if panel is not None:
            control = panel.controls.itemById(CMD_ID)
            if control:
                control.deleteMe()
        command_definition = ui.commandDefinitions.itemById(CMD_ID)
        if command_definition:
            command_definition.deleteMe()
    finally:
        _app_handlers.clear()
        _command_handlers.clear()


def _toolbar_panel(ui):
    workspace = ui.workspaces.itemById(WORKSPACE_ID)
    if workspace is None:
        return None
    return workspace.toolbarPanels.itemById(PANEL_ID)


# ----------------------------------------------------------------- helpers
def _log(message: str) -> None:
    try:
        adsk.core.Application.get().log(LOG_PREFIX + message)
    except Exception:
        pass


def _active_design():
    app = adsk.core.Application.get()
    product = app.activeProduct
    return adsk.fusion.Design.cast(product) if product is not None else None


def _document_name():
    app = adsk.core.Application.get()
    try:
        document = app.activeDocument
        return document.name if document is not None else None
    except Exception:
        return None


def _preferred_up_axis() -> str:
    """Best-effort guess from Fusion's 'default modeling orientation' preference."""
    try:
        preferences = adsk.core.Application.get().preferences.generalPreferences
        if preferences.defaultModelingOrientation == adsk.core.DefaultModelingOrientations.ZUpModelingOrientation:
            return "z"
    except Exception:
        pass
    return "y"


def _ask_output_path(ui, default_file_name: str):
    dialog = ui.createFileDialog()
    dialog.title = "Export to GLB"
    dialog.filter = "GLB files (*.glb)"
    dialog.initialFilename = default_file_name
    if dialog.showSave() != adsk.core.DialogResults.DialogOK:
        return None
    return dialog.filename


# ----------------------------------------------------------- event handlers
class _CommandCreatedHandler(adsk.core.CommandCreatedEventHandler):
    def notify(self, args):
        ui = adsk.core.Application.get().userInterface
        try:
            command = adsk.core.CommandCreatedEventArgs.cast(args).command
            command.okButtonText = "Export"
            inputs = command.commandInputs

            config_error = None
            try:
                config = config_module.load_config(CONFIG_PATH)
            except ConfigError as exc:
                config_error = str(exc)
                config = config_module.ExportConfig()

            design = _active_design()
            if design is not None:
                design_text = _document_name() or "(unnamed design)"
            else:
                design_text = "No active Fusion design. Open a design in the DESIGN workspace."
            inputs.addTextBoxCommandInput("design", "Design", design_text, 1, True)

            destination = inputs.addDropDownCommandInput(
                "destination", "Destination", adsk.core.DropDownStyles.TextListDropDownStyle)
            destination.listItems.add(DEST_FILE, True)
            if config.upload_url:
                destination.listItems.add(DEST_UPLOAD, False)

            quality = inputs.addDropDownCommandInput(
                "quality", "Mesh quality", adsk.core.DropDownStyles.TextListDropDownStyle)
            for key, label in QUALITY_LABELS:
                quality.listItems.add(label, key == config.mesh_quality)

            axis = config.up_axis if config.up_axis != "auto" else _preferred_up_axis()
            up_axis = inputs.addDropDownCommandInput(
                "up_axis", "Design up axis", adsk.core.DropDownStyles.TextListDropDownStyle)
            up_axis.listItems.add(AXIS_Y, axis == "y")
            up_axis.listItems.add(AXIS_Z, axis == "z")

            units_text = "Output units: metres (Fusion cm x {:g})".format(config.unit_scale)
            if config.unit_scale != 0.01:
                units_text = "Output scale: Fusion cm x {:g} (from config.json)".format(config.unit_scale)
            inputs.addTextBoxCommandInput("units", "", units_text, 1, True)

            if config_error:
                inputs.addTextBoxCommandInput("config_error", "Config error", config_error, 3, True)

            on_execute = _ExecuteHandler(config, config_error)
            command.execute.add(on_execute)
            on_validate = _ValidateInputsHandler(config_error)
            command.validateInputs.add(on_validate)
            on_destroy = _DestroyHandler()
            command.destroy.add(on_destroy)
            _command_handlers.extend([on_execute, on_validate, on_destroy])
        except Exception:
            _log("Command creation failed:\n" + traceback.format_exc())
            ui.messageBox("Failed to open the GLB export command:\n{}".format(traceback.format_exc()))


class _ValidateInputsHandler(adsk.core.ValidateInputsEventHandler):
    def __init__(self, config_error):
        super().__init__()
        self._config_error = config_error

    def notify(self, args):
        event_args = adsk.core.ValidateInputsEventArgs.cast(args)
        event_args.areInputsValid = self._config_error is None and _active_design() is not None


class _DestroyHandler(adsk.core.CommandEventHandler):
    def notify(self, args):
        _command_handlers.clear()


class _ExecuteHandler(adsk.core.CommandEventHandler):
    def __init__(self, config, config_error):
        super().__init__()
        self._config = config
        self._config_error = config_error

    def notify(self, args):
        app = adsk.core.Application.get()
        ui = app.userInterface
        progress = None
        try:
            if self._config_error:
                raise ConfigError(self._config_error)
            inputs = adsk.core.CommandEventArgs.cast(args).command.commandInputs
            destination = inputs.itemById("destination").selectedItem.name
            quality_label = inputs.itemById("quality").selectedItem.name
            quality = next(key for key, label in QUALITY_LABELS if label == quality_label)
            convert_axis = inputs.itemById("up_axis").selectedItem.name == AXIS_Z

            design = _active_design()
            if design is None:
                raise ExportError("No active Fusion design. Open a design in the DESIGN workspace and try again.")

            output_path = None
            if destination == DEST_FILE:
                selected = _ask_output_path(ui, self._config.default_file_name)
                if not selected:
                    return  # user cancelled the save dialog
                output_path = normalize_output_path(selected)

            progress = ui.createProgressDialog()
            progress.isCancelButtonShown = True
            progress.show("Export to GLB", "Preparing export...", 0, 1, 0)

            def on_progress(done, total, label):
                progress.maximumValue = max(total, 1)
                progress.progressValue = min(done, max(total, 1))
                progress.message = "Tessellating %v of %m: " + label
                adsk.doEvents()
                if progress.wasCancelled:
                    raise ExportCancelled("Export cancelled.")

            extractor = FusionGeometryExtractor(quality, on_progress)
            fusion_scene = extractor.extract(design, _document_name())
            gltf_scene = convert_fusion_scene_to_gltf(
                fusion_scene, ConversionOptions(self._config.unit_scale, convert_axis))

            stats = scene_statistics(gltf_scene)
            if stats["meshes"] == 0:
                raise ExportError("The active design contains no visible solid bodies to export.")

            progress.message = "Writing GLB..."
            adsk.doEvents()

            if output_path is not None:
                size = write_glb(gltf_scene, output_path)
                headline = "GLB exported successfully:\n{}".format(output_path)
            else:
                temp_path = os.path.join(tempfile.gettempdir(), "fusion-export-{}.glb".format(uuid.uuid4()))
                try:
                    size = write_glb(gltf_scene, temp_path)
                    progress.message = "Uploading GLB..."
                    adsk.doEvents()
                    upload_glb(temp_path, self._config.upload_url, self._config.api_key,
                               self._config.upload_timeout_seconds)
                finally:
                    try:
                        if os.path.exists(temp_path):
                            os.remove(temp_path)
                    except OSError:
                        _log("Could not delete temporary file: " + temp_path)
                headline = "GLB uploaded successfully."

            progress.hide()
            progress = None
            message = "{}\n\nNodes: {}   Meshes: {}   Triangles: {}   Size: {:,} bytes".format(
                headline, stats["nodes"], stats["meshes"], stats["triangles"], size)
            warnings = extractor.report.warnings()
            if warnings:
                message += "\n\nWarnings:\n- " + "\n- ".join(warnings)
            _log(message.replace("\n", " | "))
            ui.messageBox(message, "Export to GLB",
                          adsk.core.MessageBoxButtonTypes.OKButtonType,
                          adsk.core.MessageBoxIconTypes.InformationIconType)
        except ExportCancelled:
            _log("Export cancelled by the user.")
            if progress is not None:
                progress.hide()
                progress = None
            ui.messageBox("Export cancelled.", "Export to GLB")
        except ExportError as exc:
            _log("Export failed: {}".format(exc))
            if progress is not None:
                progress.hide()
                progress = None
            ui.messageBox(str(exc), "Export to GLB",
                          adsk.core.MessageBoxButtonTypes.OKButtonType,
                          adsk.core.MessageBoxIconTypes.CriticalIconType)
        except Exception as exc:
            details = traceback.format_exc()
            _log("Unexpected export error:\n" + details)
            if progress is not None:
                progress.hide()
                progress = None
            ui.messageBox("{}\n\nDetails were written to the TEXT COMMANDS window.".format(exc or type(exc).__name__),
                          "Export to GLB",
                          adsk.core.MessageBoxButtonTypes.OKButtonType,
                          adsk.core.MessageBoxIconTypes.CriticalIconType)
