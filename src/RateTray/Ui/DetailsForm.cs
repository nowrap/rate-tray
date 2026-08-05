using System.Drawing.Drawing2D;
using RateTray.Configuration;
using RateTray.Localization;
using RateTray.Model;

namespace RateTray.Ui;

/// <summary>
/// Fly-out panel listing every limit with a progress bar, its reset time and the validity of
/// the login it was read with. Fully owner-drawn so it can follow the Windows light/dark theme
/// without pulling in a UI framework.
///
/// All metrics derive from the DPI of the monitor the fly-out lands on, not from the form's
/// own <c>DeviceDpi</c> — the window is sized before it is moved there, so on a mixed 4K/HD
/// desktop its own DPI is still the previous monitor's. Fonts are sized in pixels for the
/// same reason: point sizes would be scaled a second time by the DPI the form ends up at.
/// </summary>
public sealed class DetailsForm : Form
{
    private const int BaseWidth = 560;
    private const int EdgeMargin = 12;

    private readonly AppConfig _config;
    private readonly Palette _palette;

    private IReadOnlyList<ProviderResult> _results = [];
    private DateTimeOffset? _lastUpdate;
    private DateTimeOffset? _nextPoll;
    private int _dpi = 96;

    /// <summary>
    /// Redraws only the countdown strip. 250 ms keeps a 60-second sweep from advancing in
    /// visible jumps, and repainting a two-pixel band costs nothing.
    /// </summary>
    private readonly System.Windows.Forms.Timer _countdown = new() { Interval = 250 };

    private Font _titleFont = null!;
    private Font _labelFont = null!;
    private Font _smallFont = null!;
    private Font _valueFont = null!;

    public DetailsForm(AppConfig config, Palette palette)
    {
        _config = config;
        _palette = palette;

        Text = "RateTray Details";               // window title, used by the e2e smoke test
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;           // every metric is scaled explicitly below
        DoubleBuffered = true;
        KeyPreview = true;

        BuildFonts();
        Deactivate += (_, _) => { if (AutoHide) Hide(); };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Hide(); };

