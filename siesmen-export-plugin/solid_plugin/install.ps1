<#
.SYNOPSIS
    Đăng ký COM add-in Synode Exporter cho Solid Edge 2026 (ghi registry - CẦN XÁC NHẬN).
.DESCRIPTION
    * Kiểm tra: Windows/PowerShell x64, Solid Edge 2026, .NET Framework 4.8, DLL tồn tại và là x64.
    * Hiển thị CHÍNH XÁC các khóa sẽ ghi, hỏi xác nhận (Y/N) trừ khi có -Force.
    * Chỉ ghi CLSID {42B484DA-1593-41C3-9623-1BFAA4DA645C} và ProgId SynodeExporter.SolidEdge.AddIn.
      Không chạm vào khóa của add-in khác, license hay cấu hình Siemens.
    * -Scope Machine (mặc định, HKLM, cần Administrator) | CurrentUser (HKCU, thử nghiệm).
    * -DryRun: chỉ in ra các khóa, không ghi gì.
.EXAMPLE
    # PowerShell x64 "Run as Administrator":
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

Write-Host '== Kiểm tra môi trường ==' -ForegroundColor Cyan
$envInfo = & (Join-Path $here 'scripts\Test-Environment.ps1') -SolidEdgeDir $SolidEdgeDir
if ($envInfo.Problems.Count -gt 0 -and -not $Force) {
    throw "Môi trường chưa đạt (xem [LOI] ở trên). Dùng -Force nếu vẫn muốn đăng ký."
}
if (-not [Environment]::Is64BitProcess) {
    throw 'Hãy chạy bằng Windows PowerShell 64-bit (không phải PowerShell (x86)).'
}
if (-not (Test-Path $DllPath)) {
    throw "Không tìm thấy DLL: $DllPath. Hãy chạy build.ps1 trước."
}
$DllPath = (Resolve-Path $DllPath).Path
$an = [Reflection.AssemblyName]::GetAssemblyName($DllPath)
if ($an.ProcessorArchitecture -ne [Reflection.ProcessorArchitecture]::Amd64) {
    throw "DLL không phải x64 ($($an.ProcessorArchitecture))."
}

$entries = Get-SynodeRegistryEntries -DllPath $DllPath
$hn = Get-SynodeHiveName $Scope
Write-Host ''
Write-Host "Các mục registry sẽ ghi ($hn, view 64-bit):" -ForegroundColor Cyan
Write-SynodeEntriesPreview $entries $Scope

if ($DryRun) {
    Write-Host ''
    Write-Host 'DryRun: không ghi registry.' -ForegroundColor Yellow
    return
}

if ($Scope -eq 'Machine' -and -not (Test-SynodeIsAdmin)) {
    throw 'Scope Machine (HKLM) cần quyền Administrator. Mở PowerShell bằng "Run as administrator".'
}

$existing = Get-SynodeExistingCodeBase $Scope
$newCodeBase = ([Uri]$DllPath).AbsoluteUri
if ($null -ne $existing -and $existing -ne $newCodeBase -and -not $Force) {
    throw "CLSID $SynodeClsid đã tồn tại trong $hn với CodeBase khác: '$existing'. Chạy uninstall.ps1 trước hoặc dùng -Force."
}

if (-not $Force) {
    $answer = Read-Host "Xác nhận ghi các khóa trên vào $hn? (Y/N)"
    if ($answer -notmatch '^(y|yes|c|co)$') { Write-Host 'Đã hủy, không thay đổi registry.' -ForegroundColor Yellow; return }
}

$process = Get-Process -Name Edge -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$SolidEdgeDir*" }
if ($process) { Write-Warning 'Solid Edge đang chạy: hãy khởi động lại Solid Edge sau khi cài đặt.' }

$base = Get-SynodeHive $Scope
try {
    # Xóa sạch khóa cũ CỦA RIÊNG Synode (nếu có) để không còn giá trị thừa từ phiên bản trước.
    foreach ($k in Get-SynodeRootKeys) { $base.DeleteSubKeyTree($k, $false) }
    foreach ($x in $entries) {
        $key = $base.CreateSubKey($x.Key)
        try {
            if ($x.Kind -eq 'DWord') { $key.SetValue($x.Name, [int]$x.Value, [Microsoft.Win32.RegistryValueKind]::DWord) }
            elseif ($x.Kind -eq 'String') { $key.SetValue($x.Name, [string]$x.Value, [Microsoft.Win32.RegistryValueKind]::String) }
        } finally { $key.Close() }
    }
} finally { $base.Close() }

# Kiểm tra lại
$check = Get-SynodeExistingCodeBase $Scope
if ($check -ne $newCodeBase) { throw "Kiểm tra sau khi ghi thất bại (CodeBase = '$check')." }

Write-Host ''
Write-Host "ĐÃ ĐĂNG KÝ Synode Exporter ($hn)." -ForegroundColor Green
Write-Host "  DLL: $DllPath"
Write-Host '  Mở Solid Edge 2026 -> Tools (Công cụ) -> Add-Ins -> Add-In Manager: "Synode Exporter" phải được đánh dấu.'
Write-Host '  Lưu ý: không di chuyển/xóa DLL khi đang đăng ký; nếu build lại vào chỗ khác, chạy lại install.ps1.'
