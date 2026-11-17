"""Synode NX GLB Exporter - entry journal ("Export to GLB").

Run inside Siemens NX:
  * menu Synode > Export to GLB / ribbon button (startup\\synode_glb_exporter.men/.rtb), or
  * File > Execute > NX Open... (Ctrl+U) and pick this file, or
  * Tools > Journal > Play (Alt+F8).
Batch (no UI):  run_journal.exe synode_export_glb.py -args <model.prt> <output.glb>

This file only locates the package and calls synode_nx_exporter.nx.command.main.
"""

import os
import sys


def _application_dir():
    candidates = []
    try:
        candidates.append(os.path.dirname(os.path.abspath(__file__)))
    except NameError:
        pass
    home = os.environ.get("SYNODE_NX_EXPORTER_HOME", "").strip()
    if home:
        candidates.append(os.path.join(home, "application"))
        candidates.append(home)
    user_dir = os.environ.get("UGII_USER_DIR", "").strip()
    if user_dir:
        candidates.append(os.path.join(user_dir, "application"))
    if sys.argv and sys.argv[0]:
        candidates.append(os.path.dirname(os.path.abspath(sys.argv[0])))
    for folder in candidates:
        if os.path.isdir(os.path.join(folder, "synode_nx_exporter")):
            return folder
    raise RuntimeError("synode_nx_exporter package not found next to the journal "
                       "(set SYNODE_NX_EXPORTER_HOME). Searched: " + "; ".join(candidates))


def _reload_package(app_dir):
    if app_dir not in sys.path:
        sys.path.insert(0, app_dir)
    # NX keeps one Python interpreter per session: drop cached modules so an
    # updated plugin is picked up without restarting NX.
    for name in list(sys.modules):
        if name == "synode_nx_exporter" or name.startswith("synode_nx_exporter."):
            del sys.modules[name]


def run(args):
    app_dir = _application_dir()
    _reload_package(app_dir)
    from synode_nx_exporter.nx.command import main
    return main(args, app_dir=app_dir)


if __name__ == "__main__":
    run(sys.argv[1:])
