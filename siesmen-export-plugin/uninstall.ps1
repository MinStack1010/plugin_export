<#
.SYNOPSIS
    Removes the Synode NX GLB Exporter installed by install.ps1.

.DESCRIPTION
    Reads synode_install_manifest.json from the install folder and removes only
    the files/folders listed there.  The USER variable UGII_USER_DIR is removed
    only if install.ps1 created it and it still points to the install folder.
    Folders created by the installer are removed when they are empty.

.PARAMETER Target
    Install folder (defaults: UGII_USER_DIR, then %LOCALAPPDATA%\SynodeNX\nx_plugin).

.PARAMETER WhatIf
    Only print what would be removed.
#>
[CmdletBinding()]
param(
    [string]$Target,
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'
$ManifestName = 'synode_install_manifest.json'

$candidates = @()
if ($Target) { $candidates += $Target }
else {
    if ($env:UGII_USER_DIR) { $candidates += $env:UGII_USER_DIR }
    $userValue = [Environment]::GetEnvironmentVariable('UGII_USER_DIR', 'User')
    if ($userValue) { $candidates += $userValue }
    $candidates += (Join-Path $env:LOCALAPPDATA 'SynodeNX\nx_plugin')
}

$dest = $null
foreach ($c in $candidates | Select-Object -Unique) {
    if (Test-Path -LiteralPath (Join-Path $c $ManifestName)) { $dest = $c; break }
}
if (-not $dest) {
    Write-Host "[Synode] No installation manifest ($ManifestName) found in: $($candidates -join '; ')" -ForegroundColor Yellow
    Write-Host '[Synode] Nothing removed.'
    exit 1
}

$manifestPath = Join-Path $dest $ManifestName
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
Write-Host "[Synode] Uninstalling from $dest"

$allowed = @(
    'startup\synode_glb_exporter.men',
    'startup\synode_glb_exporter.rtb',
    'application\synode_export_glb.py',
    'application\synode_nx_exporter'
)
foreach ($rel in $manifest.files) {
    if ($allowed -notcontains $rel) {
        Write-Host "  skip unexpected manifest entry: $rel" -ForegroundColor Yellow
        continue
    }
    $path = Join-Path $dest $rel
    if (-not (Test-Path -LiteralPath $path)) { continue }
    if ($WhatIf) { Write-Host "  would remove $path"; continue }
    Remove-Item -LiteralPath $path -Recurse -Force
    Write-Host "  removed $path"
}

$userValue = [Environment]::GetEnvironmentVariable('UGII_USER_DIR', 'User')
if ($manifest.setUserDir -and $userValue -and ($userValue.TrimEnd('\') -ieq ([string]$manifest.target).TrimEnd('\'))) {
    if ($WhatIf) { Write-Host '  would remove USER environment variable UGII_USER_DIR' }
    else {
        [Environment]::SetEnvironmentVariable('UGII_USER_DIR', $null, 'User')
        Write-Host '  removed USER environment variable UGII_USER_DIR'
    }
}

if (-not $WhatIf) {
    Remove-Item -LiteralPath $manifestPath -Force
    if ($manifest.setUserDir) {
        foreach ($sub in @('startup', 'application', '')) {
            $dir = if ($sub) { Join-Path $dest $sub } else { $dest }
            if ((Test-Path -LiteralPath $dir) -and -not (Get-ChildItem -LiteralPath $dir -Force)) {
                Remove-Item -LiteralPath $dir -Force
            }
        }
    }
}
Write-Host '[Synode] Done. Restart Siemens NX.'
