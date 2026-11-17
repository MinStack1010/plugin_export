<#
.SYNOPSIS
  Uninstall Synode Inventor Exporter from Autodesk Inventor 2027.

.DESCRIPTION
  Removes ONLY the Synode files from the add-in folder(s):
     %APPDATA%\Autodesk\Inventor 2027\Addins\SynodeInventorExporter\    (always)
     %ProgramData%\Autodesk\Inventor 2027\Addins\SynodeInventorExporter\ (with -AllUsers, needs admin)
  The folder is removed only when it is empty afterwards. Manifests are removed only if their
  ClassId is the Synode ClassId. Does not touch the registry or other add-ins. Never closes Inventor.
  Logs (%LOCALAPPDATA%\Synode\InventorExporter) are kept unless -RemoveLogs is given.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\uninstall.ps1 -DryRun
#>
[CmdletBinding()]
param(
    [switch]$AllUsers,
    [switch]$DryRun,
    [switch]$RemoveLogs
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $here 'scripts\Find-Inventor.ps1')

$AddInClassId = '{7001FEF0-B792-4C1D-9FB4-0515C8F2F90B}'
$FolderName = 'SynodeInventorExporter'
$ManifestName = 'Synode.InventorExporter.addin'
$OwnFiles = @('SynodeInventor.AddIn.dll', 'SynodeInventor.AddIn.pdb')

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

function Remove-OwnFile([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $true }
    if ($DryRun) { Write-Info "Would remove: $path"; return $true }
    try {
        Remove-Item -LiteralPath $path -Force
        Write-Ok "Removed $path"
        return $true
    }
    catch {
        Write-Warn2 "Cannot remove '$path': $($_.Exception.Message)"
        return $false
    }
}

Write-Host 'Synode Inventor Exporter - uninstall' -ForegroundColor White
if ($DryRun) { Write-Host '(DRY RUN - nothing will be changed)' -ForegroundColor Yellow }

$inv = Get-SynodeInventorInfo
if ($inv.IsRunning) {
    Write-Warn2 'Inventor is running. This script will NOT close it. If the add-in is loaded its DLL is locked;'
    Write-Warn2 'unload it in Tools > Add-Ins or close Inventor, then run uninstall.ps1 again.'
}

$roots = @($inv.UserAddinsDir)
if ($AllUsers) {
    if (-not (Test-IsAdmin) -and -not $DryRun) { Fail '-AllUsers requires an elevated (Administrator) PowerShell.' }
    $roots += $inv.AllUsersAddinsDir
}

$allOk = $true
$foundAny = $false
foreach ($root in $roots) {
    Write-Step "Checking $root"
    $dir = Join-Path $root $FolderName
    if (Test-Path -LiteralPath $dir) {
        $foundAny = $true
        $manifest = Join-Path $dir $ManifestName
        if (Test-Path -LiteralPath $manifest) {
            if ((Get-ManifestClassId $manifest) -eq $AddInClassId) { $allOk = (Remove-OwnFile $manifest) -and $allOk }
            else { Write-Warn2 "Manifest $manifest has a different ClassId - left untouched." }
        }
        foreach ($f in $OwnFiles) { $allOk = (Remove-OwnFile (Join-Path $dir $f)) -and $allOk }
        if (-not $DryRun -and (Test-Path -LiteralPath $dir)) {
            $left = @(Get-ChildItem -LiteralPath $dir -Force)
            if ($left.Count -eq 0) { Remove-Item -LiteralPath $dir -Force; Write-Ok "Removed folder $dir" }
            else { Write-Warn2 ("Folder kept, it contains other files: " + (($left | ForEach-Object Name) -join ', ')) }
        }
        elseif ($DryRun) { Write-Info "Would remove folder $dir if empty" }
    }
    # Stray copy of our manifest directly in the Addins root (only if it carries our ClassId).
    $stray = Join-Path $root $ManifestName
    if ((Test-Path -LiteralPath $stray) -and (Get-ManifestClassId $stray) -eq $AddInClassId) {
        $foundAny = $true
        $allOk = (Remove-OwnFile $stray) -and $allOk
    }
}

if ($RemoveLogs) {
    $logDir = Join-Path $env:LOCALAPPDATA 'Synode\InventorExporter'
    if (Test-Path -LiteralPath $logDir) {
        if ($DryRun) { Write-Info "Would remove logs: $logDir" }
        else { Remove-Item -LiteralPath $logDir -Recurse -Force; Write-Ok "Removed logs $logDir" }
    }
}

Write-Host ''
if (-not $foundAny) { Write-Host 'Synode Inventor Exporter is not installed - nothing to remove.' -ForegroundColor Green; exit 0 }
if ($DryRun) { Write-Host 'DRY RUN complete - no changes made.' -ForegroundColor Yellow; exit 0 }
if ($allOk) { Write-Host 'UNINSTALL SUCCEEDED. Restart Inventor if it is running.' -ForegroundColor Green; exit 0 }
Write-Host 'UNINSTALL INCOMPLETE - some files are in use. Close Inventor and run again.' -ForegroundColor Red
exit 1
