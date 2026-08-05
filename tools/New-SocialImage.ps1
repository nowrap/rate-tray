<#
.SYNOPSIS
    Generates docs/social.png (1200x630) and docs/apple-touch-icon.png (180x180).

.DESCRIPTION
    The share card is drawn with the app's own Palette, so the colours are the ones the tray
    actually uses rather than a designer's approximation of them — including the shading between
    limits of one service and the amber that takes over above the warning threshold.

    The numbers are drawn far larger than the 24 px the app renders at: a link preview is shown
    at a few hundred pixels wide, and a faithful 24 px specimen would be an unreadable smudge.
    TrayIconRenderer is therefore not reused here; only its colour source is.

    docs/social.png doubles as the GitHub social preview, which has no API and has to be
    uploaded once under Settings, General, Social preview.

.EXAMPLE
    pwsh tools/New-SocialImage.ps1
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\docs')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$assembly = Join-Path $PSScriptRoot "..\src\RateTray\bin\$Configuration\net9.0-windows\RateTray.dll"
if (-not (Test-Path $assembly)) { throw "Build first: dotnet build -c $Configuration" }
[void][System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes((Resolve-Path $assembly)))

$docs = [System.IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Force -Path $docs

function ConvertTo-Color([System.Drawing.Color] $c) { $c }

# ---------------------------------------------------------------- share card --

$config = New-Object RateTray.Configuration.AppConfig
$palette = New-Object RateTray.Ui.Palette($config)

$width = 1200; $height = 630
$card = New-Object System.Drawing.Bitmap -ArgumentList $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($card)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

# Page ground, so the card and the site are recognisably the same thing.
$ground = [System.Drawing.Color]::FromArgb(255, 20, 22, 26)
$g.Clear($ground)

$left = 80
$family = 'Segoe UI'

# Wordmark, in the site's monospace annotation register.
$mono = New-Object System.Drawing.Font('Consolas', 26, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$dim = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 139, 146, 158))
$fmt = New-Object System.Drawing.StringFormat
$g.DrawString('R A T E T R A Y', $mono, $dim, [single]$left, [single]72, $fmt)

# Headline, wrapped by hand so the line breaks land where they read best.
$titleFont = New-Object System.Drawing.Font($family, 62, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$text = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 236, 238, 242))
$lines = @('Live usage limits for', 'Claude Code and Codex,', 'in the Windows tray.')
$y = 140
foreach ($line in $lines) {
    $g.DrawString($line, $titleFont, $text, [single]$left, [single]$y, $fmt)
    $y += 74
}

# The tray strip, at poster scale. Same values and variants as the site's hero, so the
# shading between the three Claude limits and the amber above the threshold both show.
$samples = @(
    @{ Value = 34; Group = 'Claude'; Variant = 0; Count = 3 },
    @{ Value = 81; Group = 'Claude'; Variant = 1; Count = 3 },
    @{ Value = 22; Group = 'Claude'; Variant = 2; Count = 3 },
    @{ Value = 61; Group = 'Codex';  Variant = 0; Count = 1 }
)

$stripTop = 430
$stripHeight = 116
$strip = New-Object System.Drawing.Drawing2D.GraphicsPath
$r = 14
$stripLeft = $left; $stripRight = $width - $left
$strip.AddArc($stripLeft, $stripTop, $r*2, $r*2, 180, 90)
$strip.AddArc($stripRight - $r*2, $stripTop, $r*2, $r*2, 270, 90)
$strip.AddArc($stripRight - $r*2, $stripTop + $stripHeight - $r*2, $r*2, $r*2, 0, 90)
$strip.AddArc($stripLeft, $stripTop + $stripHeight - $r*2, $r*2, $r*2, 90, 90)
$strip.CloseFigure()
$taskbar = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 31, 32, 35))
$g.FillPath($taskbar, $strip)

$numberFont = New-Object System.Drawing.Font($family, 60, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
$x = $stripLeft + 44
foreach ($s in $samples) {
    $colour = $palette.ForReading($s.Group, [double]$s.Value, [int]$s.Variant, [int]$s.Count, $true)
    $brush = New-Object System.Drawing.SolidBrush($colour)
    $label = [string]$s.Value
    $size = $g.MeasureString($label, $numberFont)
    $g.DrawString($label, $numberFont, $brush, [single]$x, [single]($stripTop + ($stripHeight - $size.Height) / 2), $fmt)
    $brush.Dispose()
    $x += $size.Width + 46
}

# Domain, bottom right of the strip, so the card names where it came from.
$domainFont = New-Object System.Drawing.Font('Consolas', 24, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$domain = 'ratetray.nowrap.net'
$domainSize = $g.MeasureString($domain, $domainFont)
$g.DrawString($domain, $domainFont, $dim, [single]($stripRight - $domainSize.Width - 40), [single]($stripTop + ($stripHeight - $domainSize.Height) / 2), $fmt)

$g.Dispose()
$socialPath = Join-Path $docs 'social.png'
$card.Save($socialPath, [System.Drawing.Imaging.ImageFormat]::Png)
$card.Dispose()
Write-Host "Wrote $socialPath (${width}x${height})"

# ------------------------------------------------------------ touch icon --
# iOS ignores .ico, so the home-screen icon needs a PNG of its own.

$icoPath = Join-Path $PSScriptRoot '..\src\RateTray\app.ico'
$icon = New-Object System.Drawing.Icon -ArgumentList (Resolve-Path $icoPath).Path, 256, 256
$source = $icon.ToBitmap()

$touch = New-Object System.Drawing.Bitmap -ArgumentList 180, 180, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$tg = [System.Drawing.Graphics]::FromImage($touch)
$tg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$tg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$tg.DrawImage($source, 0, 0, 180, 180)
$tg.Dispose()

$touchPath = Join-Path $docs 'apple-touch-icon.png'
$touch.Save($touchPath, [System.Drawing.Imaging.ImageFormat]::Png)
$touch.Dispose(); $source.Dispose(); $icon.Dispose()
Write-Host "Wrote $touchPath (180x180)"
