<#
.SYNOPSIS
    Regenerates src/GitPersona.App/Assets/GitPersona.ico (gradient tile with a person glyph).
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/New-AppIcon.ps1
#>
param(
    [string]$OutPath = (Join-Path $PSScriptRoot '..\src\GitPersona.App\Assets\GitPersona.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded tile
    $s = [single]$size
    $d = $s * 0.44
    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tile.AddArc(0, 0, $d, $d, 180, 90)
    $tile.AddArc($s - $d, 0, $d, $d, 270, 90)
    $tile.AddArc($s - $d, $s - $d, $d, $d, 0, 90)
    $tile.AddArc(0, $s - $d, $d, $d, 90, 90)
    $tile.CloseFigure()
    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush (
        (New-Object System.Drawing.PointF 0, 0),
        (New-Object System.Drawing.PointF $s, $s),
        [System.Drawing.Color]::FromArgb(255, 0x4F, 0x46, 0xE5),
        [System.Drawing.Color]::FromArgb(255, 0x93, 0x33, 0xEA))
    $g.FillPath($gradient, $tile)

    # Person: head + shoulders
    $white = [System.Drawing.Brushes]::White
    $g.FillEllipse($white, $s * 0.355, $s * 0.17, $s * 0.29, $s * 0.29)
    $body = New-Object System.Drawing.Drawing2D.GraphicsPath
    $body.AddArc($s * 0.19, $s * 0.53, $s * 0.62, $s * 0.58, 180, 180)
    $body.CloseFigure()
    $g.FillPath($white, $body)

    $g.Dispose()
    return $bitmap
}

function Get-DibBytes([System.Drawing.Bitmap]$bitmap) {
    $size = $bitmap.Width
    $rect = New-Object System.Drawing.Rectangle 0, 0, $size, $size
    $data = $bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $stride = $data.Stride
    $pixels = New-Object byte[] ($stride * $size)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bitmap.UnlockBits($data)

    $maskRow = [int]([Math]::Ceiling($size / 32.0) * 4)
    $stream = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $stream
    # BITMAPINFOHEADER; height is doubled because the AND mask follows the colour data.
    $w.Write([int]40); $w.Write([int]$size); $w.Write([int]($size * 2))
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]0)
    $w.Write([int]($size * $size * 4 + $maskRow * $size))
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    for ($row = $size - 1; $row -ge 0; $row--) {   # bottom-up
        $w.Write($pixels, $row * $stride, $size * 4)
    }
    $w.Write((New-Object byte[] ($maskRow * $size)))  # transparency comes from alpha
    $w.Flush()
    return , $stream.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($size in $sizes) {
    $bitmap = New-IconBitmap $size
    if ($size -ge 256) {
        $png = New-Object System.IO.MemoryStream
        $bitmap.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
        $images.Add($png.ToArray())
    }
    else {
        $images.Add((Get-DibBytes $bitmap))
    }
    $bitmap.Dispose()
}

New-Item -ItemType Directory -Force (Split-Path $OutPath) | Out-Null
$file = [System.IO.File]::Create($OutPath)
$w = New-Object System.IO.BinaryWriter $file
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]$images[$i].Length); $w.Write([int]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $w.Write($image) }
$w.Dispose()
Write-Host "Wrote $OutPath"
