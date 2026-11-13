"""Synode GLB Exporter - Autodesk Fusion add-in entry point.

Fusion calls run() when the add-in is started and stop() when it is stopped or
Fusion shuts down.
"""

import traceback

import adsk.core

from .synode_glb.fusion import export_command


def run(context):
    try:
        export_command.start()
    except Exception:
        adsk.core.Application.get().userInterface.messageBox(
            "Synode GLB Exporter failed to start:\n{}".format(traceback.format_exc()))


def stop(context):
    try:
        export_command.stop()
    except Exception:
        adsk.core.Application.get().userInterface.messageBox(
            "Synode GLB Exporter failed to stop cleanly:\n{}".format(traceback.format_exc()))
