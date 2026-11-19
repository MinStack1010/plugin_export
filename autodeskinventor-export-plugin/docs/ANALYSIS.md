# Analysis of the SolidWorks plugin and its mapping to Inventor 2027

Source reviewed (read only, not modified): `C:\plugin_export_3d\solidworks-export-plugin`.

## 1. SolidWorks plugin (based on the actual source code)

| Item | Actual implementation in the source |
|---|---|
| Technology | C# .NET Framework 4.8, COM add-in, SharpGLTF 1.0.6 |
| Entry point | `SwAddin.cs` implements `ISwAddin`, GUID `d2717311-4a25-4677-877a-fa8d5ec18c71`, ProgId `PluginSolidWorks.SwAddin` |
| Registration | RegAsm → `[ComRegisterFunction]` writes `HKLM\SOFTWARE\SolidWorks\AddIns\{guid}` (Title "Synode Plugin") and `HKCU ...\AddInsStartup` |
| User interface | CommandManager group 1001 "Synode GLB Exporter", command "Export to GLB" (menu + toolbar), enabled when there is an ActiveDoc |
| Export flow | `Commands\ExportGlbCommand.cs`: SaveFileDialog (default `solidworks-export.glb`) → `SolidWorksGeometryExtractor` (traverses part/assembly, tessellation, normals, per-face/per-body materials, component transforms) → `CoordinateConverter` (Z-up→Y-up, UnitScale = 1) → `GltfSceneWriter` (SharpGLTF) → `SendMsgToUser2` reports success/failure |
| Upload | `GlbUploader` (HTTP multipart, api_key `API_KEY_DEV`, empty URL) — **dead code**: `AskOutputPath(true)` is always called, so it never uploads |
| Log | `Desktop\SynodeSolidWorks.log` |
| Dependencies | AssemblyResolve from the add-in folder |

Only output format: **GLB** (glTF 2.0 binary).

## 2. Mapping SolidWorks → Inventor 2027

| SolidWorks | Inventor 2027 | Approach |
|---|---|---|
| `ISwAddin.ConnectToSW / DisconnectFromSW` | `ApplicationAddInServer.Activate / Deactivate` | Rewritten (`StandardAddInServer.cs`) |
| RegAsm + registry `SolidWorks\AddIns` | `.addin` manifest in `%APPDATA%\Autodesk\Inventor 2027\Addins` | Rewritten; **no RegAsm/registry** |
| CommandManager / CommandGroup | `ControlDefinitions.AddButtonDefinition` + Ribbon tab/panel (`RibbonUi.cs`) | Rewritten |
| Toolbar icon (bitmap file) | `stdole.IPictureDisp` drawn in code (`IconRenderer` + `IconFactory`) | Rewritten |
| `ActiveDoc`, `swDocPART/ASSEMBLY` | `Application.ActiveDocument`, `kPartDocumentObject/kAssemblyDocumentObject` | Rewritten (`InventorDocumentService`) |
| SaveFileDialog (WinForms) | `Application.CreateFileDialog` (`ShowSave`) | Rewritten, same behavior (.glb extension, suggested name) |
| `SendMsgToUser2` | `MessageBoxW` owned by the Inventor main window | Rewritten, same message content |
| GeometryExtractor + CoordinateConverter + SharpGLTF writer | **Native glTF translator** `{856E3C5D-…}` `SaveCopyAs` | Replaced: Inventor tessellates, converts axes/units, and keeps the hierarchy and materials |
| (none) | `GlbValidator` checks every file before it is written to the destination | New |
| Logger to the Desktop | `FileLogger` to `%LOCALAPPDATA%\Synode\InventorExporter\logs` (injected, not static) | Rewritten |
| GlbUploader (dead code) | — | Not ported (no real URL/API, no hardcoded token) |

**Reused (concept/behavior):** command flow Export → save dialog → export → message; the button only works for Part/Assembly; error handling with clear messages; GLB output.

**Completely rewritten:** add-in lifecycle, registration, UI, document access, export (native translator), logging, installation. No SolidWorks API is used.

## 3. Why the native translator

- The manifest `Bin\Addins\Autodesk.TranslatorglTFExport.Inventor.addin` declares `Type="Translator"`, `SupportsSaveCopyAs=1`, `SupportsSaveCopyAsFrom=.ipt;.iam`, `FileExtensions=.glb;*.gltf`.
- Geometry, material, transform and unit quality is guaranteed by Autodesk itself.
- The add-in only uses APIs verified by reflection on `Autodesk.Inventor.Interop.dll` 31.20: `ApplicationAddIns.ItemById`, `TranslatorAddIn.HasSaveCopyAsOptions/SaveCopyAs`, `TransientObjects.CreateTranslationContext/CreateNameValueMap/CreateDataMedium`.
- Undocumented translator options are not set; only the default options are logged.
- A custom tessellation pipeline would only be built if the native translator were verified not to work.

## 4. Target framework

Inventor 2027 loads managed add-ins on .NET 10 (`Bin\ClrAddinLoader.runtimeconfig.json`: `net10.0`). The build machine at the time of the analysis only had SDK 9.0.305, so it built `net8.0-windows` (forward compatible with runtime 10). Once the .NET 10 SDK is installed, `build.ps1` switches to `net10.0-windows` automatically.
