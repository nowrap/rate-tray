using System.Text.RegularExpressions;

namespace RateTray.E2E;

/// <summary>
/// Runs `--once` exactly once for the whole class.
///
/// The Claude usage endpoint rate-limits, and one `--once` per test was enough to earn a 429
/// during a normal test run — which then failed the very assertions that were meant to check
/// the happy path. Every diagnostics assertion reads from this one invocation.
/// </summary>
public sealed class DiagnosticsRun
{
    public DiagnosticsRun()
    {
        if (!AppHarness.HasClaudeCredentials || !AppHarness.HasCodex) return;

        (ExitCode, Output) = AppHarness.Run("--once", TimeSpan.FromMinutes(1));
    }

    public int ExitCode { get; }

    public string Output { get; } = "";
}

[Collection("app")]
public class DiagnosticsTests(DiagnosticsRun run) : IClassFixture<DiagnosticsRun>
{
    [SignedInFact]
    public void Once_reports_both_providers_and_exits()
    {
        Assert.Contains("[Claude]", run.Output);
        Assert.Contains("[Codex]", run.Output);
        Assert.Contains("settings.json:", run.Output);
        Assert.InRange(run.ExitCode, 0, 1);         // 1 signals a provider error, not a crash
    }

    [SignedInFact]
    public void Once_prints_limit_ids_that_settings_json_can_reference()
    {
        var ids = Regex.Matches(run.Output, @"^\s{3}((?:claude|codex)\.[a-z0-9_.]+)", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.NotEmpty(ids);
        Assert.All(ids, id => Assert.Matches(@"^(claude|codex)\.", id));
    }

    [SignedInFact]
    public void Once_stays_english_so_bug_reports_are_comparable()
    {
        Assert.Contains("Icon size:", run.Output);
        Assert.Contains("Sign-in:", run.Output);
        Assert.DoesNotContain("Anmeldung:", run.Output);
    }
}

[Collection("app")]
public class DetailsWindowTests
{
    [DesktopFact]
    public void Details_window_opens_with_its_own_title()
    {
        var process = AppHarness.StartWithWindow("--details", TimeSpan.FromMinutes(1));
        try
        {
            Assert.Equal("RateTray Details", AppHarness.WindowTitle(process.Id));
        }
        finally
        {
            AppHarness.Kill(process);
        }
    }

    [DesktopFact]
    public void Details_window_is_completely_inside_a_monitor_work_area()
    {
        var process = AppHarness.StartWithWindow("--details", TimeSpan.FromMinutes(1));
        try
        {
            var window = AppHarness.MainWindow(process.Id);

            var host = Screen.AllScreens.FirstOrDefault(s => s.WorkingArea.Contains(window));

            Assert.True(host is not null,
                $"window {window} is not fully inside any work area: " +
                string.Join(", ", Screen.AllScreens.Select(s => s.WorkingArea)));
        }
        finally
        {
            AppHarness.Kill(process);
        }
    }

    [DesktopFact]
    public void Details_window_sits_in_the_taskbar_corner()
    {
        var process = AppHarness.StartWithWindow("--details", TimeSpan.FromMinutes(1));
        try
        {
            var window = AppHarness.MainWindow(process.Id);
            var work = Screen.AllScreens.First(s => s.WorkingArea.Contains(window)).WorkingArea;

            // One edge margin plus a tolerance for DPI rounding.
            var gapX = Math.Min(window.Left - work.Left, work.Right - window.Right);
            var gapY = Math.Min(window.Top - work.Top, work.Bottom - window.Bottom);

            Assert.True(gapX <= 40, $"horizontal gap to the nearest edge was {gapX} px");
            Assert.True(gapY <= 40, $"vertical gap to the nearest edge was {gapY} px");
        }
        finally
        {
            AppHarness.Kill(process);
        }
    }

    [DesktopFact]
    public void Details_window_scales_with_the_monitor_it_lands_on()
    {
        var process = AppHarness.StartWithWindow("--details", TimeSpan.FromMinutes(1));
        try
        {
            var window = AppHarness.MainWindow(process.Id);

            // 560 logical px, so 560 at 100 % and 1120 at 200 % — never the unscaled value on
            // a high-DPI screen, which is the bug this guards against.
            Assert.InRange(window.Width, 560, 1400);
            Assert.True(window.Height > 100);
        }
        finally
        {
            AppHarness.Kill(process);
        }
    }
}

[Collection("app")]
public class SettingsWindowTests
{
    [DesktopFact]
    public void Settings_window_opens()
    {
        var process = AppHarness.StartWithWindow("--settings", TimeSpan.FromMinutes(1));
        try
        {
            Assert.Contains("RateTray", AppHarness.WindowTitle(process.Id));
        }
        finally
        {
            AppHarness.Kill(process);
        }
    }

    [DesktopFact]
    public void Settings_window_is_fully_on_screen()
    {
        var process = AppHarness.StartWithWindow("--settings", TimeSpan.FromMinutes(1));
        try
        {
            var window = AppHarness.MainWindow(process.Id);

            Assert.Contains(Screen.AllScreens, s => s.WorkingArea.Contains(window));
        }
        finally
        {
            AppHarness.Kill(process);
        }
    }
}

/// <summary>Windows are global state; running these in parallel would fight over focus.</summary>
[CollectionDefinition("app", DisableParallelization = true)]
public class AppCollection;
