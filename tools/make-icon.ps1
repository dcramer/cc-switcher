# Regenerates src/CcSwitcher/app.ico. Run with Windows PowerShell; the result is checked in.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-IconPng([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Two overlapping badges: one profile in front of another.
    $badge = $size * 0.62
    $radius = $badge * 0.44
    $layers = @(
        @{ X = $size * 0.34; Y = $size * 0.06; Color = '#3B82F6' },
        @{ X = $size * 0.04; Y = $size * 0.32; Color = '#D97757' }
    )
    foreach ($layer in $layers) {
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddArc($layer.X, $layer.Y, $radius, $radius, 180, 90)
        $path.AddArc($layer.X + $badge - $radius, $layer.Y, $radius, $radius, 270, 90)
        $path.AddArc($layer.X + $badge - $radius, $layer.Y + $badge - $radius, $radius, $radius, 0, 90)
        $path.AddArc($layer.X, $layer.Y + $badge - $radius, $radius, $radius, 90, 90)
        $path.CloseFigure()

        # A thin gap keeps the front badge distinct where the two overlap.
        $gap = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1, $size * 0.05))
        $g.DrawPath($gap, $path)
        $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($layer.Color))
        $g.FillPath($brush, $path)
    }

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bitmap.Dispose()
    , $stream.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 256
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
