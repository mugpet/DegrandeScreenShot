Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class NativeIconMethods
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool DestroyIcon(IntPtr handle);
}
"@

$assetDir = Split-Path -Parent $MyInvocation.MyCommand.Path

function New-RoundedPath([float]$x, [float]$y, [float]$width, [float]$height, [float]$radius)
{
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $radius * 2
    $path.AddArc($x, $y, $diameter, $diameter, 180, 90)
    $path.AddArc($x + $width - $diameter, $y, $diameter, $diameter, 270, 90)
    $path.AddArc($x + $width - $diameter, $y + $height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($x, $y + $height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Add-Corner(
    [System.Drawing.Graphics]$graphics,
    [System.Drawing.Pen]$pen,
    [float]$x,
    [float]$y,
    [float]$horizontalDirection,
    [float]$verticalDirection,
    [float]$length)
{
    $graphics.DrawLine($pen, $x, $y, $x + ($horizontalDirection * $length), $y)
    $graphics.DrawLine($pen, $x, $y, $x, $y + ($verticalDirection * $length))
}

function New-IconBitmap([int]$size)
{
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $scale = $size / 256.0
    $outerPath = New-RoundedPath (16 * $scale) (16 * $scale) (224 * $scale) (224 * $scale) (54 * $scale)
    $backgroundBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (
        [System.Drawing.PointF]::new(32 * $scale, 24 * $scale)),
        ([System.Drawing.PointF]::new(224 * $scale, 232 * $scale)),
        ([System.Drawing.Color]::FromArgb(255, 14, 67, 63)),
        ([System.Drawing.Color]::FromArgb(255, 28, 127, 110))
    $graphics.FillPath($backgroundBrush, $outerPath)

    $highlightBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (
        [System.Drawing.PointF]::new(44 * $scale, 40 * $scale)),
        ([System.Drawing.PointF]::new(140 * $scale, 118 * $scale)),
        ([System.Drawing.Color]::FromArgb(120, 255, 255, 255)),
        ([System.Drawing.Color]::FromArgb(0, 255, 255, 255))
    $graphics.FillEllipse($highlightBrush, 36 * $scale, 30 * $scale, 124 * $scale, 90 * $scale)

    $cardShadowBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, 4, 26, 24))
    $cardShadowPath = New-RoundedPath (80 * $scale) (88 * $scale) (112 * $scale) (88 * $scale) (20 * $scale)
    $graphics.FillPath($cardShadowBrush, $cardShadowPath)

    $cardBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(48, 255, 255, 255))
    $cardPath = New-RoundedPath (70 * $scale) (78 * $scale) (112 * $scale) (88 * $scale) (20 * $scale)
    $graphics.FillPath($cardBrush, $cardPath)

    $innerPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(210, 255, 255, 255)), (10 * $scale)
    $innerPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $innerFrame = New-RoundedPath (66 * $scale) (74 * $scale) (124 * $scale) (100 * $scale) (26 * $scale)
    $graphics.DrawPath($innerPen, $innerFrame)

    $cornerPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 255, 255, 255)), (16 * $scale)
    $cornerPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $cornerPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $cornerPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $accentPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 50, 212, 235)), (16 * $scale)
    $accentPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $accentPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $accentPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    $left = 58 * $scale
    $top = 66 * $scale
    $right = 198 * $scale
    $bottom = 186 * $scale
    $cornerLength = 34 * $scale

    Add-Corner $graphics $cornerPen $left $top 1 1 $cornerLength
    Add-Corner $graphics $cornerPen $right $top -1 1 $cornerLength
    Add-Corner $graphics $cornerPen $right $bottom -1 -1 $cornerLength
    Add-Corner $graphics $accentPen $left $bottom 1 -1 $cornerLength

    $dotBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 50, 212, 235))
    $graphics.FillEllipse($dotBrush, 176 * $scale, 44 * $scale, 28 * $scale, 28 * $scale)

    $ringPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(180, 255, 255, 255)), (6 * $scale)
    $graphics.DrawEllipse($ringPen, 176 * $scale, 44 * $scale, 28 * $scale, 28 * $scale)

    $dotBrush.Dispose()
    $ringPen.Dispose()
    $accentPen.Dispose()
    $cornerPen.Dispose()
    $innerFrame.Dispose()
    $innerPen.Dispose()
    $cardPath.Dispose()
    $cardBrush.Dispose()
    $cardShadowPath.Dispose()
    $cardShadowBrush.Dispose()
    $highlightBrush.Dispose()
    $backgroundBrush.Dispose()
    $outerPath.Dispose()
    $graphics.Dispose()

    return $bitmap
}

$previewBitmap = New-IconBitmap 256
$previewStream = New-Object System.IO.MemoryStream
$previewBitmap.Save($previewStream, [System.Drawing.Imaging.ImageFormat]::Png)
[System.IO.File]::WriteAllBytes((Join-Path $assetDir 'AppIcon-256.png'), $previewStream.ToArray())
$previewStream.Dispose()

$iconPath = Join-Path $assetDir 'AppIcon.ico'
$iconHandle = $previewBitmap.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($iconHandle)
$fileStream = [System.IO.File]::Open($iconPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write)
$icon.Save($fileStream)
$fileStream.Dispose()
$icon.Dispose()
[NativeIconMethods]::DestroyIcon($iconHandle) | Out-Null
$previewBitmap.Dispose()

Write-Output "Created $iconPath"
