# Synode GLB Exporter for Autodesk Fusion

Python add-in for Autodesk Fusion (Fusion 360). The add-in exports the active design to a **GLB (glTF 2.0 binary)** file and is the equivalent of the SOLIDWORKS plugin "Synode Plugin / Synode GLB Exporter" (`solidworks-export-plugin`).

> [!IMPORTANT]
> **The add-in has not yet been loaded and run inside a real Fusion installation.** See sections 8 and 10 for what must be verified manually.

---

## 1. Purpose and features

Same as the original SOLIDWORKS plugin:

| Feature | Description |
|---|---|
| **Export to GLB** command button | Located in the DESIGN workspace -> UTILITIES tab -> ADD-INS panel (promoted to the toolbar). |
| Destination file selection | Save dialog, `*.glb` filter, default name `fusion-export.glb`. Cancelling the dialog does nothing (same as SW). |
| Exports the whole model | Part (root component with bodies only) or assembly (nested occurrences). |
| Node structure | 1 root node (name = document name) -> 1 node per occurrence (name = `Occurrence.name`, e.g. `Bolt:1`), preserving the hierarchy. |
| Transform | Each node stores its **local transform** relative to its parent (as in SW: `world * inverse(parentWorld)`). |
| Geometry | Visible B-Rep solid bodies only; tessellation with vertex normals; degenerate triangles are dropped; triangle winding is corrected to match the normals. |
| Materials | Triangles are grouped by equivalent appearance (tolerance 1e-4, as in SW); PBR metallic-roughness, double-sided, sRGB -> linear, transparent -> `BLEND`; no appearance -> default grey material `Material`. |
| Mesh / material names | Mesh `{nodeName}_Material_{i}`; material `{bodyName}_Material` or `{bodyName}_Face_Material` (appearance assigned at face level). |
| Units | Metres (the Fusion API uses cm -> multiplied by `unit_scale = 0.01`), same as the SW output (the SW API uses metres). |
| Upload (optional) | Multipart POST (`file` = GLB `model/gltf-binary`, `api_key`) - same as `GlbUploader.cs`. Only shown when `upload_url` is configured (in the SW plugin the upload branch is disabled because the URL is empty). |
| Messages | `GLB exported successfully:\n<path>` / `GLB uploaded successfully.` / specific error messages; plus statistics and warnings. |

Additions due to platform differences (they do not change the output contract): mesh quality selection, optional Z-up -> Y-up rotation, a progress dialog with a Cancel button, and warnings about skipped bodies.

## 2. Project structure

```
autodeskfusion-export-plugin/
├── README.md
├── install.ps1                      # Installs / uninstalls the add-in on Windows
├── .gitignore
└── SynodeGlbExporter/               # Add-in folder (copy this whole folder into Fusion)
    ├── SynodeGlbExporter.manifest   # Fusion add-in manifest
    ├── SynodeGlbExporter.py         # Entry point: run() / stop()
    ├── config.json                  # Configuration (replaces the hard-coded ExportOptions of SW)
    ├── resources/export_glb/        # Command button icons 16/32 px (+ @2x)
    └── synode_glb/
        ├── core/                    # NO Fusion dependency (pure Python, CAD-independent)
        │   ├── scene.py             # ExtractedScene/Node/Mesh/Material (as in SW)
        │   ├── materials.py         # sRGB -> linear, equivalent-material comparison
        │   ├── matrix.py            # 4x4 matrices, TRS decomposition
        │   ├── coordinate_converter.py  # cm -> m, optional Z-up -> Y-up
        │   ├── gltf_writer.py       # GLB writer (replaces SharpGLTF), atomic file write
        │   ├── uploader.py          # Multipart upload (port of GlbUploader.cs)
        │   ├── config.py            # Reads/validates config.json
        │   ├── output_path.py       # Destination path validation
        │   └── errors.py
        └── fusion/                  # Uses adsk.core / adsk.fusion
            ├── export_command.py    # Command button, dialog, export workflow, messages
            ├── geometry_extractor.py  # Walks occurrences/bodies, tessellation, material grouping
            └── appearance_reader.py # Reads color/roughness/transparency from an Appearance
```

No dependencies other than the Python standard library and the Fusion API.

## 3. Requirements

- Autodesk Fusion (current release, with its embedded Python 3) on Windows 10/11 (the manifest also declares macOS, but it has not been tried).
- A design open in the **DESIGN** workspace (Fusion Design, parametric or direct).

## 4. Installation on Windows

**Option A - script:**

