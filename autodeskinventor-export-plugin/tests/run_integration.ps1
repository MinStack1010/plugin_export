<#
.SYNOPSIS
  Integration test on a REAL Autodesk Inventor 2027 (starts a new Inventor instance via COM).

.DESCRIPTION
  - Copies the sample part into test-output\input (the original file is never modified).
  - Exports the part, builds a 3-occurrence assembly from it and exports that,
    using the same GlbExportService as the add-in (Inventor's native glTF translator).
  - Validates every GLB with GlbValidator and independently with SharpGLTF (strict).
  - Checks error cases (locked file, missing folder, unsupported drawing document).
  - Closes only the Inventor instance it started. Report: test-output\integration-report.txt
  Requires a working Inventor licence/trial. A licence dialog is an environment issue.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\tests\run_integration.ps1
#>
[CmdletBinding()]
param(
    [string]$Model = '',
    [string]$OutDir = '',
    [ValidateSet('', 'net8.0-windows', 'net10.0-windows')]
    [string]$TargetFramework = '',
    [string]$WorkDir = '',
    [switch]$Visible
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $here
. (Join-Path $root 'scripts\Find-Inventor.ps1')
if (-not $OutDir) { $OutDir = Join-Path $root 'test-output\integration' }

$inv = Get-SynodeInventorInfo
if (-not $inv.Found) { $inv.Problems | ForEach-Object { Write-Host $_ -ForegroundColor Red }; exit 2 }

if (-not $Model) {
    $sampleDir = Join-Path (Split-Path -Parent $inv.BinDir) 'Design Accelerator\models'
    $sample = Get-ChildItem -LiteralPath $sampleDir -Filter '*.ipt' -ErrorAction SilentlyContinue | Sort-Object Length | Select-Object -First 1
    if (-not $sample) { Write-Host "No sample .ipt found in $sampleDir. Use -Model <part.ipt>." -ForegroundColor Red; exit 2 }
    $Model = $sample.FullName
}
Write-Host "Model: $Model"

if (-not $TargetFramework) {
    $majors = @(& dotnet --list-sdks | ForEach-Object { if ($_ -match '^(\d+)\.') { [int]$Matches[1] } })
    $TargetFramework = if (($majors | Measure-Object -Maximum).Maximum -ge 10) { 'net10.0-windows' } else { 'net8.0-windows' }
}
$src = $root
if ($WorkDir) {
    New-Item -ItemType Directory -Force -Path $WorkDir | Out-Null
    & robocopy $root $WorkDir /MIR /XD bin obj dist .vs test-output TestResults .git /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { Write-Host "robocopy failed ($LASTEXITCODE)" -ForegroundColor Red; exit 1 }
    $src = $WorkDir
}
$proj = Join-Path $src 'tests\SynodeInventor.IntegrationTests\SynodeInventor.IntegrationTests.csproj'
& dotnet build $proj -c Release "-p:SynodeTargetFramework=$TargetFramework" "-p:InventorBinDir=$($inv.BinDir)" -p:UseSharedCompilation=false -nologo -v q
if ($LASTEXITCODE -ne 0) { Write-Host 'Integration test build failed.' -ForegroundColor Red; exit 1 }

$exe = Join-Path $src "tests\SynodeInventor.IntegrationTests\bin\Release\$TargetFramework\SynodeInventor.IntegrationTests.exe"
$argsList = @('--model', $Model, '--out', $OutDir)
if ($Visible) { $argsList += '--visible' }
& $exe @argsList
$code = $LASTEXITCODE
Write-Host "Report: $(Join-Path $OutDir 'integration-report.txt')"
exit $code
