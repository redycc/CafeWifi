# Draws the tray and app icons into src/CafeWiFi/Resources/icons/*.ico.
# Run with Windows PowerShell: powershell -File tools/make-icons.ps1
Add-Type -AssemblyName System.Drawing

$out = Join-Path $PSScriptRoot '..\src\CafeWiFi\Resources\icons'
New-Item -ItemType Directory -Force $out | Out-Null

function Draw-Cup([int]$size, [string]$style) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 32.0

    $brown = [System.Drawing.Color]::FromArgb(255, 176, 120, 72)
    $cream = [System.Drawing.Color]::FromArgb(255, 240, 225, 205)
    $stroke = New-Object System.Drawing.Pen $brown, ([Math]::Max(1.5, 2.6 * $s))
    $stroke.LineJoin = 'Round'

    # Cup body: a rounded trapezoid, handle on the right, saucer underneath.
    $body = New-Object System.Drawing.Drawing2D.GraphicsPath
    $body.AddLine(5 * $s, 9 * $s, 23 * $s, 9 * $s)
    $body.AddBezier(23 * $s, 9 * $s, 23 * $s, 18 * $s, 21 * $s, 23 * $s, 14 * $s, 23 * $s)
    $body.AddBezier(14 * $s, 23 * $s, 7 * $s, 23 * $s, 5 * $s, 18 * $s, 5 * $s, 9 * $s)
    $body.CloseFigure()

    if ($style -eq 'filled' -or $style -eq 'app') {
        $g.FillPath((New-Object System.Drawing.SolidBrush $brown), $body)
    } else {
        $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, $cream))), $body)
    }
    $g.DrawPath($stroke, $body)
    $g.DrawArc($stroke, 20 * $s, 11 * $s, 8 * $s, 7 * $s, -90, 180)
    $g.DrawLine($stroke, 3 * $s, 27 * $s, 25 * $s, 27 * $s)

    if ($style -eq 'connecting') {
        $dot = New-Object System.Drawing.SolidBrush $brown
        foreach ($x in 8, 13, 18) { $g.FillEllipse($dot, $x * $s, 2 * $s, 3 * $s, 3 * $s) }
    } else {
        # Steam
        $steam = New-Object System.Drawing.Pen $brown, ([Math]::Max(1.2, 1.8 * $s))
        foreach ($x in 10, 16) { $g.DrawBezier($steam, $x * $s, 7 * $s, ($x - 2) * $s, 5 * $s, ($x + 2) * $s, 3.5 * $s, $x * $s, 1.5 * $s) }
    }
    $g.Dispose()
    return $bmp
}

function Write-Ico([string]$path, [string]$style, [int[]]$sizes) {
    $pngs = foreach ($sz in $sizes) {
        $ms = New-Object System.IO.MemoryStream
        (Draw-Cup $sz $style).Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        , $ms.ToArray()
    }
    $fs = [System.IO.File]::Create($path)
    $w = New-Object System.IO.BinaryWriter $fs
    $w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $sz = $sizes[$i]; $len = $pngs[$i].Length
        $w.Write([byte]($sz % 256)); $w.Write([byte]($sz % 256)); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
        $offset += $len
    }
    foreach ($p in $pngs) { $w.Write($p) }
    $w.Close()
}

Write-Ico (Join-Path $out 'cup.ico') 'outline' @(16, 20, 24, 32)
Write-Ico (Join-Path $out 'cup-filled.ico') 'filled' @(16, 20, 24, 32)
Write-Ico (Join-Path $out 'connecting.ico') 'connecting' @(16, 20, 24, 32)
Write-Ico (Join-Path $out 'app.ico') 'app' @(16, 24, 32, 48, 64, 128, 256)
Write-Output "icons written to $out"
