<#
.SYNOPSIS
    Kiểm tra môi trường build/cài đặt Synode Exporter cho Solid Edge 2026 (chỉ đọc, không thay đổi hệ thống).
.OUTPUTS
    PSCustomObject với các thông tin môi trường; ném lỗi khi -Strict và thiếu điều kiện bắt buộc.
#>
[CmdletBinding()]
param(
    [string]$SolidEdgeDir = 'C:\Program Files\Siemens\Solid Edge 2026',
    [switch]$RequireSdk,
    [switch]$Strict
)

$ErrorActionPreference = 'Stop'
$problems = New-Object System.Collections.Generic.List[string]

function Write-Check([string]$Name, [bool]$Ok, [string]$Detail) {
    $mark = if ($Ok) { '[OK]  ' } else { '[LOI] ' }
    $color = if ($Ok) { 'Green' } else { 'Red' }
    Write-Host ($mark + $Name + ': ' + $Detail) -ForegroundColor $color
    if (-not $Ok) { $problems.Add($Name + ': ' + $Detail) }
}

# 1. Kiến trúc tiến trình
Write-Check 'Windows 64-bit' ([Environment]::Is64BitOperatingSystem) ([Environment]::OSVersion.VersionString)
Write-Check 'PowerShell 64-bit' ([Environment]::Is64BitProcess) 'Cần PowerShell x64 để ghi đúng registry view 64-bit'

# 2. Solid Edge 2026
$programDir = Join-Path $SolidEdgeDir 'Program'
$edge = Join-Path $programDir 'Edge.exe'
$edgeVersion = $null
if (Test-Path $edge) {
    $edgeVersion = (Get-Item $edge).VersionInfo.FileVersion
    $major = [int]((Get-Item $edge).VersionInfo.FileMajorPart)
    Write-Check 'Solid Edge (Edge.exe)' ($major -eq 226) ("$edge, phiên bản $edgeVersion (Solid Edge 2026 = 226.x)")
} else {
    Write-Check 'Solid Edge (Edge.exe)' $false "Không tìm thấy $edge"
}

$interops = 'interop.SolidEdgeFrameworkLib.dll', 'interop.SolidEdgePartLib.dll', 'interop.SolidEdgeAssemblyLib.dll',
            'interop.SolidEdgeGeometryLib.dll', 'interop.SolidEdgeConstantLib.dll'
foreach ($dll in $interops) {
    $path = Join-Path $programDir $dll
    Write-Check "Interop $dll" (Test-Path $path) $path
}

# 3. .NET Framework 4.8+ (Release >= 528040)
$release = 0
try {
    $release = [int](Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -Name Release).Release
} catch { }
Write-Check '.NET Framework 4.8+' ($release -ge 528040) ("Release = $release")

$regasm = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'
$mscoree = Join-Path $env:WINDIR 'System32\mscoree.dll'
Write-Check 'mscoree.dll (CLR shim x64)' (Test-Path $mscoree) $mscoree

# 4. SDK build (chỉ cần khi build)
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$sdkVersion = $null
if ($dotnet) {
    try { $sdkVersion = (& dotnet --version 2>$null | Select-Object -First 1) } catch { }
}
if ($RequireSdk) {
    Write-Check '.NET SDK (dotnet)' ([bool]$sdkVersion) ("dotnet --version = $sdkVersion")
    $refAsm = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
    Write-Check 'Reference assemblies .NET 4.8' (Test-Path $refAsm) $refAsm
}

$result = [pscustomobject]@{
    SolidEdgeDir      = $SolidEdgeDir
    ProgramDir        = $programDir
    EdgeVersion       = $edgeVersion
    NetFrameworkRelease = $release
    RegAsm            = $(if (Test-Path $regasm) { $regasm } else { $null })
    DotNetSdk         = $sdkVersion
    Is64BitProcess    = [Environment]::Is64BitProcess
    Problems          = $problems.ToArray()
}

if ($Strict -and $problems.Count -gt 0) {
    throw ("Môi trường chưa đáp ứng yêu cầu:`n - " + ($problems -join "`n - "))
}
$result
