# Synode SOLIDWORKS GLB Exporter

SOLIDWORKS COM add-in (`PluginSynode.dll`, .NET Framework 4.8) that exports the active
part or assembly (component hierarchy, transforms, solid bodies and DSPBR appearances)
to a binary glTF 2.0 file (`.glb`) using SharpGLTF.

## Project layout

```text
solidworks-export-plugin/
├── SwAddin.cs                      COM add-in entry point (ISwAddin) and registration
├── Commands/ExportGlbCommand.cs    "Export to GLB" command
├── Export/
│   ├── SolidWorksGeometryExtractor.cs  Tessellation, transforms and materials
│   ├── CoordinateConverter.cs          Unit scaling and normal normalisation
│   ├── GltfSceneWriter.cs              GLB writer (SharpGLTF)
│   ├── GlbUploader.cs                  Optional multipart upload of the GLB
│   └── Extracted*.cs / ExportOptions.cs  CAD-independent scene model
├── PluginSynode.csproj / PluginSynode.slnx
├── packages.config                 NuGet dependencies (SharpGLTF 1.0.6 and System.* packages)
└── app.config                      Binding redirects
```

## Requirements

- Windows x64 with SOLIDWORKS installed (the interop assemblies are referenced from
  `%ProgramW6432%\SOLIDWORKS Corp\SOLIDWORKS\api\redist`).
- .NET Framework 4.8 (Developer Pack for building).
- Visual Studio 2022 (or MSBuild) with NuGet package restore.

If SOLIDWORKS is installed in a non-default location, pass the interop folder to MSBuild:

```powershell
msbuild PluginSynode.csproj /restore /p:Configuration=Release /p:SolidWorksApiDir="D:\SOLIDWORKS Corp\SOLIDWORKS\api\redist"
```

## Build

1. Open `PluginSynode.slnx` in Visual Studio and restore NuGet packages.
2. Build the project (**Build > Build PluginSynode**). The add-in and its dependencies are
   written to `bin\Debug\` or `bin\Release\`.

## Register the add-in

Run an elevated x64 Developer Command Prompt (or use the 64-bit `RegAsm.exe` directly):

```powershell
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe" "<path>\bin\Release\PluginSynode.dll" /codebase
```

Registration writes `HKLM\SOFTWARE\SolidWorks\AddIns\{d2717311-4a25-4677-877a-fa8d5ec18c71}`
(title "Synode Plugin") and enables the add-in at startup for the current user. Restart
SOLIDWORKS; the **Synode GLB Exporter** command group provides **Export to GLB**.

To unregister, run the same command with `/unregister` instead of `/codebase`.

## Configuration

Export options are currently set in `Commands/ExportGlbCommand.cs`:

- `UnitScale` (default `1.0`): the SOLIDWORKS API works in metres, so the output is in metres.
- `UploadUrl` / `ApiKey`: used by `GlbUploader` for the optional upload path.
