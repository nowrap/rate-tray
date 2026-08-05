using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RateTray.E2E;

/// <summary>
/// Launches the real RateTray.exe and inspects it from the outside — no access to its
/// internals, which is the point of these tests.
/// </summary>
internal static class AppHarness
{
    /// <summary>
    /// The ProjectReference copies the app next to the test assembly; the source-tree path is
    /// the fallback for runners that flatten or relocate the output.
    /// </summary>
    public static string ExecutablePath
    {
        get
        {
            var beside = Path.Combine(AppContext.BaseDirectory, "RateTray.exe");
            if (File.Exists(beside)) return beside;

            var configuration = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}")
                ? "Release"
                : "Debug";
            var fromSource = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "RateTray", "bin", configuration, "net9.0-windows", "RateTray.exe"));

            return File.Exists(fromSource)
                ? fromSource
                : throw new FileNotFoundException($"RateTray.exe not found (looked in {beside} and {fromSource})");
        }
    }

    public static bool HasDesktop => OperatingSystem.IsWindows() && Environment.UserInteractive;

    public static bool HasClaudeCredentials => File.Exists(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json"));

    public static bool HasCodex =>
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "OpenAI", "Codex", "bin", "codex.exe")) ||
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator)
            .Any(dir => dir.Length > 0 && SafeExists(Path.Combine(dir, "codex.exe")));

    private static bool SafeExists(string path)
    {
        try { return File.Exists(path); }
        catch (ArgumentException) { return false; }
    }

    /// <summary>Runs the app to completion and returns its exit code and stdout.</summary>
    public static (int ExitCode, string Output) Run(string arguments, TimeSpan timeout)
    {
        var info = new ProcessStartInfo(ExecutablePath, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };

        using var process = Process.Start(info) ?? throw new InvalidOperationException("could not start the app");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"`{arguments}` did not finish within {timeout}");
        }

        Task.WaitAll([stdout, stderr], TimeSpan.FromSeconds(5));
        return (process.ExitCode, stdout.Result);
    }

    /// <summary>Starts the app and waits until it has put a visible window on screen.</summary>
    public static Process StartWithWindow(string arguments, TimeSpan timeout)
    {
        var process = Process.Start(new ProcessStartInfo(ExecutablePath, arguments) { UseShellExecute = false })
                      ?? throw new InvalidOperationException("could not start the app");

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited) throw new InvalidOperationException($"the app exited early with {process.ExitCode}");
            if (VisibleWindows(process.Id).Count > 0) return process;
            Thread.Sleep(200);
        }

        Kill(process);
        throw new TimeoutException($"`{arguments}` showed no window within {timeout}");
    }

    public static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (Exception)
        {
            // already gone
        }
    }

    /// <summary>
    /// The window the tests mean. A process can own more visible windows than it created —
    /// the shell parents small helper windows onto a dialog's title bar — so the biggest one
    /// is used rather than asserting there is exactly one.
    /// </summary>
    public static Rectangle MainWindow(int processId)
    {
        var windows = VisibleWindows(processId);

        return windows.Count > 0
            ? windows.MaxBy(r => (long)r.Width * r.Height)
            : throw new InvalidOperationException($"process {processId} has no visible window");
    }

    // ------------------------------------------------------------------ win32

    public static List<Rectangle> VisibleWindows(int processId)
    {
        var found = new List<Rectangle>();

        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var owner);
            if (owner != processId || !IsWindowVisible(handle)) return true;
            if (!GetWindowRect(handle, out var r)) return true;

            var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            if (rect is { Width: > 0, Height: > 0 }) found.Add(rect);
            return true;
        }, IntPtr.Zero);

        return found;
    }

    public static string WindowTitle(int processId)
    {
        var title = "";

        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out var owner);
            if (owner != processId || !IsWindowVisible(handle)) return true;

            var buffer = new StringBuilder(256);
            if (GetWindowText(handle, buffer, buffer.Capacity) > 0 && title.Length == 0) title = buffer.ToString();
            return true;
        }, IntPtr.Zero);

        return title;
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out int processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr handle, out Rect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
}

/// <summary>Runs only where a desktop session exists; skipped on a headless CI agent.</summary>
public sealed class DesktopFactAttribute : FactAttribute
{
    public DesktopFactAttribute()
    {
        if (!AppHarness.HasDesktop) Skip = "no interactive Windows desktop";
    }
}

/// <summary>Runs only when the machine is actually signed in to both CLIs.</summary>
public sealed class SignedInFactAttribute : FactAttribute
{
    public SignedInFactAttribute()
    {
        if (!AppHarness.HasClaudeCredentials) Skip = "no Claude credentials on this machine";
        else if (!AppHarness.HasCodex) Skip = "codex.exe not installed";
    }
}
