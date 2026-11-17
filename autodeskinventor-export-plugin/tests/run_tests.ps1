<#
.SYNOPSIS
  Build and run the unit tests (no Inventor needed). Equivalent to the test step of build.ps1.
#>
[CmdletBinding()]
param(
    [ValidateSet('', 'net8.0-windows', 'net10.0-windows')]
    [string]$TargetFramework = '',
    [string]$WorkDir = ''
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here

if (-not $TargetFramework) {
    $majors = @(& dotnet --list-sdks | ForEach-Object { if ($_ -match '^(\d+)\.') { [int]$Matches[1] } })
    $TargetFramework = if (($majors | Measure-Object -Maximum).Maximum -ge 10) { 'net10.0-windows' } else { 'net8.0-windows' }
}
if ($WorkDir) {
    New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
    & robocopy $root $WorkDir /MIR /XD bin obj dist .vs test-output TestResults .git /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { Write-Host "robocopy failed ($LASTEXITCODE)" -ForegroundColor Red; exit 1 }
    $root = $WorkDir
}
$proj = Join-Path $root 'tests\SynodeInventor.Tests\SynodeInventor.Tests.csproj'
& dotnet build $proj -c Release "-p:SynodeTargetFramework=$TargetFramework" -p:UseSharedCompilation=false -nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Host 'Unit test build failed.' -ForegroundColor Red; exit 1 }
& (Join-Path $root "tests\SynodeInventor.Tests\bin\Release\$TargetFramework\SynodeInventor.Tests.exe")
exit $LASTEXITCODE
