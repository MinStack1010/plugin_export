<#
.SYNOPSIS
    Build và chạy bộ test của Synode Exporter (Solid Edge).
.DESCRIPTION
    * net48  : unit test Core trên đúng runtime của Edge.exe (CLR 4.8 x64) + xuất GLB mẫu.
    * net9.0 : unit test + so sánh byte-đối-byte với writer gốc của plugin SOLIDWORKS
               (liên kết chỉ-đọc mã nguồn Export\*.cs + SharpGLTF 1.0.6 trong packages của plugin SW),
               đồng thời kiểm tra GLB do net48 sinh ra giống hệt net9.
               Nếu không có thư mục plugin SW, phần net9 được bỏ qua (có ghi chú).
    Toàn bộ bin/obj được tạo trong -WorkDir (bản sao), không làm bẩn thư mục nguồn.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$WorkDir,
    [string]$SwPluginDir
)

$ErrorActionPreference = 'Stop'
$here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = Split-Path $here -Parent   # ...\solid_plugin
if (-not $WorkDir) { $WorkDir = Join-Path $env:TEMP 'SynodeSolidEdgeBuild' }
if (-not $SwPluginDir) { $SwPluginDir = Join-Path $here '..\..\..\solidworks-export-plugin' }
if (-not (Test-Path $WorkDir)) { New-Item -ItemType Directory -Path $WorkDir | Out-Null }

function Invoke-DotNetBuild([string]$Project, [string[]]$ExtraArgs) {
    $args2 = @('build', $Project, '-c', $Configuration, '-nologo', '-p:UseSharedCompilation=false') + $ExtraArgs
    $out = & dotnet @args2 2>&1
    $code = $LASTEXITCODE
    $out | Where-Object { $_ -match '(error|warning) [A-Z]+[0-9]+' } | Select-Object -Unique | ForEach-Object { Write-Host "  $_" }
    if ($code -ne 0) {
        $out | Select-Object -Last 20 | ForEach-Object { Write-Host "  $_" }
        throw "dotnet build thất bại (exit $code): $Project $($ExtraArgs -join ' ')"
    }
}

# 1. Chuẩn bị bản sao nguồn
$testWork = Join-Path $WorkDir 'testsrc'
if (Test-Path $testWork) { Remove-Item $testWork -Recurse -Force }
New-Item -ItemType Directory -Path $testWork | Out-Null
Copy-Item (Join-Path $root 'src') (Join-Path $testWork 'src') -Recurse
Copy-Item (Join-Path $root 'tests') (Join-Path $testWork 'tests') -Recurse
Get-ChildItem $testWork -Recurse -Directory -Include bin, obj | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

$proj = Join-Path $testWork 'tests\SynodeExporter.Tests\SynodeExporter.Tests.csproj'
$bin = Join-Path $testWork "tests\SynodeExporter.Tests\bin\$Configuration"
$dump = Join-Path $testWork 'dump48'

$haveSw = Test-Path (Join-Path $SwPluginDir 'Export\GltfSceneWriter.cs')
$swArg = @()
if ($haveSw) {
    $swFull = (Resolve-Path $SwPluginDir).Path.TrimEnd('\') + '\'
    $swArg = @("-p:SwPluginDir=$swFull")
}

# 2. net48
Write-Host '== Build test net48 ==' -ForegroundColor Cyan
Invoke-DotNetBuild $proj (@('-f', 'net48') + $swArg)
Write-Host '== Chạy test net48 ==' -ForegroundColor Cyan
& (Join-Path $bin 'net48\SynodeExporter.Tests.exe') --dump $dump | Where-Object { $_ -notmatch '^PASS' } | ForEach-Object { Write-Host "  $_" }
if ($LASTEXITCODE -ne 0) { throw "Test net48 THẤT BẠI (exit $LASTEXITCODE)" }

# 3. net9 + đối chiếu plugin SOLIDWORKS
if ($haveSw) {
    Write-Host '== Build test net9.0 (đối chiếu writer SOLIDWORKS) ==' -ForegroundColor Cyan
    Invoke-DotNetBuild $proj (@('-f', 'net9.0') + $swArg)
    Write-Host '== Chạy test net9.0 ==' -ForegroundColor Cyan
    & (Join-Path $bin 'net9.0\SynodeExporter.Tests.exe') --compare-net48 $dump | Where-Object { $_ -notmatch '^PASS' } | ForEach-Object { Write-Host "  $_" }
    if ($LASTEXITCODE -ne 0) { throw "Test net9.0 THẤT BẠI (exit $LASTEXITCODE)" }
} else {
    Write-Warning "Không tìm thấy plugin SOLIDWORKS tại '$SwPluginDir' -> bỏ qua test net9.0 (đối chiếu byte-đối-byte)."
}

Write-Host 'TẤT CẢ TEST ĐẠT.' -ForegroundColor Green
