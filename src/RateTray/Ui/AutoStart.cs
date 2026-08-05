using System.Diagnostics;
using Microsoft.Win32;

namespace RateTray.Ui;

/// <summary>Per-user autostart via the HKCU Run key — needs no elevation.</summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RateTray";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string value && value.Length > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public static bool TrySet(bool enabled, out string? error)
    {
        error = null;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (key is null) { error = "Run-Key nicht schreibbar"; return false; }

            if (enabled)
            {
                var path = ExecutablePath();
                if (path is null) { error = "Programmpfad nicht ermittelbar"; return false; }
                key.SetValue(ValueName, $"\"{path}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Environment.ProcessPath is the real host executable, which is what a single-file
    /// publish needs; Assembly.Location is empty there.
    /// </summary>
    private static string? ExecutablePath()
    {
        if (Environment.ProcessPath is { Length: > 0 } path) return path;
        return Process.GetCurrentProcess().MainModule?.FileName;
    }
}
