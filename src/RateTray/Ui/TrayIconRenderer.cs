using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace RateTray.Ui;

/// <summary>
/// Draws a number straight into a tray icon, the way Core Temp shows per-core values.
/// Text is measured at a large font size and then scale-transformed to fill the icon, so
/// glyphs stay crisp at any DPI instead of snapping to a handful of point sizes.
/// </summary>
public static class TrayIconRenderer
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>Tray icons use the small-icon metric, which already accounts for DPI.</summary>
    public static int IconSize => Math.Max(16, SystemInformation.SmallIconSize.Width);

    public static Icon Render(string text, Color color, string fontFamily)
    {
        var size = IconSize;
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            // GDI+ DrawString (not TextRenderer) — only it composites onto a transparent
            // surface without leaving black fringes around the glyphs. Grid-fitting is off
            // because the glyphs are drawn through a scale transform, where hinting to the
            // untransformed pixel grid distorts the shapes.
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using var font = CreateFont(fontFamily);
            // Alignment stays Near: the transform below already centres the glyphs, and
            // combining both would offset the text by half its width and clip it.
            using var format = new StringFormat(StringFormat.GenericTypographic)
            {
                FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Near,
            };

            var measured = g.MeasureString(text, font, PointF.Empty, format);
            if (measured is { Width: > 0, Height: > 0 })
            {
                // One pixel of padding keeps antialiased edges from being clipped.
                var scale = Math.Min((size - 1f) / measured.Width, (size - 1f) / measured.Height);

                g.TranslateTransform(size / 2f, size / 2f);
                g.ScaleTransform(scale, scale);

                using var brush = new SolidBrush(color);
                g.DrawString(text, font, brush, new PointF(-measured.Width / 2f, -measured.Height / 2f), format);
            }
        }

        return ToIcon(bitmap);
    }

    private static Font CreateFont(string fontFamily)
    {
        // 64 px gives MeasureString enough resolution that the scale factor is accurate.
        try { return new Font(fontFamily, 64f, FontStyle.Bold, GraphicsUnit.Pixel); }
        catch (ArgumentException) { return new Font(FontFamily.GenericSansSerif, 64f, FontStyle.Bold, GraphicsUnit.Pixel); }
    }

    /// <summary>
    /// Icon.FromHandle does not own its handle, so the icon is cloned and the original
    /// HICON destroyed — otherwise every refresh would leak a GDI object.
    /// </summary>
    private static Icon ToIcon(Bitmap bitmap)
    {
        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    /// <summary>
    /// "auto" follows the Windows taskbar theme, which is what the icon actually sits on —
    /// note that this is SystemUsesLightTheme, not the app theme.
    /// </summary>
    public static bool UsesDarkTaskbar(string theme)
    {
        if (theme.Equals("dark", StringComparison.OrdinalIgnoreCase)) return true;
        if (theme.Equals("light", StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int light ? light == 0 : true;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return true;
        }
    }
}
