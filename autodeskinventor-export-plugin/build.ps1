<#
.SYNOPSIS
  Build Synode Inventor Exporter (Autodesk Inventor 2027 add-in), Release x64.

.DESCRIPTION
  1. Checks the .NET SDK and chooses the target framework
     (net10.0-windows if SDK 10 is installed - matches Inventor 2027's .NET 10 host;
      otherwise net8.0-windows, which the .NET 10 runtime can load).
  2. Detects Inventor 2027 and the Inventor API assemblies (Autodesk.Inventor.Interop.dll, stdole.dll).
  3. Restores and builds src\SynodeInventor.AddIn (Release, x64).
  4. Verifies the DLL (exists, PE32+ / AMD64, assembly name) and copies it with the .addin manifest to dist\.
  This script does not modify the registry and does not install anything.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\build.ps1
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\build.ps1 -TargetFramework net10.0-windows
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',
    [ValidateSet('', 'net8.0-windows', 'net10.0-windows')]
    [string]$TargetFramework = '',
    [string]$InventorBinDir = '',
    [string]$OutputDir = '',
    # Optional: copy the sources to this folder and build there (keeps the source tree free of bin/obj).
    [string]$WorkDir = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

# $PSScriptRoot is not reliable inside param defaults on Windows PowerShell 5.1 -> compute here.
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $OutputDir) { $OutputDir = Join-Path $here 'dist' }
. (Join-Path $here 'scripts\Find-Inventor.ps1')

function Write-Step([string]$text) { Write-Host ''; Write-Host "==> $text" -ForegroundColor Cyan }
function Write-Ok([string]$text) { Write-Host "    [OK] $text" -ForegroundColor Green }
function Write-Warn2([string]$text) { Write-Host "    [WARN] $text" -ForegroundColor Yellow }
function Fail([string]$text) { Write-Host "    [FAIL] $text" -ForegroundColor Red; exit 1 }

function Test-PeAmd64([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 0x40 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) { return 'not a PE file' }
    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
    if ($bytes[$peOffset] -ne 0x50 -or $bytes[$peOffset + 1] -ne 0x45) { return 'missing PE signature' }
    $machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
    $magic = [BitConverter]::ToUInt16($bytes, $peOffset + 24)
    if ($machine -ne 0x8664) { return ('machine 0x{0:X4} (expected 0x8664 AMD64)' -f $machine) }
    if ($magic -ne 0x20B) { return ('optional header 0x{0:X3} (expected PE32+)' -f $magic) }
    return $null
}

Write-Host 'Synode Inventor Exporter - build' -ForegroundColor White

# ---------------------------------------------------------------- 1. .NET SDK
Write-Step 'Checking .NET SDK'
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { Fail '.NET SDK not found (dotnet.exe). Install the .NET 10 SDK (x64) from https://dotnet.microsoft.com/download' }
$sdkLines = @(& dotnet --list-sdks 2>$null)
if ($LASTEXITCODE -ne 0 -or $sdkLines.Count -eq 0) { Fail 'No .NET SDK is installed. Install the .NET 10 SDK (x64).' }
$sdkMajors = @($sdkLines | ForEach-Object { if ($_ -match '^(\d+)\.') { [int]$Matches[1] } })
$maxSdk = ($sdkMajors | Measure-Object -Maximum).Maximum
Write-Ok ("SDKs: " + (($sdkLines | ForEach-Object { ($_ -split ' ')[0] }) -join ', '))

if (-not $TargetFramework) {
    if ($maxSdk -ge 10) { $TargetFramework = 'net10.0-windows' }
    elseif ($maxSdk -ge 8) {
        $TargetFramework = 'net8.0-windows'
        Write-Warn2 'The .NET 10 SDK is not installed. Inventor 2027 hosts add-ins on .NET 10;'
        Write-Warn2 'building net8.0-windows instead (forward-compatible). Install the .NET 10 SDK for a net10.0 build.'
    }
    else { Fail "SDK $maxSdk is too old. Install the .NET 10 SDK (or at least SDK 8)." }
}
elseif ($TargetFramework -eq 'net10.0-windows' -and $maxSdk -lt 10) {
    Fail 'net10.0-windows requested but the .NET 10 SDK is not installed.'
}
Write-Ok "Target framework: $TargetFramework (x64)"

