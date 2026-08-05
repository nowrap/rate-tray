<#
.SYNOPSIS
    Regenerates docs/details.png and docs/details.de.png.

.DESCRIPTION
    The details window is rendered from invented readings rather than captured from a running
    instance. A live capture would publish the account's subscription tiers, its usage at that
    moment and its sign-in timestamps — none of which belongs in a public repository.

    The values are chosen to show the colour system in one picture: two limits below the warning
    threshold in the service colour (shaded apart), one above it in amber, and the second
    service in its own colour.

    PrintWindow is used instead of a screen grab so the window does not have to be unobstructed,
    and so nothing on the desktop leaks into the image.

.EXAMPLE
    pwsh tools/New-Screenshots.ps1
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\docs')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing, System.Windows.Forms

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class WindowCapture
{
    public delegate bool EnumProc(IntPtr handle, IntPtr param);

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr param);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle, out Rect rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr handle, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out Rect value, int size);

    public const int ExtendedFrameBounds = 9;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }

    /// <summary>
    /// Window rectangle minus the invisible resize border a framed window carries on Windows 10
    /// and 11. GetWindowRect includes it, PrintWindow does not paint it, so capturing the raw
    /// rectangle leaves black bars down both sides.
    /// </summary>
    public static Rect VisibleBounds(IntPtr handle)
    {
        Rect outer;
        GetWindowRect(handle, out outer);

        Rect visible;
        if (DwmGetWindowAttribute(handle, ExtendedFrameBounds, out visible, Marshal.SizeOf(typeof(Rect))) != 0)
            return outer;

        return visible;
    }
}
'@

[void][WindowCapture]::SetProcessDPIAware()

$assembly = Join-Path $PSScriptRoot "..\src\RateTray\bin\$Configuration\net9.0-windows\RateTray.dll"
if (-not (Test-Path $assembly)) { throw "Build first: dotnet build -c $Configuration" }
[void][System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes((Resolve-Path $assembly)))

$docs = [System.IO.Path]::GetFullPath($OutputDirectory)
$null = New-Item -ItemType Directory -Force -Path $docs

function New-Reading($id, $label, $group, $percent, $resetsAt, $window, $active, $variant, $variantCount) {
    $r = New-Object RateTray.Model.LimitReading
    $r.Id = $id; $r.Label = $label; $r.Group = $group
    $r.Percent = [double]$percent
    $r.ResetsAt = [DateTimeOffset]$resetsAt
    $r.Window = [TimeSpan]$window
    $r.IsActive = [bool]$active
    $r.Variant = [int]$variant
    $r.VariantCount = [int]$variantCount
    $r
}

function New-Auth($group, $expiresAt, $detail) {
    $a = New-Object RateTray.Model.AuthStatus
    $a.Group = $group; $a.IsValid = $true
    $a.ExpiresAt = [DateTimeOffset]$expiresAt
    $a.Detail = $detail
    $a
}

function New-Result($group, $readings, $auth) {
    $list = New-Object "System.Collections.Generic.List[RateTray.Model.LimitReading]"
    foreach ($r in $readings) { $list.Add($r) }
    $result = [RateTray.Model.ProviderResult]::Success($group, $list)
    $result.Auth = $auth
    $result
}

function Get-FrameCrop($form, $outer, $visible) {
    # Crop to the client area's left and right edges, and down to its bottom, keeping the title
    # bar above it. Derived from the window's own geometry rather than by inspecting pixels:
    # both side edges then come from the same rectangle, so the result cannot end up lopsided
    # the way trimming border colours did — the frame line survives on the right but not the
    # left, which reads as a stray pixel column.
    $client = $form.RectangleToScreen($form.ClientRectangle)

    $x = $client.Left - $outer.Left
    $y = $visible.Top - $outer.Top
    New-Object System.Drawing.Rectangle $x, $y, $client.Width, (($client.Bottom - $outer.Top) - $y)
}

