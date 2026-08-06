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
    /// <summary>How much of the icon the number covers, leaving a margin so it reads as ambient
    /// rather than filling the icon edge to edge (which looked too dominant next to other icons).</summary>
    private const float Coverage = 0.76f;

    /// <summary>
    /// Optical lift, as a fraction of the icon, above the geometric centre. A number centred by
    /// pixels still reads a touch low next to Core Temp, which sits its digits slightly high; this
    /// nudges ours up to match. Tuned by eye — bump it if the numbers still sit low.
    /// </summary>
    private const float OpticalRise = 0.04f;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>Tray icons use the small-icon metric, which already accounts for DPI.</summary>
    public static int IconSize => Math.Max(16, SystemInformation.SmallIconSize.Width);

    public static Icon Render(string text, Color color, string fontFamily)
    {
        var size = IconSize;
        using var font = CreateFont(fontFamily);
        // Alignment stays Near: the transform below already centres the glyphs, and combining it
        // with StringAlignment.Center would offset the text by half its width and clip it.
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Near,
        };

        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            // GDI+ DrawString (not TextRenderer) — only it composites onto a transparent surface
            // without leaving black fringes around the glyphs. Grid-fitting is off because the
            // glyphs are drawn through a scale transform, where hinting to the untransformed pixel
            // grid distorts the shapes.
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var reference = MeasureInk("88", font, format);
            var ink = MeasureInk(text, font, format);
            if (reference is { Width: > 0, Height: > 0 } && ink is { Width: > 0, Height: > 0 })
            {
                // Scale UNIFORMLY — stretching the glyphs is exactly what looked wrong — from the
                // constant "88" reference, so a one- and a two-digit value come out at the same size
                // and baseline (Core Temp style) instead of each fitting its own box. Only a wider
                // value like "100" is shrunk further so it cannot overflow the icon.
                var scale = Math.Min(size * Coverage / reference.Width, size * Coverage / reference.Height);
                scale = Math.Min(scale, (size - 1f) / ink.Width);

                // Horizontal pen centres each value on its own ink; vertical on the reference ink so
                // every value shares one baseline. This is only a starting point, though.
                var pen = new PointF(-(ink.X + ink.Width / 2f), -(reference.Y + reference.Height / 2f));

                // A single pass lands a pixel or two off centre — antialiasing plus the fractional
                // scale — which is glaring when a row of icons should share a baseline. So render
                // once to a scratch, measure where the ink truly falls, and shift the real draw by
                // that residual: the number then sits dead centre, to the pixel.
                var nudge = CenteringNudge(text, font, format, size, scale, pen);

                g.TranslateTransform(size / 2f + nudge.X, size / 2f + nudge.Y - size * OpticalRise);
                g.ScaleTransform(scale, scale);

                using var brush = new SolidBrush(color);
                g.DrawString(text, font, brush, pen, format);
            }
        }

        return ToIcon(bitmap);
    }

    /// <summary>
    /// Device-pixel shift that moves the actually-rendered ink to the exact centre of the icon.
    /// Measured from a scratch render because antialiasing and the fractional scale leave a
    /// single-pass draw a pixel or two off — visible when a row of icons should share a baseline.
    /// </summary>
    private static PointF CenteringNudge(string text, Font font, StringFormat format, int size, float scale, PointF pen)
    {
        using var scratch = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(scratch))
        {
            g.Clear(Color.Transparent);
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(size / 2f, size / 2f);
            g.ScaleTransform(scale, scale);
            using var brush = new SolidBrush(Color.White);      // colour is irrelevant to the bounds
            g.DrawString(text, font, brush, pen, format);
        }

        var placed = InkBounds(scratch, 0, 0);
        return placed.IsEmpty
            ? PointF.Empty
            : new PointF(size / 2f - (placed.X + placed.Width / 2f), size / 2f - (placed.Y + placed.Height / 2f));
    }

    /// <summary>
    /// Tight bounding box of the pixels <see cref="Graphics.DrawString"/> paints for
    /// <paramref name="text"/>, relative to the draw origin. GDI+ exposes no ink-extent query, so
    /// the text is drawn to a scratch surface and scanned for its painted pixels.
    /// </summary>
    private static RectangleF MeasureInk(string text, Font font, StringFormat format)
    {
        const int margin = 4;
        var box = (int)Math.Ceiling(font.Size) * 5 + 2 * margin;

        using var scratch = new Bitmap(box, box, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(scratch))
        {
            g.Clear(Color.Transparent);
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Color.White);
            g.DrawString(text, font, brush, new PointF(margin, margin), format);
        }

        return InkBounds(scratch, margin, margin);
    }

    /// <summary>
    /// Tight bounds of the painted pixels in <paramref name="bmp"/>, offset back to the draw origin
    /// (<paramref name="originX"/>, <paramref name="originY"/>); empty if nothing was painted. A
    /// faint-alpha floor keeps a glyph's antialiased fringe from bloating the box differently from
    /// one shape to the next, which would drift the measured centre.
    /// </summary>
    private static RectangleF InkBounds(Bitmap bmp, int originX, int originY)
    {
        const byte floor = 24;
        var w = bmp.Width;
        var h = bmp.Height;

        var bits = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var pixels = new byte[bits.Stride * h];
        Marshal.Copy(bits.Scan0, pixels, 0, pixels.Length);
        bmp.UnlockBits(bits);

        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (var y = 0; y < h; y++)
        {
            var row = y * bits.Stride;
            for (var x = 0; x < w; x++)
            {
                if (pixels[row + x * 4 + 3] < floor) continue;      // transparent or faint fringe
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        return maxX < minX
            ? RectangleF.Empty
            : RectangleF.FromLTRB(minX - originX, minY - originY, maxX - originX + 1, maxY - originY + 1);
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
