# Synode Inventor Exporter — Autodesk Inventor Professional 2027

**Synode** add-in for Autodesk Inventor 2027: exports the open Part (`.ipt`) or Assembly (`.iam`) to a **GLB (glTF 2.0 binary)** file with a single button click.

> Internal development/testing project, running on Inventor Professional 2027 Trial (non-commercial). The add-in does not interfere with licensing.

---

## A. Overview

| Component | Description |
|---|---|
| Add-in name | `Synode Inventor Exporter` (Tools › Add-Ins) |
| User interface | Ribbon tab **Synode** › panel **Synode** › button **Export to GLB** (Part and Assembly environments) |
| Export | Uses **Inventor 2027's native glTF translator** (`{856E3C5D-A1F4-4B86-A6C0-8B849C0C8BE0}`, `TranslatorAddIn.SaveCopyAs`) — geometry, component transforms, materials/colors and units are converted by Inventor itself |
| Output validation | Every file is checked by `GlbValidator`: `glTF` magic, version 2, declared length, JSON/BIN chunks, 4-byte alignment, buffer/bufferView/accessor, mesh, node, scene. Invalid files are never written to the destination |
| Loading mechanism | `.addin` manifest file (Inventor scans the Addins folders automatically). **No RegAsm, no registry writes** |
| Technology | C# / .NET (net10.0-windows if the .NET 10 SDK is available, otherwise net8.0-windows), x64, Inventor Interop 31.x, no NuGet packages in the add-in |
| Add-in GUID | `{7001FEF0-B792-4C1D-9FB4-0515C8F2F90B}` |
| Log | `%LOCALAPPDATA%\Synode\InventorExporter\logs\SynodeInventor.log` |

Folder structure:

```text
autodeskinventor-export-plugin\
├── src\SynodeInventor.Core\      # No Inventor dependency: GlbValidator, ExportPathHelper, logger, error codes, icon
├── src\SynodeInventor.AddIn\     # Add-in: StandardAddInServer, RibbonUi, ExportGlbCommand, services
├── scripts\                      # Find-Inventor.ps1, Test-ScriptSyntax.ps1
├── docs\                         # ANALYSIS.md, MANUAL_TEST.md
├── build.ps1 / install.ps1 / uninstall.ps1
└── SynodeInventor.sln
```

## B. System requirements

- Windows 10/11 x64.
- Autodesk Inventor Professional 2027 (version 31.x) installed and activated (trial is fine).
- .NET SDK: **.NET 10 SDK x64 recommended** (matches the .NET 10 runtime that Inventor 2027 uses for add-ins). If only SDK 8/9 is installed, `build.ps1` builds `net8.0-windows` and prints a warning.
- No separate Inventor SDK required: uses the `Autodesk.Inventor.Interop.dll` shipped in `Inventor 2027\Bin\Public Assemblies`.
- Scripts are compatible with Windows PowerShell 5.1 and PowerShell 7 (all `.ps1` files contain ASCII characters only).

## C. Build

```powershell
cd C:\plugin_export_3d\autodeskinventor-export-plugin
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

The script: checks the SDK → finds Inventor 2027 through the registry (`RegistryVersion31.0\InventorLocation`) → builds Release x64 → verifies the DLL (PE32+/AMD64, assembly name) → copies the output to `dist\`.

Output in `dist\`: `SynodeInventor.AddIn.dll`, `SynodeInventor.AddIn.pdb`, `Synode.InventorExporter.addin`, `build-info.txt`.

Options: `-Configuration Release|Debug`, `-TargetFramework net10.0-windows`, `-InventorBinDir "<...>\Inventor 2027\Bin"`, `-OutputDir`, `-WorkDir`.

## D. Installation and usage

**Install (per user, no administrator rights required):**

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1 -DryRun   # preview only, changes nothing
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

Files are copied to `%APPDATA%\Autodesk\Inventor 2027\Addins\SynodeInventorExporter\`. The script can be run repeatedly (it overwrites the previous version). If Inventor is running, the script only warns and **does not close Inventor**. To install for all users: `-AllUsers` (requires an Administrator PowerShell; copies to `%ProgramData%`).

**Usage:**

1. Restart Inventor 2027.
2. Open a `.ipt` or `.iam` file.
3. Select the **Synode** tab › **Export to GLB**.
4. Choose where to save (default: same folder and same name as the document, with the `.glb` extension).
5. When finished, a message shows the path, file size and mesh/node/material/triangle counts. On failure, a clear error message is shown together with the log path.

To check that the add-in is loaded: **Tools › Add-Ins** › `Synode Inventor Exporter` › status *Loaded*; you can tick *Load Automatically*.

## E. Verification

```powershell
# Check syntax + encoding of every PowerShell script
powershell -ExecutionPolicy Bypass -File .\scripts\Test-ScriptSyntax.ps1
```

Every export is validated automatically by the built-in `GlbValidator` before the file is moved to its destination; the result is written to the log (`Validation: ...`). For an independent check, open the `.glb` in the [Khronos glTF Validator](https://github.khronos.org/glTF-Validator/).

Manual testing in the user interface: see [docs/MANUAL_TEST.md](docs/MANUAL_TEST.md).

## F. Uninstall and troubleshooting

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1 -DryRun
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1            # add -AllUsers if installed for all users, -RemoveLogs to delete logs
```

Only Synode files are removed (DLL, PDB, manifest with the matching ClassId). The registry and other add-ins are not touched.

| Symptom | Cause / Solution |
|---|---|
| `ParserError` or garbled characters when running a script | Scripts contain ASCII only; if you edit a script and add non-ASCII characters, save it as **UTF-8 with BOM**. Check with `scripts\Test-ScriptSyntax.ps1` |
| **Synode** tab not visible | Check Tools › Add-Ins; check the log; make sure Inventor was restarted after installation. The tab only exists in the Part/Assembly environments (not on the Home screen) |
| Add-in reports an error while loading | Open the log `%LOCALAPPDATA%\Synode\InventorExporter\logs\SynodeInventor.log`. If a `net8.0-windows` build is rejected by Inventor, install the .NET 10 SDK and rebuild (`build.ps1` picks net10.0 automatically) |
| `install.ps1` reports a file in use | Inventor has the add-in loaded → close Inventor and run again |
| "No document is open" / "not supported" | A Part or Assembly must be open (Drawing `.idw/.dwg` and Presentation `.ipn` are not supported) |
| "has no solid, surface or mesh bodies" | Empty part, or assembly without components |
| "glTF translator ... not available" | Enable **Translator: glTF** in Tools › Add-Ins (Inventor's native translator) |
| "output file is in use" | Close the program that has the `.glb` file open (viewer, browser) or choose another name |
| License / trial expired dialog | Inventor environment issue, not a plugin bug; the add-in does not interfere with licensing |
| `build.ps1` warns that the .NET 10 SDK is missing | net8.0 still builds; installing the .NET 10 SDK x64 is recommended |
