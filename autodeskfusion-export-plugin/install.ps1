<#
.SYNOPSIS
  Installs or removes the Synode GLB Exporter add-in for Autodesk Fusion (Windows).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\install.ps1
  powershell -ExecutionPolicy Bypass -File .\install.ps1 -Uninstall
#>
param(
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$addinName = 'SynodeGlbExporter'
$source = Join-Path $PSScriptRoot $addinName
$addinsRoot = Join-Path $env:APPDATA 'Autodesk\Autodesk Fusion 360\API\AddIns'
$target = Join-Path $addinsRoot $addinName

if ($Uninstall) {
    if (Test-Path $target) {
        Remove-Item -Recurse -Force $target
        Write-Host "Removed $target"
    } else {
        Write-Host "Add-in is not installed ($target not found)."
    }
    return
}

if (-not (Test-Path (Join-Path $source "$addinName.manifest"))) {
    throw "Add-in source folder not found: $source"
}

New-Item -ItemType Directory -Force -Path $addinsRoot | Out-Null
if (Test-Path $target) {
    Remove-Item -Recurse -Force $target
}
Copy-Item -Recurse -Path $source -Destination $target
Get-ChildItem -Path $target -Recurse -Force -Directory -Filter '__pycache__' | Remove-Item -Recurse -Force

Write-Host "Installed to $target"
Write-Host "In Fusion: UTILITIES > ADD-INS > Scripts and Add-Ins > Add-Ins tab > SynodeGlbExporter > Run."
