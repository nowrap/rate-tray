using System.Drawing.Drawing2D;
using RateTray.Configuration;
using RateTray.Model;

namespace RateTray.Ui;

/// <summary>
/// Hover card shown next to a tray icon.
///
/// Windows tray tooltips are plain text — <c>NOTIFYICONDATA.szTip</c> holds no image and is
/// capped at 63 characters by WinForms — so showing the service mark next to the value means
/// drawing our own popup. It never takes focus (WS_EX_NOACTIVATE), otherwise hovering a tray
/// icon would deactivate whatever the user is working in.
/// </summary>
public sealed class TooltipWindow : Form
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExTopMost = 0x00000008;
    private const int WsExTransparent = 0x00000020;

    private readonly AppConfig _config;
    private readonly Palette _palette;

    private LimitReading? _reading;
    private string _group = "";
    private string? _error;
    private int _dpi = 96;

    private Font _labelFont = null!;
    private Font _valueFont = null!;
    private Font _smallFont = null!;

    public TooltipWindow(AppConfig config, Palette palette)
    {
        _config = config;
        _palette = palette;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        Enabled = false;                 // purely decorative: never accepts input

        BuildFonts();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExNoActivate | WsExToolWindow | WsExTopMost | WsExTransparent;
            return cp;
        }
    }

    private bool Dark => TrayIconRenderer.UsesDarkTaskbar(_config.Theme);
    private Color Background => Dark ? Color.FromArgb(38, 40, 46) : Color.FromArgb(252, 252, 253);
    private Color Foreground => Dark ? Color.FromArgb(236, 238, 242) : Color.FromArgb(26, 28, 32);
    private Color Muted => Dark ? Color.FromArgb(154, 160, 170) : Color.FromArgb(107, 114, 128);
    private Color BorderColor => Dark ? Color.FromArgb(66, 70, 79) : Color.FromArgb(210, 215, 224);

    private int Px(int value) => (int)Math.Round(value * (_dpi / 96.0));

    public void ShowFor(LimitReading? reading, string group, string? error, Point cursor)
    {
        _reading = reading;
        _group = group;
        _error = error;

        _dpi = Native.DpiForPoint(cursor);
        BuildFonts();

        var screen = Screen.FromPoint(cursor);
        var work = screen.WorkingArea;
        var size = Measure();
        var gap = Px(6);
        var pad = Px(4);

        var taskbar = Native.TaskbarRect();
        int x, y;

        // A tray icon is hovered either on the taskbar itself or inside the overflow flyout that
        // pops up from the chevron. On the taskbar we anchor to its edge so the card sits at a
        // steady height like the details fly-out; in the flyout the icon is nowhere near that
        // edge, so there we follow the pointer — otherwise the card detaches from the icon.
        if (taskbar is { } bar && bar.Contains(cursor))
        {
            switch (FlyoutPlacement.EdgeOf(bar, screen.Bounds))
            {
                case FlyoutPlacement.Edge.Top:
                    x = cursor.X - size.Width / 2; y = work.Top + gap; break;
                case FlyoutPlacement.Edge.Left:
                    x = work.Left + gap; y = cursor.Y - size.Height / 2; break;
                case FlyoutPlacement.Edge.Right:
                    x = work.Right - size.Width - gap; y = cursor.Y - size.Height / 2; break;
                default:
                    x = cursor.X - size.Width / 2; y = work.Bottom - size.Height - gap; break;
            }
        }
        else if (Native.WindowRectAt(cursor) is { } flyout && flyout.Contains(cursor))
        {
            // Overflow flyout (the "^" popup): sit just above the whole flyout, LEFT-aligned to its
            // visible edge — the way the shell's own tooltips appear there — never covering it. The
            // reported window left sits a little outside the visible rounded box (corner margin), so
            // nudge right to line the card up with the box edge.
            y = flyout.Top - size.Height - gap;
            x = flyout.Left + Px(11);
        }
        else
        {
            // Fallback (flyout rect unavailable): sit just above the pointer, dropping below it
            // only when there is no room above.
            x = cursor.X - size.Width / 2;
            y = cursor.Y - size.Height - Px(14);
            if (y < work.Top + pad) y = cursor.Y + Px(20);
        }

        // Clamped so the card never leaves the work area.
        x = Math.Clamp(x, work.Left + pad, Math.Max(work.Left + pad, work.Right - size.Width - pad));
        y = Math.Clamp(y, work.Top + pad, Math.Max(work.Top + pad, work.Bottom - size.Height - pad));

        Bounds = new Rectangle(x, y, size.Width, size.Height);

        if (!Visible) Show();
        Invalidate();
    }

    private void BuildFonts()
    {
        var family = SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif;

        _labelFont?.Dispose();
        _valueFont?.Dispose();
        _smallFont?.Dispose();

        _labelFont = new Font(family, Px(13), FontStyle.Regular, GraphicsUnit.Pixel);
        _valueFont = new Font(family, Px(15), FontStyle.Bold, GraphicsUnit.Pixel);
        _smallFont = new Font(family, Px(11), FontStyle.Regular, GraphicsUnit.Pixel);
    }

    private Size Measure()
    {
        using var g = CreateGraphics();

        var head = _reading?.Label ?? _group;
        var value = _reading is { } r ? $"{Math.Round(r.Percent)} %" : "?";
        var detail = _error ?? _reading?.ResetText() ?? "";

        var headWidth = g.MeasureString(head, _labelFont).Width + g.MeasureString(value, _valueFont).Width + Px(16);
        var detailWidth = string.IsNullOrEmpty(detail) ? 0 : g.MeasureString(detail, _smallFont).Width;

        var width = (int)Math.Ceiling(Math.Max(headWidth, detailWidth)) + Px(20) + Px(22);
        var height = Px(string.IsNullOrEmpty(detail) ? 34 : 52);

        return new Size(Math.Clamp(width, Px(160), Px(460)), height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var back = new SolidBrush(Background)) g.FillRectangle(back, ClientRectangle);
        using (var border = new Pen(BorderColor)) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

        var accent = Harmony.Legible(_palette.Service(_group), Dark);
        var pad = Px(10);

        ServiceBadge.Draw(g, new RectangleF(pad, Px(9), Px(16), Px(16)), _group, accent);

        var textLeft = pad + Px(22);
        var head = _reading?.Label ?? _group;

        using (var brush = new SolidBrush(Foreground))
            g.DrawString(head, _labelFont, brush, textLeft, Px(9));

        if (_reading is { } reading)
        {
            var value = $"{Math.Round(reading.Percent)} %";
            var color = _palette.ForReading(reading.Group, reading.Percent, reading.Variant, reading.VariantCount, Dark);
            var width = g.MeasureString(value, _valueFont).Width;

            using var brush = new SolidBrush(color);
            g.DrawString(value, _valueFont, brush, Width - pad - width, Px(7));
        }

        var detail = _error ?? _reading?.ResetText();
        if (string.IsNullOrEmpty(detail)) return;

        using var detailBrush = new SolidBrush(_error is null ? Muted : Harmony.Legible(_palette.Critical, Dark));
        g.DrawString(detail, _smallFont, detailBrush, textLeft, Px(30));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _labelFont?.Dispose();
            _valueFont?.Dispose();
            _smallFont?.Dispose();
        }

        base.Dispose(disposing);
    }
}
