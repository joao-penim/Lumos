# Regenerates assets/*.ico (app icon + tray state icons). Run with Windows PowerShell 5.1:
#   powershell -ExecutionPolicy Bypass -File tools/make-icons.ps1
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
$assets = Join-Path $PSScriptRoot '..\assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function New-Sun([System.Drawing.Graphics]$g, [float]$cx, [float]$cy, [float]$r, $fill, $stroke, [float]$strokeW, [bool]$hollow) {
    # Core disc
    $core = $r * 0.46
    if ($hollow) {
        $pen = New-Object System.Drawing.Pen($fill, ($r * 0.16))
        $g.DrawEllipse($pen, $cx - $core, $cy - $core, $core * 2, $core * 2)
    } else {
        if ($stroke) {
            $sp = New-Object System.Drawing.Pen($stroke, $strokeW)
            $g.DrawEllipse($sp, $cx - $core, $cy - $core, $core * 2, $core * 2)
        }
        $g.FillEllipse((New-Object System.Drawing.SolidBrush($fill)), $cx - $core, $cy - $core, $core * 2, $core * 2)
    }
    # Rays
    $rayPen = New-Object System.Drawing.Pen($fill, ($r * 0.17))
    $rayPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $rayPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    for ($i = 0; $i -lt 8; $i++) {
        $a = [Math]::PI * 2 * $i / 8
        $x1 = $cx + [Math]::Cos($a) * $r * 0.68; $y1 = $cy + [Math]::Sin($a) * $r * 0.68
        $x2 = $cx + [Math]::Cos($a) * $r * 0.92; $y2 = $cy + [Math]::Sin($a) * $r * 0.92
        $g.DrawLine($rayPen, [float]$x1, [float]$y1, [float]$x2, [float]$y2)
    }
}

function Render([int]$size, [string]$kind) {
    $ss = 4; $S = $size * $ss
    $big = New-Object System.Drawing.Bitmap($S, $S, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($big)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $c = $S / 2
    switch ($kind) {
        'app' {
            $rad = $S * 0.22
            $path = New-Object System.Drawing.Drawing2D.GraphicsPath
            $m = $S * 0.04; $w = $S - 2 * $m
            $path.AddArc($m, $m, $rad, $rad, 180, 90)
            $path.AddArc($m + $w - $rad, $m, $rad, $rad, 270, 90)
            $path.AddArc($m + $w - $rad, $m + $w - $rad, $rad, $rad, 0, 90)
            $path.AddArc($m, $m + $w - $rad, $rad, $rad, 90, 90)
            $path.CloseFigure()
            $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
                (New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF($S, $S)),
                [System.Drawing.Color]::FromArgb(255, 58, 42, 140), [System.Drawing.Color]::FromArgb(255, 18, 96, 196))
            $g.FillPath($grad, $path)
            # soft glow
            $glow = New-Object System.Drawing.Drawing2D.GraphicsPath
            $glow.AddEllipse($c - $S * 0.36, $c - $S * 0.36, $S * 0.72, $S * 0.72)
            $pgb = New-Object System.Drawing.Drawing2D.PathGradientBrush($glow)
            $pgb.CenterColor = [System.Drawing.Color]::FromArgb(140, 255, 214, 110)
            $pgb.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 255, 214, 110))
            $g.FillPath($pgb, $glow)
            New-Sun $g $c $c ($S * 0.34) ([System.Drawing.Color]::FromArgb(255, 255, 222, 120)) $null 0 $false
        }
        'on'     { New-Sun $g $c $c ($S * 0.5) ([System.Drawing.Color]::FromArgb(255, 255, 196, 46)) ([System.Drawing.Color]::FromArgb(255, 150, 100, 0)) ($S * 0.03) $false }
        'off'    { New-Sun $g $c $c ($S * 0.5) ([System.Drawing.Color]::FromArgb(255, 170, 170, 170)) $null 0 $true }
        'paused' {
            New-Sun $g $c $c ($S * 0.5) ([System.Drawing.Color]::FromArgb(150, 170, 170, 170)) $null 0 $true
            $bw = $S * 0.13; $bh = $S * 0.42; $bx = $S * 0.56; $by = $S * 0.54
            $br = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 230, 120, 60))
            $g.FillRectangle($br, $bx, $by, $bw, $bh)
            $g.FillRectangle($br, $bx + $bw * 1.7, $by, $bw, $bh)
        }
    }
    $g.Dispose()
    $small = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g2 = [System.Drawing.Graphics]::FromImage($small)
    $g2.InterpolationMode = 'HighQualityBicubic'
    $g2.PixelOffsetMode = 'HighQuality'
    $g2.DrawImage($big, 0, 0, $size, $size)
    $g2.Dispose(); $big.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $small.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $small.Dispose()
    return , $ms.ToArray()
}

function Write-Ico([string]$file, [string]$kind, [int[]]$sizes) {
    $images = @(); foreach ($s in $sizes) { $images += , (Render $s $kind) }
    $out = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($out)
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $len = $images[$i].Length
        $dim = if ($s -ge 256) { 0 } else { $s }
        $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
        $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$len); $bw.Write([uint32]$offset)
        $offset += $len
    }
    foreach ($img in $images) { $bw.Write($img) }
    $bw.Flush()
    [System.IO.File]::WriteAllBytes((Join-Path $assets $file), $out.ToArray())
    Write-Host "wrote $file"
}

Write-Ico 'lumos.ico' 'app' @(16, 20, 24, 32, 40, 48, 64, 128, 256)
Write-Ico 'tray-on.ico' 'on' @(16, 20, 24, 32, 40, 48)
Write-Ico 'tray-off.ico' 'off' @(16, 20, 24, 32, 40, 48)
Write-Ico 'tray-paused.ico' 'paused' @(16, 20, 24, 32, 40, 48)

# PNG of the app icon for the UI (window logo, About page)
[System.IO.File]::WriteAllBytes((Join-Path $assets 'lumos-256.png'), (Render 256 'app'))
Write-Host 'wrote lumos-256.png'
