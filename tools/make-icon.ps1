# Draws the Youtube Downloader app icon and writes YoutubeDownloader.ico (multi-size, PNG entries) + Assets\icon-256.png.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
New-Item -ItemType Directory -Force (Join-Path $root 'Assets') | Out-Null

function New-RoundRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $s = [single]$size

    # Rounded "squircle" tile with a red gradient.
    $m = $s * 0.04
    $tile = New-RoundRect $m $m ($s - 2 * $m) ($s - 2 * $m) ($s * 0.23)
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, 0)), ([System.Drawing.PointF]::new(0, $s)), ([System.Drawing.Color]::FromArgb(255, 255, 92, 80)), ([System.Drawing.Color]::FromArgb(255, 200, 16, 30))
    $g.FillPath($grad, $tile)

    # White rounded "screen" with a red play triangle.
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $red = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 230, 33, 40))
    $sw = $s * 0.56; $sh = $s * 0.38
    $sx = ($s - $sw) / 2; $sy = $s * 0.17
    $screen = New-RoundRect $sx $sy $sw $sh ($s * 0.1)
    $g.FillPath($white, $screen)
    $cx = $s / 2; $cy = $sy + $sh / 2; $t = $s * 0.085
    $tri = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($cx - $t * 0.8, $cy - $t),
        [System.Drawing.PointF]::new($cx - $t * 0.8, $cy + $t),
        [System.Drawing.PointF]::new($cx + $t * 1.1, $cy))
    $g.FillPolygon($red, $tri)

    # Download arrow + tray below.
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1.3, $s * 0.06))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    $ay0 = $s * 0.61; $ay1 = $s * 0.80; $aw = $s * 0.09
    $g.DrawLine($pen, $cx, $ay0, $cx, $ay1)
    $g.DrawLine($pen, $cx - $aw, $ay1 - $aw, $cx, $ay1)
    $g.DrawLine($pen, $cx + $aw, $ay1 - $aw, $cx, $ay1)
    if ($size -ge 32) {
        $thin = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(200, 255, 255, 255)), ([Math]::Max(1, $s * 0.035))
        $thin.StartCap = 'Round'; $thin.EndCap = 'Round'
        $g.DrawLine($thin, $s * 0.3, $s * 0.87, $s * 0.7, $s * 0.87)
    }
    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($sz in $sizes) {
    $bmp = Draw-Icon $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($sz -eq 256) { $bmp.Save((Join-Path $root 'Assets\icon-256.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    $pngs += , $ms.ToArray()
}

# ICO container with PNG-compressed entries.
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $len = $pngs[$i].Length
    $dim = if ($sz -ge 256) { 0 } else { $sz }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $root 'YoutubeDownloader.ico'), $out.ToArray())
Write-Output "Wrote YoutubeDownloader.ico"
