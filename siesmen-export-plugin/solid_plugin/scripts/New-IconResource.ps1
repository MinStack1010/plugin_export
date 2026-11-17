<#
.SYNOPSIS
    Tạo file tài nguyên Win32 (.res) chứa biểu tượng nút "Export to GLB" cho add-in Solid Edge.
.DESCRIPTION
    ISEAddInEx.SetAddInInfoEx nạp ảnh nút theo ID tài nguyên trong chính DLL add-in.
    Script vẽ 2 bitmap 24-bit bằng System.Drawing (ID 101: 20x20, ID 102: 32x32 - khớp với
    SolidEdgeIds/SynodeAddIn) rồi ghi định dạng .res chuẩn để csc nhúng qua <Win32Resource>.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-GlbBitmapBytes([int]$Size) {
    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        $g.Clear([System.Drawing.Color]::White)

        # Khối lập phương isometric (mô hình 3D) màu xanh Synode.
        $s = [single]$Size
        $top = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF(($s * 0.5), ($s * 0.06))),
            (New-Object System.Drawing.PointF(($s * 0.92), ($s * 0.27))),
            (New-Object System.Drawing.PointF(($s * 0.5), ($s * 0.48))),
            (New-Object System.Drawing.PointF(($s * 0.08), ($s * 0.27))))
        $left = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF(($s * 0.08), ($s * 0.27))),
            (New-Object System.Drawing.PointF(($s * 0.5), ($s * 0.48))),
            (New-Object System.Drawing.PointF(($s * 0.5), ($s * 0.94))),
            (New-Object System.Drawing.PointF(($s * 0.08), ($s * 0.73))))
        $right = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF(($s * 0.92), ($s * 0.27))),
            (New-Object System.Drawing.PointF(($s * 0.92), ($s * 0.73))),
            (New-Object System.Drawing.PointF(($s * 0.5), ($s * 0.94))),
            (New-Object System.Drawing.PointF(($s * 0.5), ($s * 0.48))))
        $g.FillPolygon((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(110, 180, 245))), $top)
        $g.FillPolygon((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(30, 110, 200))), $left)
        $g.FillPolygon((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(15, 75, 150))), $right)
        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(10, 40, 90), [single]([Math]::Max(1, $Size / 20)))
        $g.DrawPolygon($pen, $top); $g.DrawPolygon($pen, $left); $g.DrawPolygon($pen, $right)

        # Chữ "GLB" trên nền tối ở nửa dưới.
        $fontSize = [single]([Math]::Max(5, $Size * 0.30))
        $font = New-Object System.Drawing.Font('Segoe UI', $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
        $fmt = New-Object System.Drawing.StringFormat
        $fmt.Alignment = [System.Drawing.StringAlignment]::Center
        $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
        $band = New-Object System.Drawing.RectangleF(0, ($s * 0.58), $s, ($s * 0.40))
        $g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(200, 35, 35))), $band)
        $g.DrawString('GLB', $font, [System.Drawing.Brushes]::White, $band, $fmt)
    }
    finally {
        $g.Dispose()
    }
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp.Dispose()
    $bytes = $ms.ToArray()
    # Tài nguyên RT_BITMAP = file BMP bỏ BITMAPFILEHEADER (14 byte).
    return ,($bytes[14..($bytes.Length - 1)])
}

function Write-ResEntry([System.IO.BinaryWriter]$w, [int]$type, [int]$id, [byte[]]$data, [int]$memoryFlags, [int]$language) {
    $w.Write([uint32]$data.Length)  # DataSize
    $w.Write([uint32]32)            # HeaderSize
    $w.Write([uint16]0xFFFF); $w.Write([uint16]$type)
    $w.Write([uint16]0xFFFF); $w.Write([uint16]$id)
    $w.Write([uint32]0)             # DataVersion
    $w.Write([uint16]$memoryFlags)
    $w.Write([uint16]$language)
    $w.Write([uint32]0)             # Version
    $w.Write([uint32]0)             # Characteristics
    if ($data.Length -gt 0) { $w.Write($data) }
    $pad = (4 - ($data.Length % 4)) % 4
    for ($i = 0; $i -lt $pad; $i++) { $w.Write([byte]0) }
}

$dir = Split-Path -Parent ([System.IO.Path]::GetFullPath($OutputPath))
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }

$stream = [System.IO.File]::Create($OutputPath)
$writer = New-Object System.IO.BinaryWriter($stream)
try {
    Write-ResEntry $writer 0 0 ([byte[]]@()) 0 0          # mục rỗng bắt buộc đầu file .res
    Write-ResEntry $writer 2 101 (New-GlbBitmapBytes 20) 0x1030 0x0409
    Write-ResEntry $writer 2 102 (New-GlbBitmapBytes 32) 0x1030 0x0409
}
finally {
    $writer.Dispose()
}
Write-Host "Đã tạo tài nguyên biểu tượng: $OutputPath"