```powershell
cd <path>\autodeskfusion-export-plugin
powershell -ExecutionPolicy Bypass -File .\install.ps1
# uninstall:
powershell -ExecutionPolicy Bypass -File .\install.ps1 -Uninstall
```

The script copies the `SynodeGlbExporter` folder to `%APPDATA%\Autodesk\Autodesk Fusion 360\API\AddIns\SynodeGlbExporter`.

**Option B - manual:** copy the `SynodeGlbExporter` folder into `%APPDATA%\Autodesk\Autodesk Fusion 360\API\AddIns\` (the folder name must match the `.py`/`.manifest` file names).

**Option C - no copy:** in Fusion, *Scripts and Add-Ins* -> *Add-Ins* tab -> **+** button (next to "My Add-Ins") -> select the `SynodeGlbExporter` folder.

## 5. Starting from Scripts and Add-Ins

1. Open Fusion -> **DESIGN** workspace -> **UTILITIES** tab -> **ADD-INS** -> **Scripts and Add-Ins** (shortcut `Shift+S`).
2. **Add-Ins** tab -> select **SynodeGlbExporter** -> **Run**.
3. (Optional) check **Run on Startup** to start it automatically when Fusion opens.
4. The **Export to GLB** button appears in the UTILITIES › ADD-INS panel (and is promoted to the toolbar).

To stop the add-in: select it again in the list -> **Stop** (the command button and handlers are removed in `stop()`).

## 6. Usage

1. Open the design to export.
2. Click **Export to GLB**. The dialog contains:
   - **Design** - the name of the current design (or a warning if there is no design -> the Export button is disabled).
   - **Destination** - `Save to file` (default); `Upload to server` is only shown when `upload_url` is set.
   - **Mesh quality** - Low / Normal / **High** (default, corresponds to SW ImprovedQuality) / Very high.
   - **Design up axis** - `Y-up (no conversion)` or `Z-up -> rotate to glTF Y-up`. The default comes from Fusion's *Default modeling orientation* preference (`up_axis: auto`).
3. Click **Export** -> choose the `.glb` file -> wait for the progress dialog (can be cancelled).
4. Result: a success message with node/mesh/triangle counts, file size and a list of warnings; or an error message. The log is also written to the **TEXT COMMANDS** window with the `[Synode GLB]` prefix.

### config.json

| Key | Default | Meaning |
|---|---|---|
| `unit_scale` | `0.01` | Factor from cm (Fusion) to output units. `0.01` = metres (matches the SW output). |
| `upload_url` | `""` | Upload URL; empty = save to file only (same as the current SW behavior). |
| `api_key` | `"API_KEY_DEV"` | Value of the `api_key` field when uploading (same value as SW). |
| `upload_timeout_seconds` | `120` | Upload timeout. |
| `default_file_name` | `"fusion-export.glb"` | Suggested file name. |
| `mesh_quality` | `"high"` | `low` / `normal` / `high` / `very_high`. |
| `up_axis` | `"auto"` | `auto` / `y` / `z`. |

Invalid configuration -> the dialog shows "Config error" and export is not allowed.

## 7. Output structure

Example: assembly `Gearbox v3` containing `Housing:1` and `Shaft:1` (which contains `Bearing:1` and `Bearing:2` sharing the same component); one Housing body has a face with its own appearance:

```text
scene 0
└── node "Gearbox v3"                       (identity; mesh if the root component has bodies)
    ├── node "Housing:1"   local translation/rotation (m)
    │   ├── node "Housing:1_Material_0"  → mesh "Housing:1_Material_0", material "Body1_Material"
    │   └── node "Housing:1_Material_1"  → mesh "Housing:1_Material_1", material "Body1_Face_Material"
    └── node "Shaft:1"     → mesh "Shaft:1_Material_0"
        ├── node "Bearing:1" → mesh "Bearing:1_Material_0"
        └── node "Bearing:2" → mesh "Bearing:2_Material_0"   (separate mesh, same as SW)
