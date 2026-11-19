<#
.SYNOPSIS
    Builds Synode Exporter for Solid Edge 2026 (COM add-in, .NET Framework 4.8, x64).
.DESCRIPTION
    1. Checks the environment (Solid Edge 2026, interop assemblies, .NET Framework 4.8, .NET SDK)
       and the encoding/syntax of the PowerShell scripts.
    2. Generates the ribbon icon resources (RT_BITMAP 101/102).
    3. Builds the add-in (from a copy of the sources in -WorkDir) and copies the DLL to -OutputDir.
    4. Verifies that the DLL is x64 / CLR v4.
    This script does NOT write to the registry. Use install.ps1 to register the add-in.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$SolidEdgeDir = 'C:\Program Files\Siemens\Solid Edge 2026',
    [string]$OutputDir,
    [string]$WorkDir
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 does not guarantee $PSScriptRoot in param default values -> assign it here.
$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $OutputDir) { $OutputDir = Join-Path $here 'dist' }
if (-not $WorkDir) { $WorkDir = Join-Path $env:TEMP 'SynodeSolidEdgeBuild' }
$scripts = Join-Path $here 'scripts'

Write-Host '== 1. Environment check ==' -ForegroundColor Cyan
$envInfo = & (Join-Path $scripts 'Test-Environment.ps1') -SolidEdgeDir $SolidEdgeDir -RequireSdk -Strict
Write-Host '== 1b. PowerShell script encoding (UTF-8 BOM) and syntax check ==' -ForegroundColor Cyan
& (Join-Path $scripts 'Test-ScriptSyntax.ps1') -Root $here

if (-not (Test-Path $WorkDir)) { New-Item -ItemType Directory -Path $WorkDir | Out-Null }
$WorkDir = (Resolve-Path $WorkDir).Path
if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir | Out-Null }
$OutputDir = (Resolve-Path $OutputDir).Path

Write-Host '== 2. Generate ribbon icons ==' -ForegroundColor Cyan
$res = Join-Path $WorkDir 'SynodeExporter.Icons.res'
& (Join-Path $scripts 'New-IconResource.ps1') -OutputPath $res

Write-Host '== 3. Build add-in ==' -ForegroundColor Cyan
$srcWork = Join-Path $WorkDir 'addinsrc'
if (Test-Path $srcWork) { Remove-Item $srcWork -Recurse -Force }
Copy-Item (Join-Path $here 'src') $srcWork -Recurse
Get-ChildItem $srcWork -Recurse -Directory -Include bin, obj | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
$buildOut = Join-Path $WorkDir 'addinbin'
if (Test-Path $buildOut) { Remove-Item $buildOut -Recurse -Force }

$proj = Join-Path $srcWork 'SynodeExporter.SolidEdge.csproj'
$out = & dotnet build $proj -c $Configuration -nologo `
    "-p:SolidEdgeProgramDir=$($envInfo.ProgramDir)" `
    "-p:Win32ResourceFile=$res" `
    '-p:UseSharedCompilation=false' `
    -o $buildOut 2>&1
$code = $LASTEXITCODE
$out | Where-Object { $_ -match '(error|warning) [A-Z]+[0-9]+' } | Select-Object -Unique | ForEach-Object { Write-Host "  $_" }
if ($code -ne 0) {
    $out | Select-Object -Last 20 | ForEach-Object { Write-Host "  $_" }
    throw "Add-in build failed (exit $code)."
}

$dllName = 'SynodeExporter.SolidEdge.dll'
foreach ($f in @($dllName, 'SynodeExporter.SolidEdge.pdb')) {
    $p = Join-Path $buildOut $f
    if (Test-Path $p) { Copy-Item $p $OutputDir -Force }
}
$dll = Join-Path $OutputDir $dllName
# Do not ship the Solid Edge interop assemblies: Edge.exe loads the official ones from its Program folder.
Get-ChildItem $OutputDir -Filter 'interop.SolidEdge*.dll' -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host '== 4. Verify DLL ==' -ForegroundColor Cyan
$an = [Reflection.AssemblyName]::GetAssemblyName($dll)
if ($an.ProcessorArchitecture -ne [Reflection.ProcessorArchitecture]::Amd64) {
    throw "The DLL is not x64 (ProcessorArchitecture = $($an.ProcessorArchitecture))."
}
Write-Host "  [OK] $($an.FullName)" -ForegroundColor Green
Write-Host "  [OK] Architecture: $($an.ProcessorArchitecture) | Size: $((Get-Item $dll).Length) bytes" -ForegroundColor Green

Write-Host ''
Write-Host "BUILD SUCCEEDED: $dll" -ForegroundColor Green
Write-Host 'Next step: run install.ps1 (64-bit PowerShell, as Administrator) to register the add-in.'
