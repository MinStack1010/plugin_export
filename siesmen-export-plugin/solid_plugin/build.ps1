<#
.SYNOPSIS
    Build Synode Exporter cho Solid Edge 2026 (COM add-in .NET Framework 4.8, x64).
.DESCRIPTION
    1. Kiểm tra môi trường (Solid Edge 2026, interop, .NET Framework 4.8, .NET SDK).
    2. Sinh tài nguyên icon ribbon (RT_BITMAP 101/102).
    3. Build add-in (bản sao nguồn trong -WorkDir) và chép DLL vào -OutputDir.
    4. Kiểm tra DLL là x64 / CLR v4.
    5. Chạy unit test (tests\run_tests.ps1) trừ khi -SkipTests.
    Script KHÔNG ghi registry. Dùng install.ps1 để đăng ký add-in.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\build.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$SolidEdgeDir = 'C:\Program Files\Siemens\Solid Edge 2026',
    [string]$OutputDir,
    [string]$WorkDir,
    [string]$SwPluginDir,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 không đảm bảo $PSScriptRoot trong giá trị mặc định của param -> gán ở đây.
$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $OutputDir) { $OutputDir = Join-Path $here 'dist' }
if (-not $WorkDir) { $WorkDir = Join-Path $env:TEMP 'SynodeSolidEdgeBuild' }
if (-not $SwPluginDir) { $SwPluginDir = Join-Path $here '..\..\solidworks-export-plugin' }
$scripts = Join-Path $here 'scripts'

Write-Host '== 1. Kiểm tra môi trường ==' -ForegroundColor Cyan
$envInfo = & (Join-Path $scripts 'Test-Environment.ps1') -SolidEdgeDir $SolidEdgeDir -RequireSdk -Strict
Write-Host '== 1b. Kiểm tra encoding (UTF-8 BOM) và cú pháp các script PowerShell ==' -ForegroundColor Cyan
& (Join-Path $scripts 'Test-ScriptSyntax.ps1') -Root $here

if (-not (Test-Path $WorkDir)) { New-Item -ItemType Directory -Path $WorkDir | Out-Null }
$WorkDir = (Resolve-Path $WorkDir).Path
if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir | Out-Null }
$OutputDir = (Resolve-Path $OutputDir).Path

Write-Host '== 2. Sinh icon ribbon ==' -ForegroundColor Cyan
$res = Join-Path $WorkDir 'SynodeExporter.Icons.res'
& (Join-Path $scripts 'New-IconResource.ps1') -OutputPath $res

Write-Host '== 3. Build add-in ==' -ForegroundColor Cyan
$srcWork = Join-Path $WorkDir 'addinsrc'
if (Test-Path $srcWork) { Remove-Item $srcWork -Recurse -Force }
Copy-Item (Join-Path $here 'src') $srcWork -Recurse
Get-ChildItem $srcWork -Recurse -Directory -Include bin, obj | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
$buildOut = Join-Path $WorkDir 'addinbin'
if (Test-Path $buildOut) { Remove-Item $buildOut -Recurse -Force }

$proj = Join-Path $srcWork 'SynodeExporter.SolidEdge.csproj'
$out = & dotnet build $proj -c $Configuration -nologo `
    "-p:SolidEdgeProgramDir=$($envInfo.ProgramDir)" `
    "-p:Win32ResourceFile=$res" `
    '-p:UseSharedCompilation=false' `
    -o $buildOut 2>&1
$code = $LASTEXITCODE
$out | Where-Object { $_ -match '(error|warning) [A-Z]+[0-9]+' } | Select-Object -Unique | ForEach-Object { Write-Host "  $_" }
if ($code -ne 0) {
    $out | Select-Object -Last 20 | ForEach-Object { Write-Host "  $_" }
    throw "Build add-in thất bại (exit $code)."
}

$dllName = 'SynodeExporter.SolidEdge.dll'
foreach ($f in @($dllName, 'SynodeExporter.SolidEdge.pdb')) {
    $p = Join-Path $buildOut $f
    if (Test-Path $p) { Copy-Item $p $OutputDir -Force }
}
$dll = Join-Path $OutputDir $dllName
# Không phân phối interop của Solid Edge: Edge.exe nạp bản chính thức từ thư mục Program.
Get-ChildItem $OutputDir -Filter 'interop.SolidEdge*.dll' -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host '== 4. Kiểm tra DLL ==' -ForegroundColor Cyan
$an = [Reflection.AssemblyName]::GetAssemblyName($dll)
if ($an.ProcessorArchitecture -ne [Reflection.ProcessorArchitecture]::Amd64) {
    throw "DLL không phải x64 (ProcessorArchitecture = $($an.ProcessorArchitecture))."
}
Write-Host "  [OK] $($an.FullName)" -ForegroundColor Green
Write-Host "  [OK] Kiến trúc: $($an.ProcessorArchitecture) | Kích thước: $((Get-Item $dll).Length) byte" -ForegroundColor Green

if ($SkipTests) {
    Write-Warning 'Bỏ qua unit test (-SkipTests).'
} else {
    Write-Host '== 5. Unit test ==' -ForegroundColor Cyan
    & (Join-Path $here 'tests\run_tests.ps1') -Configuration $Configuration -WorkDir $WorkDir -SwPluginDir $SwPluginDir
}

Write-Host ''
Write-Host "BUILD THÀNH CÔNG: $dll" -ForegroundColor Green
Write-Host 'Bước tiếp theo: chạy install.ps1 (PowerShell x64, quyền Administrator) để đăng ký add-in.'
