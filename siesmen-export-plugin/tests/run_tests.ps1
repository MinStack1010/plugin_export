<#
.SYNOPSIS
    Builds the test tools and runs the Synode NX exporter test suite.

.DESCRIPTION
    - tests\tools\pyhost      : runs the CPython runtime shipped with Siemens NX
                                (NXBIN\python\python312.dll), so tests use the same
                                interpreter as NX. Skipped when -Python is given.
    - tests\reference_sw      : compiles the ORIGINAL SOLIDWORKS writer sources
                                (read-only link) + SharpGLTF 1.0.6 from the SOLIDWORKS
                                plugin folder. Skipped with -SkipReference or when
                                the SOLIDWORKS plugin folder is missing.
    Build output goes to -OutputRoot (default %TEMP%\SynodeNXTests); nothing is
    written into the SOLIDWORKS project.  Requires the .NET SDK (8+) for the tools.

.PARAMETER Python
    A python.exe (3.8+) to use instead of the NX runtime (no PyHost build).
.PARAMETER SwPluginDir
    SOLIDWORKS plugin folder (default ..\solidworks-export-plugin next to this project).
.PARAMETER OutputRoot
    Folder for build output and temporary test files.
.PARAMETER SkipReference
    Do not build/run the SOLIDWORKS comparison.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\tests\run_tests.ps1
#>
[CmdletBinding()]
param(
    [string]$Python,
    [string]$SwPluginDir,
    [string]$OutputRoot = (Join-Path $env:TEMP 'SynodeNXTests'),
    [switch]$SkipReference
)

$ErrorActionPreference = 'Stop'
$testsDir = $PSScriptRoot
$projectDir = Split-Path -Parent $testsDir
if (-not $SwPluginDir) { $SwPluginDir = Join-Path (Split-Path -Parent $projectDir) 'solidworks-export-plugin' }

function Build-Tool([string]$name, [string]$sourceDir, [string[]]$extra) {
    $src = Join-Path $OutputRoot "src\$name"
    $out = Join-Path $OutputRoot "bin\$name"
    if (Test-Path -LiteralPath $src) { Remove-Item -LiteralPath $src -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $src | Out-Null
    Get-ChildItem -LiteralPath $sourceDir -File | Where-Object { $_.Extension -in '.cs', '.csproj' } |
        Copy-Item -Destination $src
    Write-Host "[tests] dotnet build $name -> $out"
    & dotnet build (Join-Path $src "$name.csproj") -c Release -o $out --nologo -v q @extra | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed for $name" }
    return (Join-Path $out "$name.exe")
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$needDotnet = (-not $Python) -or (-not $SkipReference)
if ($needDotnet -and -not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK (dotnet) not found. Install it, or use -Python <python.exe> -SkipReference.'
}

if ($Python) {
    $runner = @($Python)
} else {
    $pyhost = Build-Tool 'PyHost' (Join-Path $testsDir 'tools\pyhost') @()
    $runner = @($pyhost)
}

$env:SYNODE_REFERENCE_SW_EXE = ''
if (-not $SkipReference) {
    $writer = Join-Path $SwPluginDir 'Export\GltfSceneWriter.cs'
    if (Test-Path -LiteralPath $writer) {
        $swDir = (Resolve-Path -LiteralPath $SwPluginDir).Path.TrimEnd('\') + '\'
        $env:SYNODE_REFERENCE_SW_EXE = Build-Tool 'ReferenceSw' (Join-Path $testsDir 'reference_sw') @("-p:SwPluginDir=$swDir")
    } else {
        Write-Host "[tests] SOLIDWORKS plugin not found at $SwPluginDir - comparison tests will be skipped." -ForegroundColor Yellow
    }
}

$env:SYNODE_TEST_TMP = Join-Path $OutputRoot 'tmp'
New-Item -ItemType Directory -Force -Path $env:SYNODE_TEST_TMP | Out-Null
$env:PYTHONDONTWRITEBYTECODE = '1'
Write-Host "[tests] running tests\run_tests.py"
& $runner[0] (Join-Path $testsDir 'run_tests.py')
$code = $LASTEXITCODE
Write-Host "[tests] exit code $code"
exit $code
