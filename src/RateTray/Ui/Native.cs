using System.Runtime.InteropServices;

namespace RateTray.Ui;

/// <summary>Win32 calls needed for taskbar-aware, per-monitor-DPI-correct placement.</summary>
internal static class Native
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(PointStruct point, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    private const uint MonitorDefaultToNearest = 2;
    private const int MdtEffectiveDpi = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointStruct { public int X, Y; }

    /// <summary>Rectangle of the primary taskbar, or null if it cannot be determined.</summary>
    public static Rectangle? TaskbarRect()
    {
        var handle = FindWindow("Shell_TrayWnd", null);
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var r)) return null;

        var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        return rect is { Width: > 0, Height: > 0 } ? rect : null;
    }

    /// <summary>
    /// Effective DPI of the monitor containing <paramref name="point"/>. Read from the monitor
    /// rather than from the window, because the fly-out is sized before it has been moved onto
    /// its target screen — on a mixed 4K/HD setup the window's own DPI is still the old one.
    /// </summary>
    public static int DpiForPoint(Point point)
    {
        try
        {
            var monitor = MonitorFromPoint(new PointStruct { X = point.X, Y = point.Y }, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, MdtEffectiveDpi, out var dpiX, out _) == 0 && dpiX > 0)
                return (int)dpiX;
        }
        catch (DllNotFoundException)
        {
            // Shcore.dll is Windows 8.1+; fall through to the default below.
        }

        return 96;
    }

    public static int DpiForWindow(IntPtr handle)
    {
        try
        {
            var dpi = GetDpiForWindow(handle);
            return dpi > 0 ? (int)dpi : 96;
        }
        catch (EntryPointNotFoundException)
        {
            return 96;
        }
    }
}
