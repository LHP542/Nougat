# Nougat App-Icon-Generator (PowerShell-Port von build_icon.py).
#
# Warum zwei Varianten: Der Arbeitslaptop hat kein echtes Python (unter
# WindowsApps liegt nur der Store-Stub), Bazzite hat Python + Pillow. Gleiche
# Geometrie, gleiche Farben, gleiches Ergebnis — bei Design-Änderungen BEIDE
# Skripte anpassen, sonst driften PNG und ICO je nach Rechner auseinander.
#
# Motiv: Bonbon-/Praline-Silhouette in Kroste-Gold auf abgerundetem dunklem
# Grund. Thematisch ein eingewickeltes NuGet-Paket, schriftfrei, auch als
# 16x16-Favicon lesbar.
#
# Aufruf aus dem Repo-Root:  pwsh -File scripts/build_icon.ps1
#
# Diese Datei ist UTF-8 MIT BOM gespeichert. Ohne BOM liest Windows
# PowerShell 5.1 sie als ANSI und macht aus "ä" ein "Ã¤".

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$APP_NAME = 'Nougat'

# Farben identisch zu build_icon.py
$GOLD    = [System.Drawing.Color]::FromArgb(255, 224, 177, 76)   # #E0B14C
$GOLD_D  = [System.Drawing.Color]::FromArgb(255, 176, 138, 55)   # dunkler für Kanten
$SURFACE = [System.Drawing.Color]::FromArgb(255, 22, 28, 35)     # #161C23
$BORDER  = [System.Drawing.Color]::FromArgb(255, 42, 51, 61)     # #2A333D

$CORNER = 48

function New-Graphics([System.Drawing.Bitmap]$bmp) {
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    return $g
}

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-Icon([int]$size) {
    $scale = $size / 256.0
    $bmp = New-Object System.Drawing.Bitmap $size, $size,
        ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = New-Graphics $bmp

    # Grund: Radius gegen die Kantenlänge deckeln, sonst degenerieren die Ecken
    # bei kleinen Größen und das Icon zerfällt sichtbar.
    $corner = [Math]::Max(2, [int][Math]::Min($CORNER * $scale, $size * 0.22))
    $bSurface = New-Object System.Drawing.SolidBrush $SURFACE
    $pBorder  = New-Object System.Drawing.Pen $BORDER, ([Math]::Max(1, [int](2 * $scale)))
    $bg = New-RoundedPath 0 0 ($size - 1) ($size - 1) $corner
    $g.FillPath($bSurface, $bg)
    $g.DrawPath($pBorder, $bg)
    $bg.Dispose()

    $bGold = New-Object System.Drawing.SolidBrush $GOLD
    $pGold = New-Object System.Drawing.Pen $GOLD_D, ([Math]::Max(2, [int](3 * $scale)))

    $cx = $size / 2.0
    $cy = $size / 2.0
    $bodyW = [int](120 * $scale)
    $bodyH = [int](80 * $scale)
    $bodyR = [int](20 * $scale)

    # Bonbon-Körper
    $body = New-RoundedPath ($cx - $bodyW / 2) ($cy - $bodyH / 2) $bodyW $bodyH $bodyR
    $g.FillPath($bGold, $body)
    $g.DrawPath($pGold, $body)
    $body.Dispose()

    # Zipfel links und rechts
    $tipW = [int](52 * $scale)
    $tipH = [int](64 * $scale)
    $left = @(
        (New-Object System.Drawing.PointF(($cx - $bodyW / 2), $cy)),
        (New-Object System.Drawing.PointF(($cx - $bodyW / 2 - $tipW), ($cy - $tipH / 2))),
        (New-Object System.Drawing.PointF(($cx - $bodyW / 2 - $tipW), ($cy + $tipH / 2)))
    )
    $right = @(
        (New-Object System.Drawing.PointF(($cx + $bodyW / 2), $cy)),
        (New-Object System.Drawing.PointF(($cx + $bodyW / 2 + $tipW), ($cy - $tipH / 2))),
        (New-Object System.Drawing.PointF(($cx + $bodyW / 2 + $tipW), ($cy + $tipH / 2)))
    )
    $g.FillPolygon($bGold, $left)
    $g.DrawPolygon($pGold, $left)
    $g.FillPolygon($bGold, $right)
    $g.DrawPolygon($pGold, $right)

    # Verpackungsfalten — unter 64px matschen sie und bleiben deshalb weg.
    if ($size -ge 64) {
        $pBand = New-Object System.Drawing.Pen $GOLD_D, ([Math]::Max(1, [int](2 * $scale)))
        $inset = [int](20 * $scale)
        $vPad  = [int](12 * $scale)
        $g.DrawLine($pBand, ($cx - $bodyW / 2 + $inset), ($cy - $bodyH / 2 + $vPad),
                            ($cx - $bodyW / 2 + $inset), ($cy + $bodyH / 2 - $vPad))
        $g.DrawLine($pBand, ($cx + $bodyW / 2 - $inset), ($cy - $bodyH / 2 + $vPad),
                            ($cx + $bodyW / 2 - $inset), ($cy + $bodyH / 2 - $vPad))
        $pBand.Dispose()
    }

    $pGold.Dispose(); $bGold.Dispose(); $pBorder.Dispose(); $bSurface.Dispose()
    $g.Dispose()
    return $bmp
}

