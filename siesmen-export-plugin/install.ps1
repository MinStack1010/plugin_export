<#
.SYNOPSIS
    Installs the Synode NX GLB Exporter (NX Open Python journal + menu/ribbon files).

.DESCRIPTION
    1. Checks the environment BEFORE changing anything:
       - Windows PowerShell 5.1+ / PowerShell 7
       - a Siemens NX installation (UGII_BASE_DIR, -NxRoot, or auto-detection
         under "Program Files\Siemens") with NXBIN\ugraf.exe
       - NX Open for Python (NXBIN\python\NXOpen.pyd or NXOpen*.pyd)
       - plugin source files next to this script
    2. Copies nx_plugin\startup and nx_plugin\application into the NX user
       directory:
       - the existing UGII_USER_DIR if it is already set (files are added,
         nothing else is touched), otherwise
       - %LOCALAPPDATA%\SynodeNX\nx_plugin and the USER environment variable
         UGII_USER_DIR is set to it (only if it was not set before).
    3. Writes an install manifest (synode_install_manifest.json) used by
       uninstall.ps1 to remove exactly what was installed.

    Nothing is installed system-wide, no NX file is modified, no software is
    downloaded.  Restart NX after installing.

.PARAMETER NxRoot
    NX installation root (folder containing NXBIN).  Optional.

.PARAMETER Target
    Explicit NX user directory to install into (overrides UGII_USER_DIR logic).
    UGII_USER_DIR is NOT changed when -Target is used; set it yourself.

.PARAMETER Force
    Overwrite existing plugin files without asking / continue on warnings.

.PARAMETER WhatIf
    Only print what would be done.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1 -NxRoot "D:\Siemens\NX2406" -WhatIf
#>
[CmdletBinding()]
param(
    [string]$NxRoot,
    [string]$Target,
    [switch]$Force,
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'
$ManifestName = 'synode_install_manifest.json'
$PluginFiles = @(
    'startup\synode_glb_exporter.men',
    'startup\synode_glb_exporter.rtb',
    'application\synode_export_glb.py'
)
$PackageDir = 'application\synode_nx_exporter'

function Write-Step([string]$text) { Write-Host "[Synode] $text" }
function Write-Ok([string]$text) { Write-Host "  OK   $text" -ForegroundColor Green }
function Write-Warn2([string]$text) { Write-Host "  WARN $text" -ForegroundColor Yellow }
function Write-Fail([string]$text) { Write-Host "  FAIL $text" -ForegroundColor Red }

function Find-NxRoots {
    $roots = New-Object System.Collections.Generic.List[string]
    if ($NxRoot) { $roots.Add($NxRoot) }
    if ($env:UGII_BASE_DIR) { $roots.Add($env:UGII_BASE_DIR) }
    foreach ($pf in @($env:ProgramFiles, ${env:ProgramW6432})) {
        if (-not $pf) { continue }
        $siemens = Join-Path $pf 'Siemens'
        if (Test-Path -LiteralPath $siemens) {
            Get-ChildItem -LiteralPath $siemens -Directory -ErrorAction SilentlyContinue | ForEach-Object {
                if (Test-Path -LiteralPath (Join-Path $_.FullName 'NXBIN\ugraf.exe')) { $roots.Add($_.FullName) }
            }
        }
    }
    $roots | Select-Object -Unique
}

# ---------------------------------------------------------------- checks
Write-Step 'Checking environment (nothing is changed during this step)'
$problems = 0
$source = Join-Path $PSScriptRoot 'nx_plugin'

Write-Ok ("PowerShell {0}" -f $PSVersionTable.PSVersion)

foreach ($rel in $PluginFiles + @("$PackageDir\__init__.py", "$PackageDir\nx\command.py")) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $rel))) { Write-Fail "Missing plugin file: nx_plugin\$rel"; $problems++ }
}
if ($problems -eq 0) { Write-Ok "Plugin sources found: $source" }

$nx = $null
foreach ($root in Find-NxRoots) {
    $ugraf = Join-Path $root 'NXBIN\ugraf.exe'
    if (Test-Path -LiteralPath $ugraf) { $nx = $root; break }
}
if (-not $nx) {
    Write-Fail 'Siemens NX not found (set UGII_BASE_DIR or pass -NxRoot <folder containing NXBIN>).'
    $problems++
} else {
    $ugraf = Join-Path $nx 'NXBIN\ugraf.exe'
    $info = (Get-Item -LiteralPath $ugraf).VersionInfo
    Write-Ok ("Siemens NX: {0}  (ugraf.exe {1} {2})" -f $nx, $info.ProductName, $info.FileVersion)
    $pyd = Get-ChildItem -LiteralPath (Join-Path $nx 'NXBIN\python') -Filter 'NXOpen*.pyd' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($pyd) { Write-Ok "NX Open Python: $($pyd.FullName)" }
    else { Write-Fail 'NX Open for Python (NXBIN\python\NXOpen*.pyd) not found - this NX installation cannot run Python journals.'; $problems++ }
    $runJournal = Join-Path $nx 'NXBIN\run_journal.exe'
    if (Test-Path -LiteralPath $runJournal) { Write-Ok "Batch runner available: $runJournal" }
}

