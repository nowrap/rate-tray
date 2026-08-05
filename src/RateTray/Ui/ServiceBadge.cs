using System.Drawing.Drawing2D;

namespace RateTray.Ui;

/// <summary>
/// Small mark drawn next to a service name in the details window, tinted with that service's
/// colour so the group header ties back to its tray icons.
///
/// The glyphs are generic motifs drawn here in code — a spark and a terminal prompt — and are
/// deliberately not reproductions of the Anthropic or OpenAI logos. Those are trademarks, and
/// bundling them in a public repository would put redistribution terms on this project that it
/// cannot satisfy.
/// </summary>
public static class ServiceBadge
{
    public static void Draw(Graphics g, RectangleF bounds, string group, Color color)
    {
        using (var back = new SolidBrush(Color.FromArgb(48, color)))
        using (var plate = Rounded(bounds, bounds.Width * 0.30f))
            g.FillPath(back, plate);

        var inner = RectangleF.Inflate(bounds, -bounds.Width * 0.28f, -bounds.Height * 0.28f);

        if (group.Equals("Codex", StringComparison.OrdinalIgnoreCase)) DrawPrompt(g, inner, color);
        else DrawSpark(g, inner, color);
    }

    /// <summary>Four-pointed spark: straight diagonals pinched towards the centre.</summary>
    private static void DrawSpark(Graphics g, RectangleF r, Color color)
    {
        float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
        float rx = r.Width / 2f, ry = r.Height / 2f;
        var pinch = 0.16f;   // lower = sharper points

        using var path = new GraphicsPath();
        PointF top = new(cx, cy - ry), right = new(cx + rx, cy), bottom = new(cx, cy + ry), left = new(cx - rx, cy);

        AddPinched(path, top, right, cx, cy, rx * pinch, ry * pinch, 1, -1);
        AddPinched(path, right, bottom, cx, cy, rx * pinch, ry * pinch, 1, 1);
        AddPinched(path, bottom, left, cx, cy, rx * pinch, ry * pinch, -1, 1);
        AddPinched(path, left, top, cx, cy, rx * pinch, ry * pinch, -1, -1);
        path.CloseFigure();

        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    private static void AddPinched(GraphicsPath path, PointF from, PointF to,
        float cx, float cy, float dx, float dy, int signX, int signY)
    {
        // Both control points sit near the centre, so the edge bows inward instead of bulging.
        var control = new PointF(cx + dx * signX, cy + dy * signY);
        path.AddBezier(from, control, control, to);
    }

    /// <summary>Terminal prompt: a chevron and an underscore.</summary>
    private static void DrawPrompt(Graphics g, RectangleF r, Color color)
    {
        using var pen = new Pen(color, Math.Max(1.3f, r.Width * 0.17f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        g.DrawLines(pen,
        [
            new PointF(r.Left, r.Top + r.Height * 0.06f),
            new PointF(r.Left + r.Width * 0.46f, r.Top + r.Height * 0.5f),
            new PointF(r.Left, r.Bottom - r.Height * 0.06f),
        ]);

        g.DrawLine(pen, r.Left + r.Width * 0.60f, r.Bottom, r.Right, r.Bottom);
    }

    private static GraphicsPath Rounded(RectangleF bounds, float radius)
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
}
