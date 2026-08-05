namespace RateTray.Ui;

/// <summary>
/// Works out where the details fly-out goes: in the taskbar corner the tray icons live in,
/// and always fully inside the visible work area.
///
/// Kept free of WinForms types so it can be unit tested for every taskbar edge and screen
/// size without a desktop.
/// </summary>
public static class FlyoutPlacement
{
    public enum Edge { Bottom, Top, Left, Right }

    /// <summary>Derives which screen edge the taskbar is docked to from its rectangle.</summary>
    public static Edge EdgeOf(Rectangle taskbar, Rectangle screen)
    {
        if (taskbar.Width >= taskbar.Height)
            return taskbar.Top - screen.Top >= screen.Bottom - taskbar.Bottom ? Edge.Bottom : Edge.Top;

        return taskbar.Left - screen.Left >= screen.Right - taskbar.Right ? Edge.Right : Edge.Left;
    }

    /// <summary>
    /// Top-left corner for a window of <paramref name="size"/>. The result is always inside
    /// <paramref name="workArea"/>: a window larger than the screen is aligned to the top-left
    /// corner rather than pushed off-screen.
    /// </summary>
    public static Point Locate(Size size, Rectangle workArea, Edge edge, int margin)
    {
        // The tray sits at the far end of the taskbar, so anchor to that corner.
        var x = edge == Edge.Left
            ? workArea.Left + margin
            : workArea.Right - size.Width - margin;

        var y = edge == Edge.Top
            ? workArea.Top + margin
            : workArea.Bottom - size.Height - margin;

        return new Point(
            Clamp(x, workArea.Left, workArea.Right - size.Width),
            Clamp(y, workArea.Top, workArea.Bottom - size.Height));
    }

    /// <summary>Caps the window so it never exceeds the work area it has to fit into.</summary>
    public static Size Fit(Size desired, Rectangle workArea, int margin) => new(
        Math.Min(desired.Width, Math.Max(1, workArea.Width - 2 * margin)),
        Math.Min(desired.Height, Math.Max(1, workArea.Height - 2 * margin)));

    private static int Clamp(int value, int min, int max) =>
        max < min ? min : Math.Clamp(value, min, max);
}
