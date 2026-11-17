<#
.SYNOPSIS
    Gỡ đăng ký COM add-in Synode Exporter khỏi Solid Edge 2026 (CẦN XÁC NHẬN).
.DESCRIPTION
    Chỉ xóa đúng 2 khóa của Synode Exporter (ở HKLM và/hoặc HKCU, view 64-bit):
      SOFTWARE\Classes\CLSID\{42B484DA-1593-41C3-9623-1BFAA4DA645C}
      SOFTWARE\Classes\SynodeExporter.SolidEdge.AddIn
    Không xóa file DLL, không chạm vào add-in khác hay cấu hình Siemens.
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\uninstall.ps1            # HKLM + HKCU (HKLM cần Administrator)
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

if (-not [Environment]::Is64BitProcess) { throw 'Hãy chạy bằng Windows PowerShell 64-bit.' }

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
    Write-Host 'Synode Exporter chưa được đăng ký - không có gì để gỡ.' -ForegroundColor Yellow
    return
}

Write-Host 'Các khóa sẽ bị XÓA (kèm khóa con):' -ForegroundColor Cyan
$targets | ForEach-Object { Write-Host "  $(Get-SynodeHiveName $_.Scope)\$($_.Key)" }
if ($DryRun) { Write-Host 'DryRun: không thay đổi registry.' -ForegroundColor Yellow; return }

if (($targets | Where-Object Scope -eq 'Machine') -and -not (Test-SynodeIsAdmin)) {
    throw 'Xóa khóa HKLM cần quyền Administrator. Mở PowerShell bằng "Run as administrator" (hoặc dùng -Scope CurrentUser).'
}
if (-not $Force) {
    $answer = Read-Host 'Xác nhận xóa? (Y/N)'
    if ($answer -notmatch '^(y|yes|c|co)$') { Write-Host 'Đã hủy.' -ForegroundColor Yellow; return }
}

foreach ($t in $targets) {
    $base = Get-SynodeHive $t.Scope
    try { $base.DeleteSubKeyTree($t.Key, $false) } finally { $base.Close() }
}
Write-Host 'ĐÃ GỠ đăng ký Synode Exporter. Khởi động lại Solid Edge để áp dụng.' -ForegroundColor Green
