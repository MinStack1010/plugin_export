<#
.SYNOPSIS
  Install Synode Inventor Exporter into Autodesk Inventor 2027 (per-user by default, no admin needed).

.DESCRIPTION
  Copies the files produced by build.ps1 (dist\) to the Autodesk-defined add-in folder:
     per-user  : %APPDATA%\Autodesk\Inventor 2027\Addins\SynodeInventorExporter\
     all users : %ProgramData%\Autodesk\Inventor 2027\Addins\SynodeInventorExporter\   (-AllUsers, needs admin)
  Files: SynodeInventor.AddIn.dll, SynodeInventor.AddIn.pdb, Synode.InventorExporter.addin.
  Inventor discovers the add-in from the .addin manifest: no RegAsm, no COM registration, no registry writes.
  Safe to run again (files are replaced). Never closes Inventor.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\install.ps1 -DryRun
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\install.ps1
#>
[CmdletBinding()]
param(
    [string]$SourceDir = '',
    [switch]$AllUsers,
    [switch]$DryRun,
    [switch]$Force,
    [string]$InventorBinDir = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $SourceDir) { $SourceDir = Join-Path $here 'dist' }
. (Join-Path $here 'scripts\Find-Inventor.ps1')

$AddInClassId = '{7001FEF0-B792-4C1D-9FB4-0515C8F2F90B}'
$FolderName = 'SynodeInventorExporter'
$ManifestName = 'Synode.InventorExporter.addin'
$DllName = 'SynodeInventor.AddIn.dll'
$PdbName = 'SynodeInventor.AddIn.pdb'

function Write-Step([string]$text) { Write-Host ''; Write-Host "==> $text" -ForegroundColor Cyan }
function Write-Ok([string]$text) { Write-Host "    [OK] $text" -ForegroundColor Green }
function Write-Warn2([string]$text) { Write-Host "    [WARN] $text" -ForegroundColor Yellow }
function Write-Info([string]$text) { Write-Host "    $text" }
function Fail([string]$text) { Write-Host "    [FAIL] $text" -ForegroundColor Red; exit 1 }

function Get-ManifestClassId([string]$path) {
    try {
        [xml]$x = Get-Content -LiteralPath $path -Raw
        $node = $x.SelectSingleNode('/Addin/ClassId')
        if ($node) { return $node.InnerText.Trim() }
    }
    catch { Write-Warn2 "Cannot read manifest '$path': $($_.Exception.Message)" }
    return $null
}

function Test-IsAdmin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

Write-Host 'Synode Inventor Exporter - install' -ForegroundColor White
if ($DryRun) { Write-Host '(DRY RUN - nothing will be changed)' -ForegroundColor Yellow }

# ---------------------------------------------------------------- 1. Inventor
Write-Step 'Detecting Autodesk Inventor 2027'
$inv = Get-SynodeInventorInfo -InventorBinDir $InventorBinDir
if ($inv.Found) {
    Write-Ok "Inventor.exe $($inv.FileVersion) at $($inv.BinDir)"
}
else {
    $inv.Problems | ForEach-Object { Write-Warn2 $_ }
    if (-not $Force) { Fail 'Inventor 2027 not found. Use -InventorBinDir, or -Force to install anyway.' }
    Write-Warn2 'Continuing because -Force was given.'
}

# ---------------------------------------------------------------- 2. Build output
Write-Step "Checking build output in $SourceDir"
$srcDll = Join-Path $SourceDir $DllName
$srcManifest = Join-Path $SourceDir $ManifestName
if (-not (Test-Path -LiteralPath $srcDll)) { Fail "$DllName not found. Run .\build.ps1 first." }
if (-not (Test-Path -LiteralPath $srcManifest)) { Fail "$ManifestName not found. Run .\build.ps1 first." }
$srcClassId = Get-ManifestClassId $srcManifest
if ($srcClassId -ne $AddInClassId) { Fail "Manifest ClassId '$srcClassId' does not match $AddInClassId." }
$asm = [System.Reflection.AssemblyName]::GetAssemblyName($srcDll)
Write-Ok "$DllName $($asm.Version) ($([math]::Round((Get-Item -LiteralPath $srcDll).Length / 1KB, 1)) KB)"
$buildInfo = Join-Path $SourceDir 'build-info.txt'
if (Test-Path -LiteralPath $buildInfo) {
    $tfmLine = Select-String -LiteralPath $buildInfo -Pattern '^TargetFramework=' | Select-Object -First 1
    if ($tfmLine) { Write-Ok ("Built for " + ($tfmLine.Line -replace '^TargetFramework=', '')) }
}