# destination
$setUserDir = $false
if ($Target) {
    $dest = $Target
    Write-Ok "Install target (explicit): $dest  (UGII_USER_DIR will NOT be changed)"
} elseif ($env:UGII_USER_DIR -or [Environment]::GetEnvironmentVariable('UGII_USER_DIR', 'User')) {
    $dest = if ($env:UGII_USER_DIR) { $env:UGII_USER_DIR } else { [Environment]::GetEnvironmentVariable('UGII_USER_DIR', 'User') }
    Write-Ok "Existing UGII_USER_DIR will be used: $dest"
} else {
    $dest = Join-Path $env:LOCALAPPDATA 'SynodeNX\nx_plugin'
    $setUserDir = $true
    Write-Ok "Install target: $dest  (USER variable UGII_USER_DIR will be set)"
}
if ($dest -and ($dest -split ';').Count -gt 1) {
    Write-Fail "UGII_USER_DIR contains several folders ($dest). Use -Target <one folder>."
    $problems++
}

$existing = @()
foreach ($rel in $PluginFiles + @($PackageDir)) {
    if ($dest -and (Test-Path -LiteralPath (Join-Path $dest $rel))) { $existing += $rel }
}
if ($existing.Count -gt 0) {
    if ($Force) { Write-Warn2 "Existing plugin files will be overwritten: $($existing -join ', ')" }
    else { Write-Fail "Plugin already installed in $dest (use -Force to overwrite or run uninstall.ps1)."; $problems++ }
}

if ($problems -gt 0) {
    Write-Fail "$problems problem(s) found. Nothing was changed."
    exit 1
}
if ($WhatIf) {
    Write-Step 'WhatIf: the following would be done:'
    foreach ($rel in $PluginFiles) { Write-Host "  copy nx_plugin\$rel -> $(Join-Path $dest $rel)" }
    Write-Host "  copy nx_plugin\$PackageDir\* -> $(Join-Path $dest $PackageDir)"
    if ($setUserDir) { Write-Host "  set USER environment variable UGII_USER_DIR=$dest" }
    Write-Host "  write $(Join-Path $dest $ManifestName)"
    exit 0
}

# ---------------------------------------------------------------- install
Write-Step "Installing into $dest"
$installed = New-Object System.Collections.Generic.List[string]
foreach ($sub in @('startup', 'application')) {
    New-Item -ItemType Directory -Force -Path (Join-Path $dest $sub) | Out-Null
}
foreach ($rel in $PluginFiles) {
    Copy-Item -LiteralPath (Join-Path $source $rel) -Destination (Join-Path $dest $rel) -Force
    $installed.Add($rel)
}
$pkgSource = Join-Path $source $PackageDir
$pkgDest = Join-Path $dest $PackageDir
if (Test-Path -LiteralPath $pkgDest) { Remove-Item -LiteralPath $pkgDest -Recurse -Force }
New-Item -ItemType Directory -Force -Path $pkgDest | Out-Null
Get-ChildItem -LiteralPath $pkgSource -Recurse -File | Where-Object {
    $_.Extension -eq '.py' -and $_.FullName -notmatch '\\__pycache__\\'
} | ForEach-Object {
    $rel = $_.FullName.Substring($pkgSource.Length).TrimStart('\')
    $to = Join-Path $pkgDest $rel
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $to) | Out-Null
    Copy-Item -LiteralPath $_.FullName -Destination $to -Force
}
$installed.Add($PackageDir)

if ($setUserDir) {
    [Environment]::SetEnvironmentVariable('UGII_USER_DIR', $dest, 'User')
    Write-Ok "USER environment variable UGII_USER_DIR=$dest"
}

$manifest = [ordered]@{
    product       = 'Synode NX GLB Exporter'
    installedAt   = (Get-Date).ToString('s')
    target        = $dest
    nxRoot        = $nx
    files         = @($installed)
    setUserDir    = $setUserDir
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dest $ManifestName) -Encoding UTF8
Write-Ok "Manifest: $(Join-Path $dest $ManifestName)"

Write-Step 'Done. Restart Siemens NX, open a part or assembly, then use:'
Write-Host '  - menu  Synode > Export to GLB'
Write-Host '  - ribbon tab "Synode" > Export to GLB'
Write-Host "  - or File > Execute > NX Open... (Ctrl+U) and choose $(Join-Path $dest 'application\synode_export_glb.py')"
Write-Host "Log file: $env:TEMP\SynodeNX\synode_nx_export.log"
