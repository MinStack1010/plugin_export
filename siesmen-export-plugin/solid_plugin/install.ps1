<#
.SYNOPSIS
    Registers the Synode Exporter COM add-in for Solid Edge 2026 (writes to the registry - REQUIRES CONFIRMATION).
.DESCRIPTION
    * Checks: 64-bit Windows/PowerShell, Solid Edge 2026, .NET Framework 4.8, and that the DLL exists and is x64.
    * Shows EXACTLY which keys will be written and asks for confirmation (Y/N) unless -Force is used.
    * Only writes CLSID {42B484DA-1593-41C3-9623-1BFAA4DA645C} and ProgId SynodeExporter.SolidEdge.AddIn.
      Does not touch keys of other add-ins, licenses or Siemens settings.
    * -Scope Machine (default, HKLM, requires Administrator) | CurrentUser (HKCU, experimental).
    * -DryRun: only prints the keys, writes nothing.
.EXAMPLE
    # 64-bit PowerShell, "Run as Administrator":
    powershell -ExecutionPolicy Bypass -File .\install.ps1
#>
[CmdletBinding()]
param(
    [string]$DllPath,
    [ValidateSet('Machine', 'CurrentUser')][string]$Scope = 'Machine',
    [string]$SolidEdgeDir = 'C:\Program Files\Siemens\Solid Edge 2026',
    [switch]$Force,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
. (Join-Path $here 'scripts\RegistryLayout.ps1')
if (-not $DllPath) { $DllPath = Join-Path $here 'dist\SynodeExporter.SolidEdge.dll' }

Write-Host '== Environment check ==' -ForegroundColor Cyan
$envInfo = & (Join-Path $here 'scripts\Test-Environment.ps1') -SolidEdgeDir $SolidEdgeDir
if ($envInfo.Problems.Count -gt 0 -and -not $Force) {
    throw "The environment does not meet the requirements (see [FAIL] above). Use -Force to register anyway."
}
if (-not [Environment]::Is64BitProcess) {
    throw 'Run this script in 64-bit Windows PowerShell (not Windows PowerShell (x86)).'
}
if (-not (Test-Path $DllPath)) {
    throw "DLL not found: $DllPath. Run build.ps1 first."
}
$DllPath = (Resolve-Path $DllPath).Path
$an = [Reflection.AssemblyName]::GetAssemblyName($DllPath)
if ($an.ProcessorArchitecture -ne [Reflection.ProcessorArchitecture]::Amd64) {
    throw "The DLL is not x64 ($($an.ProcessorArchitecture))."
}

$entries = Get-SynodeRegistryEntries -DllPath $DllPath
$hn = Get-SynodeHiveName $Scope
Write-Host ''
Write-Host "Registry entries to be written ($hn, 64-bit view):" -ForegroundColor Cyan
Write-SynodeEntriesPreview $entries $Scope

if ($DryRun) {
    Write-Host ''
    Write-Host 'DryRun: the registry was not modified.' -ForegroundColor Yellow
    return
}

if ($Scope -eq 'Machine' -and -not (Test-SynodeIsAdmin)) {
    throw 'Scope Machine (HKLM) requires Administrator rights. Open PowerShell with "Run as administrator".'
}

$existing = Get-SynodeExistingCodeBase $Scope
$newCodeBase = ([Uri]$DllPath).AbsoluteUri
if ($null -ne $existing -and $existing -ne $newCodeBase -and -not $Force) {
    throw "CLSID $SynodeClsid already exists in $hn with a different CodeBase: '$existing'. Run uninstall.ps1 first or use -Force."
}

if (-not $Force) {
    $answer = Read-Host "Write the keys above to $hn? (Y/N)"
    # 'c' / 'co' are also accepted as "yes" for backward compatibility.
    if ($answer -notmatch '^(y|yes|c|co)$') { Write-Host 'Cancelled; the registry was not modified.' -ForegroundColor Yellow; return }
}

$process = Get-Process -Name Edge -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$SolidEdgeDir*" }
if ($process) { Write-Warning 'Solid Edge is running: restart Solid Edge after the installation.' }

$base = Get-SynodeHive $Scope
try {
    # Remove any existing Synode-ONLY keys first so that no stale values from a previous version remain.
    foreach ($k in Get-SynodeRootKeys) { $base.DeleteSubKeyTree($k, $false) }
    foreach ($x in $entries) {
        $key = $base.CreateSubKey($x.Key)
        try {
            if ($x.Kind -eq 'DWord') { $key.SetValue($x.Name, [int]$x.Value, [Microsoft.Win32.RegistryValueKind]::DWord) }
            elseif ($x.Kind -eq 'String') { $key.SetValue($x.Name, [string]$x.Value, [Microsoft.Win32.RegistryValueKind]::String) }
        } finally { $key.Close() }
    }
} finally { $base.Close() }

# Verify the result
$check = Get-SynodeExistingCodeBase $Scope
if ($check -ne $newCodeBase) { throw "Post-write verification failed (CodeBase = '$check')." }

Write-Host ''
Write-Host "Synode Exporter REGISTERED ($hn)." -ForegroundColor Green
Write-Host "  DLL: $DllPath"
Write-Host '  Open Solid Edge 2026 -> Tools -> Add-Ins -> Add-In Manager: "Synode Exporter" must be checked.'
Write-Host '  Note: do not move/delete the DLL while it is registered; if you rebuild it to another location, run install.ps1 again.'
