using RateTray.Configuration;
using RateTray.Model;
using RateTray.Providers;

namespace RateTray;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Diagnostics: `RateTray.exe --once > out.txt` polls both providers, prints every
        // limit id the account exposes (the ids you put in settings.json) and exits.
        if (args.Contains("--once", StringComparer.OrdinalIgnoreCase))
            return DumpOnce().GetAwaiter().GetResult();

        // `--details` opens just the fly-out with live data and no tray icons.
        if (args.Contains("--details", StringComparer.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            return PreviewDetails().GetAwaiter().GetResult();
        }

        // `--settings` opens just the settings dialog, without starting the tray.
        if (args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            return PreviewSettings().GetAwaiter().GetResult();
        }

        // A second instance would add duplicate tray icons for the same limits.
        using var single = new Mutex(initiallyOwned: true, @"Local\RateTray.SingleInstance", out var isFirst);
        if (!isFirst) return 0;

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ReportFatal(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ReportFatal(e.ExceptionObject as Exception);

        Application.Run(new TrayApp());
        return 0;
    }

    private static async Task<int> PreviewDetails()
    {
        var config = ConfigStore.Load();
        Localization.Loc.Use(config.Language);

        var results = await PollAsync(config).ConfigureAwait(true);

        var form = new Ui.DetailsForm(config, new Ui.Palette(config)) { AutoHide = false };

        // The preview has no polling loop, so the countdown is placed mid-interval to show
        // what the strip looks like in the running app rather than leaving it blank.
        form.ShowNearTray(results, DateTimeOffset.Now,
            DateTimeOffset.Now.AddSeconds(config.RefreshSeconds * 0.45));
        Application.Run(form);
        return 0;
    }

    private static async Task<int> PreviewSettings()
    {
        var config = ConfigStore.Load();
        Localization.Loc.Use(config.Language);

        var results = await PollAsync(config).ConfigureAwait(true);
        var known = results.SelectMany(r => r.Readings).ToList();

        using var form = new Ui.SettingsForm(config, known);
        Application.Run(form);
        return 0;
    }

    private static async Task<ProviderResult[]> PollAsync(AppConfig config)
    {
        IUsageProvider[] providers =
        [
            new ClaudeUsageProvider(config.Claude),
            new CodexUsageProvider(config.Codex),
        ];

        return await Task.WhenAll(providers
            .Where(p => p.Enabled)
            .Select(p => p.ReadAsync(CancellationToken.None)))
            .ConfigureAwait(true);
    }

    private static async Task<int> DumpOnce()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // Diagnostics stay English regardless of the configured language, so pasting them
        // into a bug report is useful to everyone.
        Localization.Loc.Use("en");

        var config = ConfigStore.Load();
        IUsageProvider[] providers =
        [
            new ClaudeUsageProvider(config.Claude),
            new CodexUsageProvider(config.Codex),
        ];

        var failed = false;
        Console.WriteLine($"settings.json: {ConfigStore.Path_}");
        Console.WriteLine($"Icon size:     {Ui.TrayIconRenderer.IconSize} px");
        Console.WriteLine();

        foreach (var provider in providers)
        {
            if (!provider.Enabled)
            {
                Console.WriteLine($"[{provider.Group}] disabled");
                continue;
            }

            var result = await provider.ReadAsync(CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine($"[{result.Group}] {(result.Ok ? "ok" : "ERROR: " + result.Error)}");

            if (result.Auth is { } auth)
                Console.WriteLine($"   Sign-in: {auth.Summary()}   ({auth.Detail})");

            // Belongs in a pasted diagnostic more than anywhere else: it explains numbers that
            // came from somewhere other than the endpoint everyone else is reading.
            if (result.Notice is { } notice)
                Console.WriteLine($"   {notice}");

            foreach (var reading in result.Readings)
            {
                Console.WriteLine(
                    $"   {reading.Id,-34} {Math.Round(reading.Percent),3} %   " +
                    $"Icon={reading.IconText,-3} {reading.ResetText()}{(reading.IsActive ? "   [active]" : "")}");
            }

            failed |= !result.Ok;
            Console.WriteLine();
        }

        return failed ? 1 : 0;
    }

    private static void ReportFatal(Exception? exception)
    {
        MessageBox.Show(
            exception?.ToString() ?? "Unknown error",
            "RateTray",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