        _countdown.Tick += (_, _) => InvalidateCountdown();
        VisibleChanged += (_, _) =>
        {
            if (Visible) _countdown.Start();
            else _countdown.Stop();
        };
    }

    /// <summary>Off in the --details preview, where losing focus must not close the window.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool AutoHide { get; init; } = true;

    private bool Dark => TrayIconRenderer.UsesDarkTaskbar(_config.Theme);

    private Color Background => Dark ? Color.FromArgb(31, 33, 38) : Color.White;
    private Color Foreground => Dark ? Color.FromArgb(236, 238, 242) : Color.FromArgb(26, 28, 32);
    private Color Muted => Dark ? Color.FromArgb(154, 160, 170) : Color.FromArgb(107, 114, 128);
    private Color BorderColor => Dark ? Color.FromArgb(58, 61, 69) : Color.FromArgb(214, 219, 228);

    private int Px(int value) => (int)Math.Round(value * (_dpi / 96.0));

    /// <param name="nextPoll">
    /// When the next poll is due, drawn as the countdown strip along the bottom edge. Null
    /// hides the strip — there is nothing to count down to.
    /// </param>
    public void ShowNearTray(IReadOnlyList<ProviderResult> results, DateTimeOffset? lastUpdate,
        DateTimeOffset? nextPoll = null)
    {
        _results = results;
        _lastUpdate = lastUpdate;
        _nextPoll = nextPoll;

        var taskbar = Native.TaskbarRect();
        var screen = taskbar is { } bar ? Screen.FromRectangle(bar) : Screen.PrimaryScreen!;
        var edge = taskbar is { } t
            ? FlyoutPlacement.EdgeOf(t, screen.Bounds)
            : FlyoutPlacement.Edge.Bottom;

        // Anchor the DPI probe inside the taskbar corner the fly-out will occupy.
        _dpi = Native.DpiForPoint(new Point(
            screen.WorkingArea.Left + screen.WorkingArea.Width / 2,
            screen.WorkingArea.Top + screen.WorkingArea.Height / 2));

        BuildFonts();

        var margin = Px(EdgeMargin);
        var size = FlyoutPlacement.Fit(new Size(Px(BaseWidth), MeasureHeight()), screen.WorkingArea, margin);
        Bounds = new Rectangle(FlyoutPlacement.Locate(size, screen.WorkingArea, edge, margin), size);

        Show();
        Activate();
        Invalidate();
    }

    private void BuildFonts()
    {
        var family = SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif;

        _titleFont?.Dispose();
        _labelFont?.Dispose();
        _smallFont?.Dispose();
        _valueFont?.Dispose();

        _titleFont = new Font(family, Px(15), FontStyle.Bold, GraphicsUnit.Pixel);
        _labelFont = new Font(family, Px(14), FontStyle.Regular, GraphicsUnit.Pixel);
        _smallFont = new Font(family, Px(11), FontStyle.Regular, GraphicsUnit.Pixel);
        _valueFont = new Font(family, Px(14), FontStyle.Bold, GraphicsUnit.Pixel);
    }

    private int MeasureHeight()
    {
        var height = Px(16);
        foreach (var result in _results)
        {
            height += Px(26);                                   // group header
            if (result.Auth is not null) height += Px(18);
            if (result.Error is not null) height += Px(20);
            height += result.Readings.Count * Px(46);
            height += Px(10);
        }

        return height + Px(30);                                 // footer
    }

    /// <summary>Band along the bottom edge, inside the border.</summary>
    private Rectangle CountdownBounds => new(1, Height - Px(3) - 1, Width - 2, Px(3));

    private void InvalidateCountdown()
    {
        if (_nextPoll is not null) Invalidate(CountdownBounds);
    }

    /// <summary>
    /// How far the current poll interval has run, 0 to 1. Pure so the sweep can be tested
    /// without a window.
    /// </summary>
    internal static double CountdownProgress(DateTimeOffset nextPoll, TimeSpan interval, DateTimeOffset now)
    {
        if (interval <= TimeSpan.Zero) return 0;

        var remaining = nextPoll - now;
        if (remaining <= TimeSpan.Zero) return 1;      // overdue: a poll is in flight or late
        if (remaining >= interval) return 0;           // clock skew, or the interval just grew

        return 1 - remaining / interval;
    }

    private void DrawCountdown(Graphics g, Rectangle bounds)
    {
        using (var back = new SolidBrush(Background)) g.FillRectangle(back, bounds);

        if (_nextPoll is not { } next) return;

        // The track is drawn even at zero progress. Without it the strip is simply absent for
        // the first seconds after a refresh, which reads as "the feature isn't there".
        using (var track = new SolidBrush(Color.FromArgb(Dark ? 38 : 30, Muted)))
            g.FillRectangle(track, bounds);

        var progress = CountdownProgress(next, TimeSpan.FromSeconds(Math.Max(1, _config.RefreshSeconds)), DateTimeOffset.Now);
        var width = (int)Math.Round(bounds.Width * progress);
        if (width <= 0) return;

        // Ambient, not something to read — but it has to be visible to be ambient at all.
        using var brush = new SolidBrush(Color.FromArgb(Dark ? 130 : 105, Muted));
        g.FillRectangle(brush, bounds with { Width = width });
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        // A repaint confined to the strip redraws only the strip; the timer fires four times a
        // second and the rest of the window has not changed.
        if (e.ClipRectangle.Top >= CountdownBounds.Top)
        {
            DrawCountdown(g, CountdownBounds);
            return;
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var back = new SolidBrush(Background)) g.FillRectangle(back, ClientRectangle);
        using (var border = new Pen(BorderColor)) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

        using var foreground = new SolidBrush(Foreground);
        using var muted = new SolidBrush(Muted);

        var pad = Px(16);
        var y = Px(12);
        var barLeft = Px(250);
        var barWidth = Px(200);
        var valueLeft = barLeft + barWidth + Px(12);

        if (_results.Count == 0)
        {
            g.DrawString(Loc.T("details.noData"), _labelFont, muted, pad, y);
            return;
        }

        foreach (var result in _results)
        {
            var accent = Harmony.Legible(_palette.Service(result.Group), Dark);
            var badge = new RectangleF(pad, y + Px(2), Px(16), Px(16));
            ServiceBadge.Draw(g, badge, result.Group, accent);
            g.DrawString(result.Group, _titleFont, foreground, pad + Px(23), y);
            y += Px(26);

            if (result.Auth is { } auth)
            {
                var text = Loc.T("details.auth", auth.Summary()) +
                           (auth.Detail is { Length: > 0 } d ? $"  ·  {d}" : "");
                using var brush = new SolidBrush(auth.IsValid ? Muted : Harmony.Legible(_palette.Critical, Dark));
                g.DrawString(text, _smallFont, brush, pad, y);
                y += Px(18);
            }

            if (result.Error is { } error)
            {
                // "paused" without a duration reads like "broken", so say when it retries.
                if (result.RetryAt is { } retry && retry > DateTimeOffset.Now)
                    error += "  ·  " + Loc.T("details.retryIn", LimitReading.FormatSpan(retry - DateTimeOffset.Now));

                using var brush = new SolidBrush(Harmony.Legible(_palette.Critical, Dark));
                g.DrawString(error, _smallFont, brush, pad, y);
                y += Px(20);
            }

            foreach (var reading in result.Readings)
            {
                var color = _palette.ForReading(reading.Group, reading.Percent, reading.Variant, reading.VariantCount, Dark);

                g.DrawString(reading.Label + (reading.IsActive ? "  •" : ""), _labelFont, foreground, pad, y);
                g.DrawString(reading.ResetText(), _smallFont, muted, pad, y + Px(18));

                DrawBar(g, new Rectangle(barLeft, y + Px(12), barWidth, Px(8)), reading.Percent, color);

                using var valueBrush = new SolidBrush(color);
                g.DrawString($"{Math.Round(reading.Percent)} %", _valueFont, valueBrush, valueLeft, y + Px(7));

                y += Px(46);
            }

            y += Px(10);
        }

        var footer = _lastUpdate is { } stamp
            ? Loc.T("details.footer", stamp.ToLocalTime().ToString("HH:mm:ss", Loc.Culture), _config.RefreshSeconds)
            : Loc.T("details.noData");
        g.DrawString(footer, _smallFont, muted, pad, Height - Px(24));

        DrawCountdown(g, CountdownBounds);
    }

    private void DrawBar(Graphics g, Rectangle bounds, double percent, Color color)
    {
        var radius = bounds.Height / 2f;

        using (var track = new SolidBrush(Palette.Track(color, Dark)))
        using (var path = RoundedRect(bounds, radius))
            g.FillPath(track, path);

        var filled = (int)Math.Round(bounds.Width * Math.Clamp(percent, 0, 100) / 100.0);
        // A non-zero value always shows at least a full cap, otherwise 1 % renders as nothing.
        if (filled < bounds.Height) filled = percent > 0 ? bounds.Height : 0;
        if (filled <= 0) return;

        using var fill = new SolidBrush(color);
        using var filledPath = RoundedRect(bounds with { Width = filled }, radius);
        g.FillPath(fill, filledPath);
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _countdown.Dispose();
            _titleFont?.Dispose();
            _labelFont?.Dispose();
            _smallFont?.Dispose();
            _valueFont?.Dispose();
        }

        base.Dispose(disposing);
    }
}