# ---------------------------------------------------------------- 3. Target folder
Write-Step 'Resolving add-in folder'
if ($AllUsers) {
    if (-not (Test-IsAdmin)) { Fail '-AllUsers writes to %ProgramData% and requires an elevated (Administrator) PowerShell. Without -AllUsers no admin is needed.' }
    $addinsRoot = $inv.AllUsersAddinsDir
}
else {
    $addinsRoot = $inv.UserAddinsDir
}
$targetDir = Join-Path $addinsRoot $FolderName
Write-Ok "Target: $targetDir"

$targetManifest = Join-Path $targetDir $ManifestName
if (Test-Path -LiteralPath $targetManifest) {
    $existingId = Get-ManifestClassId $targetManifest
    if ($existingId -and $existingId -ne $AddInClassId -and -not $Force) {
        Fail "A different add-in manifest ($existingId) already exists at $targetManifest. Not overwriting (use -Force)."
    }
    Write-Info 'Existing Synode installation found - it will be updated.'
}

# Other manifests with our ClassId would make Inventor load the add-in twice.
foreach ($root in @($inv.UserAddinsDir, $inv.AllUsersAddinsDir) | Select-Object -Unique) {
    if (-not (Test-Path -LiteralPath $root)) { continue }
    Get-ChildItem -LiteralPath $root -Filter '*.addin' -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
        if ($_.FullName -ne $targetManifest -and (Get-ManifestClassId $_.FullName) -eq $AddInClassId) {
            Write-Warn2 "Another manifest with the Synode ClassId exists: $($_.FullName). Run uninstall.ps1 (with -AllUsers if needed) to remove it."
        }
    }
}

# ---------------------------------------------------------------- 4. Inventor running?
if ($inv.IsRunning) {
    Write-Warn2 "Inventor is running ($($inv.RunningCount) process(es)). This script will NOT close it."
    Write-Warn2 'If the add-in is already loaded the DLL is locked and copying fails: close Inventor yourself and run again.'
    Write-Warn2 'A newly installed add-in is loaded the next time Inventor starts.'
}

# ---------------------------------------------------------------- 5. Copy
Write-Step 'Installing files'
$files = @($DllName, $ManifestName)
if (Test-Path -LiteralPath (Join-Path $SourceDir $PdbName)) { $files += $PdbName }
if ($DryRun) {
    Write-Info "Would create folder: $targetDir"
    foreach ($f in $files) { Write-Info "Would copy: $(Join-Path $SourceDir $f) -> $(Join-Path $targetDir $f)" }
    Write-Host ''
    Write-Host 'DRY RUN complete - no changes made.' -ForegroundColor Yellow
    exit 0
}

New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
foreach ($f in $files) {
    $src = Join-Path $SourceDir $f
    $dst = Join-Path $targetDir $f
    try {
        Copy-Item -LiteralPath $src -Destination $dst -Force
        Unblock-File -LiteralPath $dst -ErrorAction SilentlyContinue
    }
    catch [System.IO.IOException] {
        Fail "Cannot replace '$dst' (file in use - Inventor has the add-in loaded). Close Inventor and run install.ps1 again."
    }
    catch [System.UnauthorizedAccessException] {
        Fail "Access denied writing '$dst'. $($_.Exception.Message)"
    }
    $hSrc = (Get-FileHash -LiteralPath $src -Algorithm SHA256).Hash
    $hDst = (Get-FileHash -LiteralPath $dst -Algorithm SHA256).Hash
    if ($hSrc -ne $hDst) { Fail "Verification failed for $dst (hash mismatch)." }
    Write-Ok $dst
}

Write-Host ''
Write-Host 'INSTALL SUCCEEDED' -ForegroundColor Green
Write-Host "  Add-in folder : $targetDir"
Write-Host '  Next steps    :'
Write-Host '    1. Start (or restart) Autodesk Inventor 2027.'
Write-Host '    2. Open a part (.ipt) or assembly (.iam) -> ribbon tab "Synode" -> "Export to GLB".'
Write-Host '    3. Tools > Add-Ins should list "Synode Inventor Exporter" as Loaded.'
Write-Host ('  Log file      : ' + (Join-Path $env:LOCALAPPDATA 'Synode\InventorExporter\logs\SynodeInventor.log'))
exit 0
