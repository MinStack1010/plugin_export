<#
.SYNOPSIS
    Unregisters the Synode Exporter COM add-in from Solid Edge 2026 (REQUIRES CONFIRMATION).
.DESCRIPTION
    Deletes only the 2 Synode Exporter keys (in HKLM and/or HKCU, 64-bit view):
      SOFTWARE\Classes\CLSID\{42B484DA-1593-41C3-9623-1BFAA4DA645C}
      SOFTWARE\Classes\SynodeExporter.SolidEdge.AddIn
    Does not delete the DLL file and does not touch other add-ins or Siemens settings.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\uninstall.ps1            # HKLM + HKCU (HKLM requires Administrator)
    powershell -ExecutionPolicy Bypass -File .\uninstall.ps1 -Scope CurrentUser
#>
[CmdletBinding()]
param(
    [ValidateSet('All', 'Machine', 'CurrentUser')][string]$Scope = 'All',
    [switch]$Force,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
. (Join-Path $here 'scripts\RegistryLayout.ps1')

if (-not [Environment]::Is64BitProcess) { throw 'Run this script in 64-bit Windows PowerShell.' }

$scopes = if ($Scope -eq 'All') { @('Machine', 'CurrentUser') } else { @($Scope) }
$targets = @()
foreach ($s in $scopes) {
    $base = Get-SynodeHive $s
    try {
        foreach ($k in Get-SynodeRootKeys) {
            $sub = $base.OpenSubKey($k)
            if ($sub) { $sub.Close(); $targets += [pscustomobject]@{ Scope = $s; Key = $k } }
        }
    } finally { $base.Close() }
}

if ($targets.Count -eq 0) {
    Write-Host 'Synode Exporter is not registered - nothing to remove.' -ForegroundColor Yellow
    return
}

Write-Host 'Keys that will be DELETED (including subkeys):' -ForegroundColor Cyan
$targets | ForEach-Object { Write-Host "  $(Get-SynodeHiveName $_.Scope)\$($_.Key)" }
if ($DryRun) { Write-Host 'DryRun: the registry was not modified.' -ForegroundColor Yellow; return }

if (($targets | Where-Object Scope -eq 'Machine') -and -not (Test-SynodeIsAdmin)) {
    throw 'Deleting HKLM keys requires Administrator rights. Open PowerShell with "Run as administrator" (or use -Scope CurrentUser).'
}
if (-not $Force) {
    $answer = Read-Host 'Confirm deletion? (Y/N)'
    # 'c' / 'co' are also accepted as "yes" for backward compatibility.
    if ($answer -notmatch '^(y|yes|c|co)$') { Write-Host 'Cancelled.' -ForegroundColor Yellow; return }
}

foreach ($t in $targets) {
    $base = Get-SynodeHive $t.Scope
    try { $base.DeleteSubKeyTree($t.Key, $false) } finally { $base.Close() }
}
Write-Host 'Synode Exporter UNREGISTERED. Restart Solid Edge to apply the change.' -ForegroundColor Green
