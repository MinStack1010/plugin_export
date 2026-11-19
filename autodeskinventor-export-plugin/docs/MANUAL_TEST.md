# Manual test guide (Tests 2–6)

Record results as: PASS / FAIL / NOT TESTED. Log: `%LOCALAPPDATA%\Synode\InventorExporter\logs\SynodeInventor.log`.

Preparation: `build.ps1` → `install.ps1` → restart Inventor 2027.

## Test 2 — Add-in loading

| # | Step | Expected | Result |
|---|---|---|---|
| 2.1 | Tools › Add-Ins | `Synode Inventor Exporter` is listed with status Loaded | |
| 2.2 | Open or create a Part | Tab **Synode** › panel **Synode** › button **Export to GLB** with an icon | |
| 2.3 | Open or create an Assembly | Same tab/button | |
| 2.4 | Untick Loaded in Add-Ins, then tick it again | The tab disappears and reappears, without duplicates | |
| 2.5 | Log | Contains the line `Activate completed.` | |

## Test 3 — Part → GLB

| # | Step | Expected | Result |
|---|---|---|---|
| 3.1 | Open an `.ipt` with a solid → Export to GLB | The save dialog suggests `<part name>.glb` in the part's folder | |
| 3.2 | Save | Success message with path, file size and triangle count | |
| 3.3 | Open the file in https://gltf-viewer.donmccurdy.com or Windows 3D Viewer | Correct shape, colors and size | |
| 3.4 | Check the log entry for this export; optionally drop the file into the [Khronos glTF Validator](https://github.khronos.org/glTF-Validator/) | Log contains `Validation: GLB v2, ...` followed by `Export OK`; the Khronos validator reports no errors | |
| 3.5 | Unsaved Part (File › New) with a body | Suggested name `Part1.glb`, export succeeds | |

## Test 4 — Assembly → GLB

| # | Step | Expected | Result |
|---|---|---|---|
| 4.1 | Open an `.iam` with many components (including a sub-assembly) → Export | Succeeds | |
| 4.2 | View in a viewer | Component positions/orientations match Inventor, node hierarchy is present | |
| 4.3 | Hidden / suppressed component | Record the behavior (determined by the native translator) | |

## Test 5 — Error cases

| # | Step | Expected | Result |
|---|---|---|---|
| 5.1 | Click Export while a Drawing is open | "not supported" warning, no crash | |
| 5.2 | Empty Part (no bodies) | "has no solid, surface or mesh bodies" warning | |
| 5.3 | Empty Assembly | "has no components" warning | |
| 5.4 | Cancel in the save dialog | No error message, status bar shows "export cancelled" | |
| 5.5 | Overwrite a `.glb` that is open in another program (file locked) | "in use by another program" error | |
| 5.6 | Save to a folder without write permission (e.g. `C:\Windows`) | "Cannot write to the output folder" error | |
| 5.7 | Type a name with the `.gltf` extension | The file is saved as `.glb` | |
| 5.8 | After each error | No `*.synode-tmp-*.glb` file remains in the destination folder | |

## Test 6 — Stability

| # | Step | Expected | Result |
|---|---|---|---|
| 6.1 | Export the same model 5 times in a row | All succeed, Inventor does not slow down/hang | |
| 6.2 | Export a large assembly | Completes, duration recorded in the log | |
| 6.3 | Close Inventor | No errors; log contains `Deactivate completed.` | |
| 6.4 | Run `uninstall.ps1`, then open Inventor | The Synode tab is gone | |
