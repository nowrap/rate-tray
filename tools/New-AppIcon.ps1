<#
.SYNOPSIS
    Generates src/RateTray/app.ico.

.DESCRIPTION
    The icon is drawn in code rather than authored in an editor so it stays in step with the
    palette: change the service colours here and every resolution is regenerated consistently.

    The motif is two level bars in the two service colours — what the app actually measures —
    on a dark rounded plate. Deliberately generic geometry: the Anthropic and OpenAI logos are
    trademarks and cannot ship in this repository.

    Detail is dropped at small sizes: the limit line only appears from 48 px up, where it reads
    as a line instead of as noise.

.EXAMPLE
    pwsh tools/New-AppIcon.ps1
#>
[CmdletBinding()]
param(
    [string] $ClaudeColor = '#E08A64',   # CI terracotta, lifted for contrast on the dark plate
    [string] $CodexColor  = '#16C08F',   # CI green, lifted likewise
    [string] $PlateColor  = '#23262C',
    [string] $TrackColor  = '#363B44',   # drained track behind each fill
    [string] $OutputPath  = (Join-Path $PSScriptRoot '..\src\RateTray\app.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Windows picks the nearest of these for the taskbar, alt-tab, Explorer and the shortcut.
$sizes = 16, 20, 24, 32, 48, 64, 128, 256

function ConvertTo-Color([string] $hex) {
    $h = $hex.TrimStart('#')
    [System.Drawing.Color]::FromArgb(255,
        [Convert]::ToInt32($h.Substring(0, 2), 16),
        [Convert]::ToInt32($h.Substring(2, 2), 16),
        [Convert]::ToInt32($h.Substring(4, 2), 16))
}

function New-RoundedPath([single] $x, [single] $y, [single] $w, [single] $h, [single] $radius) {
    $radius = [Math]::Min($radius, [Math]::Min($w, $h) / 2)
    $d = $radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($d -le 0) { $path.AddRectangle((New-Object System.Drawing.RectangleF($x, $y, $w, $h))); return $path }
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $path
}

function New-IconBitmap([int] $size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)

        # Plate. A hair of inset keeps the antialiased corners off the icon boundary.
        $inset = [single]([Math]::Max(0.5, $size * 0.02))
        $plate = New-RoundedPath $inset $inset ($size - 2 * $inset) ($size - 2 * $inset) ([single]($size * 0.22))
        $plateBrush = New-Object System.Drawing.SolidBrush(ConvertTo-Color $PlateColor)
        $g.FillPath($plateBrush, $plate)
        $plateBrush.Dispose(); $plate.Dispose()

        # Two meters: a drained track with a coloured fill rising from the baseline — the same
        # figure the details window draws, rotated upright. The fills stay clearly taller than
        # they are wide at every size, otherwise the shorter one rounds into a dot.
        $top      = [single]($size * 0.19)
        $baseline = [single]($size * 0.81)
        $barWidth = [single]($size * 0.205)
        $gap      = [single]($size * 0.09)
        $left     = [single](($size - (2 * $barWidth + $gap)) / 2)
        $radius   = [single]($barWidth / 2)

        # Both fills stay well above the rounded cap height, or at 16 px the shorter one
        # collapses into a dot and the meter reads as a single bar plus a speck.
        $bars = @(
            @{ X = $left;                    Fill = [single](($baseline - $top) * 0.86); Color = $ClaudeColor },
            @{ X = $left + $barWidth + $gap; Fill = [single](($baseline - $top) * 0.62); Color = $CodexColor  }
        )

        $trackBrush = New-Object System.Drawing.SolidBrush(ConvertTo-Color $TrackColor)
        foreach ($bar in $bars) {
            $track = New-RoundedPath $bar.X $top $barWidth ($baseline - $top) $radius
            $g.FillPath($trackBrush, $track)
            $track.Dispose()

            $fill = New-RoundedPath $bar.X ($baseline - $bar.Fill) $barWidth $bar.Fill $radius
            $brush = New-Object System.Drawing.SolidBrush(ConvertTo-Color $bar.Color)
            $g.FillPath($brush, $fill)
            $brush.Dispose(); $fill.Dispose()
        }
        $trackBrush.Dispose()
    }
    finally {
        $g.Dispose()
    }

    $bmp
}

# --- assemble the .ico ------------------------------------------------------
# ICONDIR, then one ICONDIRENTRY per size, then the PNG payloads. PNG-compressed
# entries are understood by Windows Vista and later and keep the file small.

$payloads = foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    $stream = New-Object System.IO.MemoryStream
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
    $stream.Dispose()
}

$resolved = [System.IO.Path]::GetFullPath($OutputPath)
$null = New-Item -ItemType Directory -Force -Path ([System.IO.Path]::GetDirectoryName($resolved))

$file = [System.IO.File]::Create($resolved)
$writer = New-Object System.IO.BinaryWriter($file)
try {
    $writer.Write([uint16]0)                      # reserved
    $writer.Write([uint16]1)                      # type: icon
    $writer.Write([uint16]$payloads.Count)

    $offset = 6 + 16 * $payloads.Count
    foreach ($entry in $payloads) {
        # 0 encodes 256 in a single byte.
        $writer.Write([byte]($entry.Size % 256))
        $writer.Write([byte]($entry.Size % 256))
        $writer.Write([byte]0)                    # palette size: none
        $writer.Write([byte]0)                    # reserved
        $writer.Write([uint16]1)                  # colour planes
        $writer.Write([uint16]32)                 # bits per pixel
        $writer.Write([uint32]$entry.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $entry.Bytes.Length
    }

    foreach ($entry in $payloads) { $writer.Write($entry.Bytes) }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

Write-Host "Wrote $resolved ($([Math]::Round((Get-Item $resolved).Length / 1KB, 1)) KB, sizes: $($sizes -join ', '))"