function Save-Ico([System.Drawing.Bitmap[]]$images, [string]$path) {
    # System.Drawing kann keine Multi-Res-ICOs schreiben — deshalb das
    # ICO-Format von Hand. Eingebettete PNGs sind ab Windows Vista erlaubt
    # und sparen die BMP/AND-Mask-Fummelei.
    $blobs = foreach ($img in $images) {
        $ms = New-Object System.IO.MemoryStream
        $img.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        , $ms.ToArray()
    }

    $fs = [System.IO.File]::Create($path)
    $bw = New-Object System.IO.BinaryWriter $fs
    try {
        $bw.Write([uint16]0)                  # reserved
        $bw.Write([uint16]1)                  # type: 1 = Icon
        $bw.Write([uint16]$images.Count)

        # Directory-Einträge sind je 16 Byte; Bilddaten folgen dahinter.
        $offset = 6 + 16 * $images.Count
        for ($i = 0; $i -lt $images.Count; $i++) {
            $w = $images[$i].Width
            # 256 wird im ICO-Header als 0 kodiert.
            $bw.Write([byte]($(if ($w -ge 256) { 0 } else { $w })))
            $bw.Write([byte]($(if ($w -ge 256) { 0 } else { $w })))
            $bw.Write([byte]0)                # Farbanzahl (0 = truecolor)
            $bw.Write([byte]0)                # reserved
            $bw.Write([uint16]1)              # color planes
            $bw.Write([uint16]32)             # bits per pixel
            $bw.Write([uint32]$blobs[$i].Length)
            $bw.Write([uint32]$offset)
            $offset += $blobs[$i].Length
        }
        foreach ($blob in $blobs) { $bw.Write($blob) }
    }
    finally {
        $bw.Dispose(); $fs.Dispose()
    }
}

# Repo-relativ: <repo>/scripts/build_icon.ps1 -> <repo>/Nougat/Assets
$assets = Join-Path (Split-Path -Parent $PSScriptRoot) "$APP_NAME/Assets"
New-Item -ItemType Directory -Force -Path $assets | Out-Null
$pngPath = Join-Path $assets 'nougat.png'
$icoPath = Join-Path $assets 'nougat.ico'

$master = New-Icon 256
$master.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "Wrote $pngPath (256x256)"

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$icons = foreach ($s in $sizes) { New-Icon $s }
Save-Ico $icons $icoPath
Write-Host "Wrote $icoPath (multi-res: $($sizes -join ', '))"

foreach ($i in $icons) { $i.Dispose() }
$master.Dispose()