```

glTF details:

- `asset.generator = "Synode Fusion GLB Exporter 1.0.0"`, 1 scene, 1 root node.
- Local transforms are written as `translation`/`rotation`/`scale`; if the matrix contains mirroring/shear, `matrix` is written instead.
- Each mesh has 1 TRIANGLES primitive with `POSITION` (with min/max), `NORMAL` and `indices` (`UNSIGNED_SHORT`, or `UNSIGNED_INT` when there are > 65535 vertices).
- Materials: `pbrMetallicRoughness` (linear `baseColorFactor`, `metallicFactor`, `roughnessFactor`), `doubleSided: true`, `alphaMode: BLEND` if transparent. Materials with identical content are shared.

## 8. Verifying the result

**Manual checks (inside Fusion - not yet performed):**

1. The add-in runs without errors; the **Export to GLB** button appears; Stop removes the button.
2. No design open (or a different workspace) -> the Export button is disabled / the message "No active Fusion design" is shown.
3. Export 1 part, 1 nested assembly and 1 assembly with a component used several times; open the file in https://gltf-viewer.donmccurdy.com/ or Blender: check position, orientation, size (m), node names and colors.
4. Validate with https://github.khronos.org/glTF-Validator/ (no errors).
5. Appearances assigned to a face / body / occurrence; transparent appearance (glass).
6. Z-up design: choose the rotation option -> the model stands upright in the viewer.
7. Cancel in the progress dialog; choose a folder without write permission -> error message.
8. If upload is used: configure `upload_url` and check that the server receives the correct `file` + `api_key`.
9. Compare with the GLB file exported by the SW plugin for the same model (if available) using the downstream system.

## 9. Troubleshooting

| Symptom | Cause / Resolution |
|---|---|
| The add-in does not appear in the list | The folder must be named `SynodeGlbExporter` and contain the `.py` + `.manifest` files with the same name; or use the **+** button to point to the folder. |
| "Synode GLB Exporter failed to start" | See the traceback in the message; usually caused by missing files or an outdated Fusion version. If the `SolidScriptsAddinsPanel` panel does not exist, a clear error is reported. |
| The Export button is greyed out | No Fusion design is open (e.g. in Drawing/CAM without a Design) or `config.json` is invalid ("Config error" line). |
| "The active design contains no visible solid bodies to export." | All bodies are hidden, or are surface or mesh bodies. Show the bodies / convert them to solids and export again. |
| "The output folder does not exist / is not writable" | Choose another folder. |
| The model lies on its side in the viewer | Z-up design: choose `Z-up -> rotate to glTF Y-up`. |
| Size off by a factor of 100 | Check `unit_scale` (0.01 = metres). |
| Upload: "HTTP 4xx/5xx ..." | The server response body is shown verbatim (same as SW). |
| Unexpected error | Traceback details are in the TEXT COMMANDS window (`[Synode GLB]`). |

## 10. Differences and limitations compared to the SOLIDWORKS plugin

| Area | SOLIDWORKS | Fusion | Notes |
|---|---|---|---|
| Platform | C# .NET 4.8 COM add-in, `regasm` | Python add-in, folder copy | Does not use the SolidWorks API/DLLs. |
| GLB writing | SharpGLTF `SceneBuilder` | Custom writer (stdlib) | Same content; the **node layout when one component has several meshes** (SW: `AddRigidMesh` called several times on the same node) has not been compared against an actual SharpGLTF file - here child nodes named after the meshes are used. Compare if downstream depends on the node count. |
| Units | API in metres, `UnitScale=1` | API in cm, `unit_scale=0.01` | Both output metres. |
| Axes | SW is Y-up, no rotation | Fusion can be Y-up or Z-up | Optional rotation; the default is guessed from the application-wide preference, not per file. |
| Suppressed components | Skipped | The Fusion API has no suppression for occurrences | Not applicable. Hidden occurrences are still exported (hidden bodies - light bulb off - are skipped with a warning). |
| Material source | Face -> Feature -> Body/Part (DSPBR) | Face/override -> Occurrence -> Body -> Material (Appearance) | Fusion has no feature-level appearance, so there are no `_Feature_Material` names. Metallic = 1 for Prism metal appearances, 0 for other types (approximation). Textures are not exported (SW does not either) - a warning is shown. |
| Transparency | DSPBR transparency | Color alpha or `generic_transparency` | Prism glass appearances may have no alpha -> may be exported as opaque. Needs real-world verification. |
| Zero normals | Throws an error | Recomputed from triangles with a warning; error only if they cannot be computed | Avoids failing the whole export. |
| Mesh/surface bodies | Not exported (solids only) | Skipped + warning | Equivalent. |
| Empty model | Writes an empty GLB | Reports an error, no file written | Avoids meaningless files. |
| Mesh quality | Fixed ImprovedQuality | Selectable, default High | Triangle counts will differ from SW. |
| Upload | Code present but unused (empty URL) | Same contract, enabled via `upload_url` | Not yet tried against a real server. |
| Logger | Desktop log (disabled) | TEXT COMMANDS | No log file is written. |
| Verification | - | - | **Not yet run inside real Fusion.** |