function Save-Window($form, $fileName) {
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 900

    $outer = New-Object WindowCapture+Rect
    [void][WindowCapture]::GetWindowRect($form.Handle, [ref]$outer)
    $visible = [WindowCapture]::VisibleBounds($form.Handle)

    # PrintWindow always draws into the full window rectangle, so render that and then crop
    # away the invisible border a framed window carries.
    $full = New-Object System.Drawing.Bitmap -ArgumentList ([int]($outer.Right - $outer.Left)), ([int]($outer.Bottom - $outer.Top))
    $graphics = [System.Drawing.Graphics]::FromImage($full)
    $hdc = $graphics.GetHdc()
    [void][WindowCapture]::PrintWindow($form.Handle, $hdc, 2)   # PW_RENDERFULLCONTENT
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()

    $crop = if ($form.FormBorderStyle -eq [System.Windows.Forms.FormBorderStyle]::None) {
        # Borderless: the fly-out draws its own edge and the window rectangle is exactly it.
        New-Object System.Drawing.Rectangle(
            [int]($visible.Left - $outer.Left), [int]($visible.Top - $outer.Top),
            [int]($visible.Right - $visible.Left), [int]($visible.Bottom - $visible.Top))
    } else {
        Get-FrameCrop $form $outer $visible
    }

    $bitmap = $full.Clone($crop, $full.PixelFormat)

    $path = Join-Path $docs $fileName
    $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose(); $full.Dispose()

    Write-Host "Wrote $path ($($crop.Width)x$($crop.Height))"
}

function New-SampleResults($language) {
    [RateTray.Localization.Loc]::Use($language)
    $L = [RateTray.Localization.Loc]
    $now = [DateTimeOffset]::Now

    # Invented, but shaped like a real account: three Claude windows and one Codex window.
    $claude = @(
        (New-Reading 'claude.session' $L::T('label.claude.session') 'Claude' 34 $now.AddHours(2.4) ([TimeSpan]::FromHours(5)) $false 0 3),
        (New-Reading 'claude.weekly_all' $L::T('label.claude.weeklyAll') 'Claude' 78 $now.AddDays(4.2) ([TimeSpan]::FromDays(7)) $true 1 3),
        (New-Reading 'claude.weekly_scoped.fable' $L::T('label.claude.weeklyScoped', @('Fable')) 'Claude' 22 $now.AddDays(4.2) ([TimeSpan]::FromDays(7)) $false 2 3)
    )
    $codex = @(
        (New-Reading 'codex.primary' $L::T('label.codex.window', @($L::T('window.week'))) 'Codex' 61 $now.AddDays(5.3) ([TimeSpan]::FromDays(7)) $true 0 1)
    )

    $results = New-Object "System.Collections.Generic.List[RateTray.Model.ProviderResult]"
    $results.Add((New-Result 'Claude' $claude (New-Auth 'Claude' $now.AddHours(3.6) 'OAuth')))
    $results.Add((New-Result 'Codex' $codex (New-Auth 'Codex' $now.AddDays(9.1) 'chatgpt')))

    # Leading comma: without it PowerShell unrolls the list into an Object[] on return, and the
    # typed IReadOnlyList parameter no longer binds.
    , $results
}

function Save-Details($language, $fileName) {
    $results = New-SampleResults $language
    $now = [DateTimeOffset]::Now

    $config = New-Object RateTray.Configuration.AppConfig
    $palette = New-Object RateTray.Ui.Palette($config)
    $form = New-Object RateTray.Ui.DetailsForm($config, $palette)
    $form.AutoHide = $false
    $form.ShowNearTray($results, $now, $now.AddSeconds($config.RefreshSeconds * 0.45))

    Save-Window $form $fileName
    $form.Dispose()
}

function Save-Settings($language, $fileName, $tabIndex) {
    $results = New-SampleResults $language

    $known = New-Object "System.Collections.Generic.List[RateTray.Model.LimitReading]"
    foreach ($result in $results) { foreach ($reading in $result.Readings) { $known.Add($reading) } }

    $config = New-Object RateTray.Configuration.AppConfig
    # Explicit rather than "auto", which would render as "Automatic (Deutsch)" in the English
    # screenshot on a German machine.
    $config.Language = $language
    foreach ($reading in $known) { $config.Icons.Add($reading.Id) }

    $form = New-Object RateTray.Ui.SettingsForm($config, $known)
    $form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $form.Show()
    [System.Windows.Forms.Application]::DoEvents()

    # The dialog owns its TabControl, so the tab is selected directly rather than by poking
    # the native control from outside, which changes the header but not the page.
    foreach ($control in $form.Controls) {
        if ($control -is [System.Windows.Forms.TabControl]) { $control.SelectedIndex = $tabIndex }
    }
    [System.Windows.Forms.Application]::DoEvents()

    Save-Window $form $fileName
    $form.Dispose()
}

Save-Details 'en' 'details.png'
Save-Details 'de' 'details.de.png'

Save-Settings 'en' 'settings.png' 0
Save-Settings 'de' 'settings.de.png' 0
Save-Settings 'en' 'settings-colors.png' 1
Save-Settings 'de' 'settings-colors.de.png' 1
