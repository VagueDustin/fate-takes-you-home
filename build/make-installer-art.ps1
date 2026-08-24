<#
.SYNOPSIS
    Draws the installer's banner and dialog bitmaps in the house style.

.DESCRIPTION
    WixUI takes two bitmaps: a 493x58 banner across the top of the inner pages, and a 493x312
    dialog background for the first and last pages. The stock ones are the red WiX wheel, which
    reads as "generic open-source installer" — the opposite of the impression an installer should
    give for a branded product.

    Drawn at build time rather than committed, so a change to the mark or the palette flows into
    the installer by rebuilding. Both images keep their text areas near-white because the MSI
    dialog text is black and cannot be themed without replacing the whole UI table set.

.PARAMETER OutDir
    Where banner.bmp and dialog.bmp are written.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutDir
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$glyphPath = Join-Path $repoRoot 'src/FateTakesYouHome/Assets/Icons/glyph-256.png'
$cinzelPath = Join-Path $repoRoot 'src/FateTakesYouHome/Assets/Fonts/Cinzel-Regular.ttf'

New-Item -ItemType Directory -Force $OutDir | Out-Null

# The FATE palette, from ThemeDefaults. These are the only colour literals allowed outside it,
# because this script cannot reference the assembly.
$navyDeep = [System.Drawing.Color]::FromArgb(255, 0x07, 0x0B, 0x1A)
$navyRaised = [System.Drawing.Color]::FromArgb(255, 0x10, 0x17, 0x36)
$gold = [System.Drawing.Color]::FromArgb(255, 0xD4, 0xAF, 0x37)
$goldBright = [System.Drawing.Color]::FromArgb(255, 0xF2, 0xC9, 0x4C)
$paper = [System.Drawing.Color]::FromArgb(255, 0xFB, 0xFA, 0xF7)

$fonts = New-Object System.Drawing.Text.PrivateFontCollection
$fonts.AddFontFile($cinzelPath)
$cinzel = $fonts.Families[0]

$glyph = [System.Drawing.Image]::FromFile($glyphPath)

# Deterministic stars: the same build produces the same bitmap.
$rand = New-Object System.Random(20260824)

function Draw-Stars {
    param($g, [int]$x, [int]$y, [int]$w, [int]$h, [int]$count)

    for ($i = 0; $i -lt $count; $i++) {
        $sx = $x + $rand.Next($w)
        $sy = $y + $rand.Next($h)
        $size = if ($rand.NextDouble() -lt 0.12) { 2 } else { 1 }
        $isGold = $rand.NextDouble() -lt 0.2
        $alpha = 60 + $rand.Next(150)

        $colour = if ($isGold) {
            [System.Drawing.Color]::FromArgb($alpha, $goldBright)
        } else {
            [System.Drawing.Color]::FromArgb($alpha, 0xE6, 0xEA, 0xF2)
        }

        $brush = New-Object System.Drawing.SolidBrush($colour)
        $g.FillEllipse($brush, $sx, $sy, $size, $size)
        $brush.Dispose()
    }
}

function New-Canvas {
    param([int]$w, [int]$h)

    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    return $bmp, $g
}

# ---------------------------------------------------------------- dialog (493x312)

$bmp, $g = New-Canvas 493 312

# Text area first: near-white, because the MSI's own text is black.
$paperBrush = New-Object System.Drawing.SolidBrush($paper)
$g.FillRectangle($paperBrush, 0, 0, 493, 312)

# The night-sky strip down the left.
$stripWidth = 164
$gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    (New-Object System.Drawing.Rectangle(0, 0, $stripWidth, 312)), $navyDeep, $navyRaised, 90.0)
$g.FillRectangle($gradient, 0, 0, $stripWidth, 312)
Draw-Stars $g 0 0 $stripWidth 312 110

# The gold arch, centred in the strip's upper half.
$markSize = 72
$g.DrawImage($glyph, [int](($stripWidth - $markSize) / 2), 78, $markSize, $markSize)

# The wordmark, letterspaced by hand because GDI+ has no tracking.
$wordFont = New-Object System.Drawing.Font($cinzel, 13, [System.Drawing.FontStyle]::Regular)
$goldBrush = New-Object System.Drawing.SolidBrush($goldBright)
$word = 'FATE'
$letterWidths = $word.ToCharArray() | ForEach-Object { $g.MeasureString([string]$_, $wordFont).Width }
$tracking = 8
$total = ($letterWidths | Measure-Object -Sum).Sum + $tracking * ($word.Length - 1) - 10
$wx = ($stripWidth - $total) / 2
foreach ($i in 0..($word.Length - 1)) {
    $g.DrawString([string]$word[$i], $wordFont, $goldBrush, [float]$wx, 168.0)
    $wx += $letterWidths[$i] + $tracking - 2.5
}

$subFont = New-Object System.Drawing.Font($cinzel, 6.6, [System.Drawing.FontStyle]::Regular)
$subBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0x94, 0xA0, 0xBB))
$sub = 'TAKES YOU HOME'
$subSize = $g.MeasureString($sub, $subFont)
$g.DrawString($sub, $subFont, $subBrush, [float](($stripWidth - $subSize.Width) / 2), 196.0)

# A hairline of gold where the sky meets the page.
$goldPen = New-Object System.Drawing.Pen($gold, 1)
$g.DrawLine($goldPen, $stripWidth, 0, $stripWidth, 312)

$g.Dispose()
$bmp.Save((Join-Path $OutDir 'dialog.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$bmp.Dispose()

# ---------------------------------------------------------------- banner (493x58)

$bmp, $g = New-Canvas 493 58

$paperBrush2 = New-Object System.Drawing.SolidBrush($paper)
$g.FillRectangle($paperBrush2, 0, 0, 493, 58)

# A small piece of the night sky on the right, carrying the mark.
$tile = New-Object System.Drawing.Rectangle(423, 0, 70, 58)
$gradient2 = New-Object System.Drawing.Drawing2D.LinearGradientBrush($tile, $navyDeep, $navyRaised, 90.0)
$g.FillRectangle($gradient2, $tile)
Draw-Stars $g 423 0 70 58 26
$g.DrawImage($glyph, 440, 11, 36, 36)
$g.DrawLine($goldPen, 423, 0, 423, 58)

# And a gold rule along the bottom so the banner reads as the same product as the dialog.
$g.DrawLine($goldPen, 0, 57, 493, 57)

$g.Dispose()
$bmp.Save((Join-Path $OutDir 'banner.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$bmp.Dispose()

$glyph.Dispose()

Write-Host "    wrote dialog.bmp and banner.bmp to $OutDir"
