# Generates src/VpnBypass/Assets/eye.ico (neon eye logo) as a multi-size PNG-based ICO.
param([string]$Out = (Join-Path $PSScriptRoot '..\src\VpnBypass\Assets\eye.ico'))

Add-Type -AssemblyName System.Drawing

function New-EyePng([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded dark tile
    $r = [single]($s * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $r, $r, 180, 90)
    $path.AddArc($s - $r - 1, 0, $r, $r, 270, 90)
    $path.AddArc($s - $r - 1, $s - $r - 1, $r, $r, 0, 90)
    $path.AddArc(0, $s - $r - 1, $r, $r, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 6, 10, 12))), $path)
    $g.DrawPath((New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 0, 90, 60)), ([single]([math]::Max(1, $s / 64)))), $path)

    # Almond eye outline
    $cx = $s / 2.0; $cy = $s / 2.0; $w = $s * 0.40; $h = $s * 0.24
    $eye = New-Object System.Drawing.Drawing2D.GraphicsPath
    $eye.AddBezier([single]($cx - $w), [single]$cy, [single]($cx - $w * 0.45), [single]($cy - $h * 1.6), [single]($cx + $w * 0.45), [single]($cy - $h * 1.6), [single]($cx + $w), [single]$cy)
    $eye.AddBezier([single]($cx + $w), [single]$cy, [single]($cx + $w * 0.45), [single]($cy + $h * 1.6), [single]($cx - $w * 0.45), [single]($cy + $h * 1.6), [single]($cx - $w), [single]$cy)
    $neon = [System.Drawing.Color]::FromArgb(255, 0, 255, 156)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(40, 0, 255, 156))), $eye)
    $g.DrawPath((New-Object System.Drawing.Pen $neon, ([single]([math]::Max(1.2, $s / 22)))), $eye)

    # Iris + pupil (looking slightly right)
    $ir = $s * 0.15; $ix = $cx + $s * 0.04
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0, 229, 255))), [single]($ix - $ir), [single]($cy - $ir), [single]($ir * 2), [single]($ir * 2))
    $pr = $ir * 0.48
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 4, 8, 10))), [single]($ix - $pr), [single]($cy - $pr), [single]($pr * 2), [single]($pr * 2))
    if ($s -ge 32) {
        $hr = $ir * 0.22
        $g.FillEllipse([System.Drawing.Brushes]::White, [single]($ix - $ir * 0.45), [single]($cy - $ir * 0.55), [single]$hr, [single]$hr)
    }
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @(); foreach ($s in $sizes) { $pngs += , (New-EyePng $s) }

$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = $pngs[$i]
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$d.Length); $bw.Write([uint32]$offset)
    $offset += $d.Length
}
foreach ($d in $pngs) { $bw.Write($d) }
$bw.Close()
Write-Host "Wrote $Out"
