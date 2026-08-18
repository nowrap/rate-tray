namespace RateTray.Ui;

/// <summary>
/// Draws one line of text into a box, ending it in an ellipsis when it does not fit.
///
/// Every string in this UI that originates outside it — a provider error, a notice, a label the
/// server named — is of unknown length, and <see cref="Graphics.DrawString(string, Font, Brush,
/// float, float)"/> paints such a string as far as it reaches: across the bars, past the window
/// edge, and, if it contains a newline, straight over the rows below. The layout gives those
/// strings one line and one width; this is what holds them to it.
/// </summary>
internal static class TextLine
{
    /// <summary>
    /// Left as the default (not typographic) format so a clipped line sits on exactly the same
    /// left edge and baseline as the neighbouring lines drawn with the point overload.
    /// </summary>
    private static readonly StringFormat Format = new()
    {
        FormatFlags = StringFormatFlags.NoWrap,
        Trimming = StringTrimming.EllipsisCharacter,
    };

    public static void Draw(Graphics g, string text, Font font, Brush brush, float x, float y, float width) =>
        g.DrawString(text, font, brush, new RectangleF(x, y, width, font.Height), Format);
}