# ---------------------------------------------------------------- 2. Inventor
Write-Step 'Detecting Autodesk Inventor 2027 and its API'
$inv = Get-SynodeInventorInfo -InventorBinDir $InventorBinDir
if (-not $inv.Found) { $inv.Problems | ForEach-Object { Write-Warn2 $_ }; Fail 'Inventor 2027 not found. Use -InventorBinDir "<Inventor 2027>\Bin".' }
Write-Ok "Inventor.exe $($inv.FileVersion) ($($inv.Source))"
if (-not $inv.InteropPath) { Fail 'Autodesk.Inventor.Interop.dll not found.' }
Write-Ok "Inventor API: $($inv.InteropPath) ($($inv.InteropVersion))"
if (-not $inv.StdolePath) { Fail 'stdole.dll not found in the Inventor Bin folder.' }
Write-Ok "stdole: $($inv.StdolePath)"

# ---------------------------------------------------------------- 3. Build
$srcRoot = $here
if ($WorkDir) {
    Write-Step "Copying sources to work folder $WorkDir"
    New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
    & robocopy $here $WorkDir /MIR /XD bin obj dist .vs .git /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { Fail "robocopy failed with exit code $LASTEXITCODE" }
    $srcRoot = $WorkDir
    Write-Ok 'Sources copied'
}

$commonProps = @(
    "-p:SynodeTargetFramework=$TargetFramework",
    "-p:InventorBinDir=$($inv.BinDir)",
    '-p:UseSharedCompilation=false',
    '-nologo'
)

Write-Step "Building add-in ($Configuration)"
$addinProj = Join-Path $srcRoot 'src\SynodeInventor.AddIn\SynodeInventor.AddIn.csproj'
& dotnet build $addinProj -c $Configuration @commonProps
if ($LASTEXITCODE -ne 0) { Fail "dotnet build failed (exit code $LASTEXITCODE)." }

$binDir = Join-Path $srcRoot "src\SynodeInventor.AddIn\bin\$Configuration\$TargetFramework"
$dll = Join-Path $binDir 'SynodeInventor.AddIn.dll'
if (-not (Test-Path -LiteralPath $dll)) { Fail "Expected output not found: $dll" }

# ---------------------------------------------------------------- 4. Verify + dist
Write-Step 'Verifying output'
$peProblem = Test-PeAmd64 $dll
if ($peProblem) { Fail "SynodeInventor.AddIn.dll: $peProblem" }
Write-Ok 'PE32+ / AMD64'
$asmName = [System.Reflection.AssemblyName]::GetAssemblyName($dll)
if ($asmName.Name -ne 'SynodeInventor.AddIn') { Fail "Unexpected assembly name '$($asmName.Name)'." }
Write-Ok "Assembly: $($asmName.FullName)"
$unexpected = @(Get-ChildItem -LiteralPath $binDir -Filter '*.dll' | Where-Object { $_.Name -ne 'SynodeInventor.AddIn.dll' })
if ($unexpected.Count -gt 0) { Write-Warn2 ('Extra DLLs in output (not deployed): ' + (($unexpected | ForEach-Object Name) -join ', ')) }

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Copy-Item -LiteralPath $dll -Destination $OutputDir -Force
$pdb = Join-Path $binDir 'SynodeInventor.AddIn.pdb'
if (Test-Path -LiteralPath $pdb) { Copy-Item -LiteralPath $pdb -Destination $OutputDir -Force }
Copy-Item -LiteralPath (Join-Path $srcRoot 'src\SynodeInventor.AddIn\Synode.InventorExporter.addin.template') `
          -Destination (Join-Path $OutputDir 'Synode.InventorExporter.addin') -Force
Set-Content -LiteralPath (Join-Path $OutputDir 'build-info.txt') -Encoding ASCII -Value @(
    "Product=Synode Inventor Exporter",
    "AssemblyVersion=$($asmName.Version)",
    "TargetFramework=$TargetFramework",
    "Configuration=$Configuration",
    "InventorVersion=$($inv.FileVersion)",
    "InteropVersion=$($inv.InteropVersion)",
    ("BuiltUtc=" + [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ'))
)
$distDll = Join-Path $OutputDir 'SynodeInventor.AddIn.dll'
Write-Ok "DLL: $distDll"
Write-Ok "Manifest: $(Join-Path $OutputDir 'Synode.InventorExporter.addin')"

Write-Host ''
Write-Host 'BUILD SUCCEEDED' -ForegroundColor Green
Write-Host "  Add-in DLL : $distDll"
Write-Host "  Next step  : powershell -ExecutionPolicy Bypass -File .\install.ps1"
exit 0
