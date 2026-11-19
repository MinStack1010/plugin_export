# Synode NX GLB Exporter

A **Siemens NX** plugin that exports the displayed part or assembly to a **GLB (binary glTF 2.0)** file. It is a rewrite of the SOLIDWORKS plugin `PluginSynode` (*Export to GLB* command) located in `..\solidworks-export-plugin` (that project is not modified).

> [!IMPORTANT]
> The plugin **has not yet been run in a real NX session**. On the development machine (Designcenter NX 2606 Student Edition), `run_journal.exe` reports a license error: *"SPLM_LICENSE_SERVER needs to point to a license server"*. The NX-independent logic was checked outside NX and the GLB writer layout was designed to match the original SOLIDWORKS writer, but the NX Open calls (UF_FACET, GetPosition, file box, menu/ribbon) still need to be verified directly in NX. See [Testing in NX](#testing-in-nx).

## 1. Features

| Feature | Description |
|---|---|
| *Export to GLB* command | Menu **Synode › Export to GLB**, ribbon tab **Synode**, or run the journal via File › Execute › NX Open (Ctrl+U) |
| Export scope | Display Part (part or assembly). The SW plugin uses the active document |
| Hierarchy | Root = part name; each component occurrence is a glTF node, preserving parent–child relationships, including nested assemblies |
| Transform | Local matrix = `inverse(parent world) × world` (same as SW). The `GetPosition` matrix convention is checked against real geometry at run time |
| Units | Converts part units (mm, inch, m, µm) to meters. Supports child parts whose units differ from the assembly |
| Instances sharing a part | Each occurrence has its own node and transform; each body is tessellated only once (cached per prototype) |
| Multi-body | All visible solid bodies; each body is split into meshes by material (`<node>_Material_<i>`, same as SW) |
| Color and transparency | NX display color (including assembly color overrides) and translucency → `baseColorFactor` (sRGB → linear), `alphaMode BLEND` |
| Body filtering | Skips sheet bodies, blanked bodies, bodies on hidden layers, bodies outside the reference set, and suppressed components |
| Data safety | Does not modify or save the model. The temporary facet model is always deleted. The GLB is written atomically (no corrupt file is left behind) |
| Notifications | Success/error dialogs like SW; log at `%TEMP%\SynodeNX\synode_nx_export.log` |
| Batch | `run_journal.exe synode_export_glb.py -args <model.prt> <out.glb>` (requires a valid NX license) |

## 2. Requirements

- Windows 10/11 x64, Siemens NX with **NX Open for Python** (`NXBIN\python\NXOpen.pyd`). The plugin was written and its API usage checked against **NX 2606** (Designcenter NX 2606.3002). Other NX versions that use Python 3 may work but are **not verified**.
- Licenses:
  - Running a Python journal (not compiled, not signed) does **not** require an NX Open Author license.
  - `UF_FACET_facet_solid` requires the **ASSEMBLIES MODULE** license (according to the NXOpen_UF docstring in NX 2606).
  - Batch runs (`run_journal.exe`) require a valid license server / batch license.
- No additional Python libraries are needed: the GLB writer uses only the Python standard library.

## 3. Technology

- Python 3.12 (the interpreter bundled with NX), NX Open (`NXOpen`, `NXOpen.UF`).
- Why Python instead of C#/C++: no compilation, no DLL signing or Author license, and it runs directly from MenuScript or Ctrl+U. All required APIs (Session, Part, ComponentAssembly, Component, Body/Face, UF_FACET, UF_OBJ, UF_UI file box, NXMessageBox) are available in Python.
- GLB writer: rewritten in pure Python to reproduce the output of SharpGLTF 1.0.6 used by the SW plugin. Its binary layout and JSON structure were designed to match the SOLIDWORKS writer.

## 4. Source layout

```
siesmen-export-plugin/
├─ install.ps1 / uninstall.ps1          # install and uninstall (Windows, per user)
├─ solid_plugin/                        # separate Solid Edge exporter (see its own README)
└─ nx_plugin/                           # NX user directory (UGII_USER_DIR)
   ├─ startup/
   │  ├─ synode_glb_exporter.men        # MenuScript: menu "Synode > Export to GLB"
   │  └─ synode_glb_exporter.rtb        # ribbon tab "Synode"
   └─ application/
      ├─ synode_export_glb.py           # journal entry point (ACTIONS of the menu/ribbon)
      ├─ synode_nx_exporter.json.example
      └─ synode_nx_exporter/
         ├─ config.py                   # ExportOptions + JSON file
         ├─ errors.py, logger.py
         ├─ scene.py                    # ExtractedScene/Node/Mesh/Material (same as SW)
         ├─ matrix.py                   # 4x4 matrices (System.Numerics layout)
         ├─ materials.py                # sRGB→linear, alpha, material comparison
         ├─ mesh_builder.py             # vertex merging, normals, mesh validation
         ├─ coordinate_converter.py     # port of CoordinateConverter (float32 normalization)
         ├─ gltf_writer.py              # port of GltfSceneWriter (SharpGLTF layout)
         ├─ output_path.py              # path validation, write permission, atomic write
         ├─ exporter.py                 # scene → converter → GLB
         └─ nx/                         # NX Open-dependent code
            ├─ __init__.py              # NxContext (Session, UFSession, UI)
            ├─ command.py               # Export command (port of ExportGlbCommand)
            ├─ extractor.py             # assembly traversal, transforms, bodies (port of SolidWorksGeometryExtractor)
            ├─ tessellator.py           # UF_FACET
            ├─ appearance.py            # color/translucency
            ├─ units.py                 # part units
            └─ ui.py                    # file box, message box, batch mode
```

## 5. Installation

```powershell
cd C:\plugin_export_3d\siesmen-export-plugin
powershell -ExecutionPolicy Bypass -File .\install.ps1 -WhatIf   # check only, changes nothing
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

The script **checks first** and only then makes changes:

1. Locates NX (`-NxRoot`, `UGII_BASE_DIR` or `Program Files\Siemens\*\NXBIN\ugraf.exe`) and prints the version from `ugraf.exe`.
2. Checks `NXBIN\python\NXOpen*.pyd` and the plugin files.
3. Chooses the destination folder:
   - If `UGII_USER_DIR` is already set: adds the files there without touching other files.
   - If it is not set: copies to `%LOCALAPPDATA%\SynodeNX\nx_plugin` and sets the **User** environment variable `UGII_USER_DIR`.
   - `-Target <folder>`: installs into the given folder without changing environment variables.
4. Writes `synode_install_manifest.json`. If the plugin is already installed, the script stops unless `-Force` is used.

After installing, **restart NX**. To uninstall: `powershell -ExecutionPolicy Bypass -File .\uninstall.ps1` (removes only what is listed in the manifest; removes `UGII_USER_DIR` only if the install script created it).

Manual installation: copy `nx_plugin\startup\*` and `nx_plugin\application\*` to `<UGII_USER_DIR>\startup` and `<UGII_USER_DIR>\application`.

## 6. Usage

1. Open a part or assembly in NX (it becomes the Display Part).
2. Choose **Menu › Synode › Export to GLB**, the ribbon tab **Synode › Export to GLB**, or **File › Execute › NX Open…** (Ctrl+U) and select `application\synode_export_glb.py`.
3. Choose the `.glb` path. The default is the part's folder with the name `nx-export.glb`. If the file already exists, the plugin asks before overwriting it.
4. After the export, the plugin shows *"GLB exported successfully: <path>"* (with the number of warnings, if any), or an error dialog.

Batch, without UI (requires a batch license):

```bat
"%UGII_BASE_DIR%\NXBIN\run_journal.exe" "%UGII_USER_DIR%\application\synode_export_glb.py" -args "D:\models\asm.prt" "D:\out\asm.glb"
```

Options: copy `synode_nx_exporter.json.example` to `synode_nx_exporter.json` next to the journal, or point the `SYNODE_NX_EXPORTER_CONFIG` variable to a JSON file. Main keys: `chord_tolerance_mm`, `angular_tolerance_deg`, `component_name_style` (`path`/`leaf`), `include_root_part_bodies`, `share_instance_meshes`, `skip_hidden_layer_bodies`, `unit_scale`, `log_directory`. Invalid keys are reported as errors.

### Menu/ribbon integration

- `startup\synode_glb_exporter.men` (VERSION 120, `EDIT UG_GATEWAY_MAIN_MENUBAR`, `BEFORE UG_HELP`) creates the **Synode** menu with the `SYNODE_EXPORT_GLB` button and `ACTIONS synode_export_glb.py` (NX looks for the file in the `application` folder of `UGII_USER_DIR`).
- `startup\synode_glb_exporter.rtb` creates the **Synode** tab with the *Synode GLB Exporter* group, reusing the `SYNODE_EXPORT_GLB` button. If the tab does not appear automatically, right-click the ribbon and enable the **Synode** tab. The ribbon button and the menu run the same export command.
- The syntax was compared with the NX 2606 menu/ribbon files (`UGII\menus\*.men`, `*.rtb`). No stock NX file uses a `.py` file as ACTIONS; this relies on the Siemens MenuScript documentation and **must be verified in NX**. If the menu does not appear, use Ctrl+U (always works) and check the NX syslog (Help › Log File) for errors while loading the menu file.

## 7. Output format (same as the SOLIDWORKS plugin)

- GLB 2.0 with a single scene. Root node = part name (`Part.Leaf`); child nodes are components. A node gets a `matrix` when its transform is not the identity.
- Node names: `"<part name>-<n>"` (n numbered per parent). The `path` style joins names along the parents (`"sub_a-1/bolt-2"`), like the SW `Component2.Name2` string.
- Each `ExtractedMesh` is a glTF mesh with one triangle primitive. The first mesh is attached to the node; additional meshes are attached to unnamed child nodes (same as SharpGLTF).
- `POSITION`/`NORMAL` float32 interleaved (stride 24), in meters, **no axis conversion** (same as SW). Indices are uint16, or uint32 for the whole file when any mesh exceeds 65535 vertices.
- Material: `pbrMetallicRoughness` (linear `baseColorFactor`, metallic 0, roughness 0), `doubleSided: true`, `alphaMode: BLEND` when transparent. The name is `<body>_Material` or `<body>_Face_Material`.
- `asset.generator` = `"Synode NX GLB Exporter 1.0.0"` (SW: the SharpGLTF string).

## 8. Example

Assembly `main_assembly` containing `sub_a(bolt ×2)`, `sub_b(plate)`, `bracket`:

```
main_assembly
├─ sub_a-1            (matrix: rotZ 90°, t=0.2 m)
│  ├─ sub_a-1/bolt-1  mesh sub_a-1/bolt-1_Material_0
│  └─ sub_a-1/bolt-2  mesh ... (rotX 30°)
├─ sub_b-1
│  └─ sub_b-1/plate-1 mesh _Material_0 + unnamed child node (mesh _Material_1, BLEND)
└─ bracket-1          2 bodies → 2 meshes
```

## 9. Testing in NX

1. Run `install.ps1`, start NX, and check that the **Synode** menu and **Synode** tab are present.
2. Open a single part and export it. Open the file in a glTF viewer (for example https://gltf-viewer.donmccurdy.com or Windows 3D Viewer) and compare the dimensions (in meters) with NX (Analysis › Measure).
3. Open an assembly with nested sub-assemblies, repeated components and rotated components. Check the node tree and positions. In the log, the line *"Component orientation convention confirmed: rows/columns"* shows the result of the `GetPosition` check.
4. Check assembly color overrides, transparent bodies, hidden layers, blanked bodies, and parts in inches.
5. Try an invalid path and a read-only folder (an error dialog must appear and no file must be created).
6. Close all parts and run the command: *"No active NX part…"* must be shown.
7. Send `%TEMP%\SynodeNX\synode_nx_export.log` if an error occurs.

## 10. Common errors

| Message | Cause / solution |
|---|---|
| `No active NX part. Open a part or an assembly first.` | No part is open (Display Part is empty) |
| `Only parts and assemblies are supported.` | The Display Part is not an `NXOpen.Part` (e.g. drawing-only or another type) |
| `The output folder does not exist` / `No permission to write…` / `…read-only` | Choose another folder; the file may be open in another program |
| `The output file must have the .glb extension` | Change the file extension to `.glb` |
| `The model contains no exportable solid geometry.` | No visible solid body (sheet, blanked, hidden layer, outside the reference set) |
| Body skipped with a `could not be tessellated` warning | UF_FACET failed or the ASSEMBLIES license is missing; see the log |
| Menu does not appear | NX has not read `UGII_USER_DIR` (restart NX), or `.py` ACTIONS are not supported by this NX version → use Ctrl+U |
| `License Error, SPLM_LICENSE_SERVER…` (batch) | Batch mode requires a license server; run in an interactive NX session |

## 11. Differences from the SOLIDWORKS plugin

| Item | SOLIDWORKS | NX plugin |
|---|---|---|
| Integration | C# COM add-in, CommandGroup | Python journal + MenuScript/ribbon |
| Scope | Active document | Display Part |
| Node name | `Component2.Name2` | `<part leaf>-<n>` per parent, joined as a path (same string format) |
| Material | DSPBR render material (face → feature → body) | NX display color + translucency (face → body). NX Open provides no DSPBR equivalent; metallic/roughness = 0 |
| Bodies in the assembly part | No equivalent concept | Exported on the root node (`include_root_part_bodies`) |
| Zero normal | Throws an error | Replaced by the facet normal and logged |
| File selection dialog | SaveFileDialog | UF_UI file box (`CreateFilebox`) |
| `asset.generator` | SharpGLTF | Synode NX GLB Exporter 1.0.0 |
| Face translucency | – | A face without its own translucency uses the body's value (assumption, not yet verified in NX) |

## 12. API and license limitations

- NX has a built-in `ExtendedRealityFileCreator` (glTF) but it is **not used**, because its output structure (names, nodes, materials) differs from the SW plugin contract.
- UF_FACET requires the ASSEMBLIES license. If tessellating the prototype fails, the plugin tessellates the occurrence and transforms it back into the component coordinate system.
- The `Component.GetPosition()` matrix convention (whether rows or columns are the axes) is verified with geometry at run time, because it cannot be verified offline.
- A part may appear "modified" because of undo marks or the temporary facet model. The plugin does not save the part.
- Student Edition and batch mode: require an appropriate license. The plugin has not yet been run in a real NX session on the development machine.
