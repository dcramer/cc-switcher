# Regenerates the app icon and the pictures made from it: src/CcSwitcher/app.ico, docs/icon.png
# and the installer's wizard images. Run with Windows PowerShell; the results are checked in.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Each size is drawn this many times larger and scaled down, which is smoother than GDI+'s own edges.
$oversample = 8

function Get-Color([string]$hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

function New-Point([double]$x, [double]$y) { New-Object System.Drawing.PointF ([single]$x), ([single]$y) }

# A superellipse: the soft-cornered square Claude's own icon sits on.
function New-Card([double]$cx, [double]$cy, [double]$radius) {
    $points = for ($i = 0; $i -lt 160; $i++) {
        $angle = 2 * [Math]::PI * $i / 160
        $cos = [Math]::Cos($angle); $sin = [Math]::Sin($angle)
        New-Point ($cx + $radius * [Math]::Sign($cos) * [Math]::Pow([Math]::Abs($cos), 2 / 4.6)) `
                  ($cy + $radius * [Math]::Sign($sin) * [Math]::Pow([Math]::Abs($sin), 2 / 4.6))
    }
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddPolygon([System.Drawing.PointF[]]$points)
    $path
}

function Add-Card($g, $path, [string]$top, [string]$bottom) {
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $path.GetBounds(), (Get-Color $top), (Get-Color $bottom), 90
    $g.FillPath($brush, $path)
    $brush.Dispose()
}

# A burst of tapered, round-tipped rays. Twelve uneven ones read as hand-drawn; the small sizes
# only have the pixels for a plain asterisk.
function Add-Burst($g, [double]$cx, [double]$cy, [double]$radius, [int]$size) {
    $rays = if ($size -le 20) { 6 } elseif ($size -le 40) { 8 } else { 12 }
    $thickness = if ($size -le 20) { 0.16 } elseif ($size -le 40) { 0.115 } else { 0.08 }
    $rotation = if ($size -le 20) { -90 } else { -8 }
    $lean = 0, 4, -3, 2, -5, 3, 0, -4, 3, -2, 5, -3
    $reach = 1, 0.80, 0.94, 0.76, 0.98, 0.84, 0.92, 0.78, 1, 0.82, 0.95, 0.80

    $brush = New-Object System.Drawing.SolidBrush (Get-Color $(if ($size -le 24) { '#FFFFFF' } else { '#FAF9F5' }))
    for ($i = 0; $i -lt $rays; $i++) {
        $degrees = $rotation + $i * 360 / $rays
        $length = 1 - 0.14 * ($i % 2)
        if ($rays -eq 12) { $degrees += $lean[$i]; $length = $reach[$i] }

        $angle = $degrees * [Math]::PI / 180
        $dx = [Math]::Cos($angle); $dy = [Math]::Sin($angle)
        $tip = $radius * $thickness; $root = $tip * 0.4
        $end = $radius * $length - $tip
        $g.FillPolygon($brush, [System.Drawing.PointF[]]@(
            (New-Point ($cx + $dy * $root) ($cy - $dx * $root)),
            (New-Point ($cx + $dx * $end + $dy * $tip) ($cy + $dy * $end - $dx * $tip)),
            (New-Point ($cx + $dx * $end - $dy * $tip) ($cy + $dy * $end + $dx * $tip)),
            (New-Point ($cx - $dy * $root) ($cy + $dx * $root))))
        $g.FillEllipse($brush, [single]($cx + $dx * $end - $tip), [single]($cy + $dy * $end - $tip), [single](2 * $tip), [single](2 * $tip))
    }
    $brush.Dispose()
}

function New-IconBitmap([int]$size) {
    $large = New-Object System.Drawing.Bitmap ($size * $oversample), ($size * $oversample), ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($large)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    # Everything below is drawn in a 1x1 square.
    $g.ScaleTransform($large.Width, $large.Height)

    # Two overlapping cards: one profile in front of another. Card edges and the gap between
    # the cards land on whole pixels so the small sizes stay crisp.
    $radius = [Math]::Round($size * 0.69) / (2.0 * $size)
    $gap = [Math]::Max(1, [Math]::Round($size * 0.045)) / $size
    $front = $radius, (1 - $radius)

    Add-Card $g (New-Card (1 - $radius) $radius $radius) '#7AA8D6' '#5B8CBE'

    # Cut the gap out of the back card rather than painting it, so it works on any background.
    $g.SmoothingMode = 'None'
    $g.CompositingMode = 'SourceCopy'
    $g.FillPath([System.Drawing.Brushes]::Transparent, (New-Card $front[0] $front[1] ($radius + $gap)))
    $g.CompositingMode = 'SourceOver'
    $g.SmoothingMode = 'AntiAlias'

    Add-Card $g (New-Card $front[0] $front[1] $radius) '#E08462' '#CC6A4A'
    Add-Burst $g $front[0] $front[1] ($radius * $(if ($size -le 20) { 0.76 } else { 0.72 })) $size
    $g.Dispose()

    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $edges = New-Object System.Drawing.Imaging.ImageAttributes
    $edges.SetWrapMode('TileFlipXY')
    $g.DrawImage($large, (New-Object System.Drawing.Rectangle 0, 0, $size, $size), 0, 0, $large.Width, $large.Height, 'Pixel', $edges)

    $g.Dispose(); $large.Dispose()
    $bitmap
}

function New-IconPng([int]$size) {
    $bitmap = New-IconBitmap $size
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    , $stream.ToArray()
}

# The icon centered on a plain or shaded background. Inno Setup takes its wizard images as
# bitmaps without transparency.
function Save-WizardImage([string]$path, [int]$width, [int]$height, [int]$iconSize, [int]$iconTop, [string]$top, [string]$bottom) {
    $bitmap = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $area = New-Object System.Drawing.Rectangle 0, 0, $width, $height
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $area, (Get-Color $top), (Get-Color $bottom), 90
    $g.FillRectangle($brush, $area)
    $icon = New-IconBitmap $iconSize
    $g.DrawImageUnscaled($icon, [int](($width - $iconSize) / 2), $iconTop)
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $icon.Dispose(); $brush.Dispose(); $g.Dispose(); $bitmap.Dispose()
}

# 20 and 40 are what the title bar and taskbar ask for at 125% display scaling.
$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$images = $sizes | ForEach-Object { , (New-IconPng $_) }

$out = Join-Path $PSScriptRoot '..\src\CcSwitcher\app.ico'
$writer = New-Object System.IO.BinaryWriter ([System.IO.File]::Create($out))
$writer.Write([int16]0); $writer.Write([int16]1); $writer.Write([int16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([int16]1); $writer.Write([int16]32)
    $writer.Write([int32]$images[$i].Length); $writer.Write([int32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write([byte[]]$image) }
$writer.Close()
"Wrote $out"

$readme = Join-Path $PSScriptRoot '..\docs\icon.png'
[System.IO.File]::WriteAllBytes($readme, [byte[]]$images[-1])
"Wrote $readme"

# Twice the size the wizard shows them at 100% scaling; it shrinks them to fit.
$installer = Join-Path $PSScriptRoot '..\installer'
Save-WizardImage (Join-Path $installer 'wizard-small.bmp') 110 110 94 8 '#FFFFFF' '#FFFFFF'
Save-WizardImage (Join-Path $installer 'wizard.bmp') 328 628 176 150 '#FAF9F5' '#EDE9DD'
"Wrote $installer\wizard-small.bmp and wizard.bmp"
