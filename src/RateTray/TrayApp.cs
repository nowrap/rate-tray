using System.Diagnostics;
using RateTray.Configuration;
using RateTray.Localization;
using RateTray.Model;
using RateTray.Providers;
using RateTray.Ui;

namespace RateTray;

/// <summary>
/// Owns the tray icons and the polling loop. One <see cref="NotifyIcon"/> per configured
/// limit, Core Temp style, all sharing a single context menu.
/// </summary>
public sealed class TrayApp : ApplicationContext
{
    /// <summary>Hover cards are hidden once the tray stops reporting mouse movement.</summary>
    private const int TooltipIdleMs = 700;

    /// <summary>How far the pointer may drift and still count as "still parked on the icon".</summary>
    private const int HoverSlack = 4;

    /// <summary>Floor for the poll interval — the usage endpoint rate-limits a tight loop.</summary>
    private const int MinRefreshSeconds = 30;

    private readonly AppConfig _config;
    private readonly Palette _palette;
    private readonly List<IUsageProvider> _providers;
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly System.Windows.Forms.Timer _tooltipTimer = new();
    private readonly Dictionary<string, NotifyIcon> _icons = [];
    private readonly ContextMenuStrip _menu = new();
    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>Keys of "already warned" windows, so a toast fires once per reset period.</summary>
    private readonly HashSet<string> _notified = [];

    private ToolStripMenuItem _iconsMenu = null!;
    private ToolStripMenuItem _languageMenu = null!;
    private ToolStripMenuItem _aboutMenu = null!;
    private DetailsForm? _details;
    private TooltipWindow? _tooltip;
    private UpdateCheck.Result? _latestUpdate;
    private DateTime _lastHover = DateTime.MinValue;
    private Point _lastHoverPos;
    private string? _hoveredId;

    private IReadOnlyList<ProviderResult> _lastResults = [];
    private Dictionary<string, LimitReading> _lastReadings = [];
    private DateTimeOffset? _lastUpdate;
    private bool _refreshing;

    /// <summary>When the timer fires next, drawn as the countdown strip in the details window.</summary>
    private DateTimeOffset _nextPoll;

    /// <summary>Last readings that actually arrived, per provider, so a failed poll can keep
    /// showing numbers instead of blanking the tray. Persisted across restarts.</summary>
    private readonly Dictionary<string, CachedReadings> _lastGood;

    /// <summary>Decides which providers may be polled after a failure.</summary>
    private readonly PollScheduler _schedule;

    /// <summary>
    /// Poll interval in milliseconds. Multiplied as a <c>long</c> on purpose: a hand-edited
    /// refreshSeconds big enough to overflow an int used to arrive as a negative interval, which
    /// the timer rejects — the config is clamped, and this keeps the arithmetic safe regardless.
    /// </summary>
    private int PollIntervalMs => (int)Math.Clamp(
        (long)Math.Max(MinRefreshSeconds, _config.RefreshSeconds) * 1000,
        MinRefreshSeconds * 1000L,
        int.MaxValue);

    public TrayApp()
    {
        _config = ConfigStore.Load();
        Loc.Use(_config.Language);

        _palette = new Palette(_config);
        _providers =
        [
            new ClaudeUsageProvider(_config.Claude),
            new CodexUsageProvider(_config.Codex),
        ];

        _lastGood = UsageCache.Load();
        _schedule = new PollScheduler(_config.MaxBackoffMinutes);

        BuildMenu();
        _menu.Opened += (_, _) => HideTooltip();
        ShowCachedReadings();

        _timer.Interval = PollIntervalMs;
        _timer.Tick += (_, _) => { ScheduleNextPoll(); _ = RefreshAsync(); };
        _timer.Start();
        ScheduleNextPoll();

        _tooltipTimer.Interval = 200;
        _tooltipTimer.Tick += (_, _) => HideTooltipWhenIdle();

        _ = RefreshAsync();
        MaybeCheckForUpdates();
    }

    // ---------------------------------------------------------------- polling

    /// <param name="force">
    /// Set by the menu's refresh command: an explicit request from the user clears any
    /// backoff, because they are entitled to a retry now even if the last poll failed.
    /// </param>
    private async Task RefreshAsync(bool force = false)
    {
        if (_refreshing) return;                 // a slow poll must not stack up behind the timer
        _refreshing = true;

        if (force) _schedule.Reset();

        try
        {
            var tasks = _providers
                .Where(p => p.Enabled)
                .Select(async p =>
                {
                    // A provider that just failed is left alone for a while. Without this a
                    // rate-limited usage endpoint would be hammered once a minute forever,
                    // which is what got it rate-limited in the first place.
                    if (!_schedule.ShouldPoll(p.Group, DateTimeOffset.Now))
                        return (Result: LastResultFor(p.Group) with { RetryAt = _schedule.RetryAt(p.Group) }, Polled: false);

                    try { return (Result: await p.ReadAsync(_shutdown.Token).ConfigureAwait(false), Polled: true); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { return (Result: ProviderResult.Failed(p.Group, ex.Message), Polled: true); }
                });

            // Only cycles that actually reached a provider are recorded. Counting a skipped
            // one would extend the very pause that caused the skip, and nothing would ever be
            // retried again.
            var results = (await Task.WhenAll(tasks).ConfigureAwait(true))
                .Select(outcome => outcome.Polled ? RememberAndRestore(outcome.Result) : outcome.Result)
                .ToList();

            _lastResults = results;
            _lastReadings = results
                .SelectMany(r => r.Readings)
                .GroupBy(r => r.Id)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            // The oldest of the values on display, not the newest: one provider polling fine
            // would otherwise put its own time under numbers the other one has been serving from
            // cache for hours. Only providers in this cycle count, so a disabled one cannot
            // age the footer with an entry nothing is drawing.
            _lastUpdate = results
                .Select(r => _lastGood.TryGetValue(r.Group, out var entry) ? entry.FetchedAt : (DateTimeOffset?)null)
                .Where(fetched => fetched is not null)
                .Min() ?? DateTimeOffset.Now;

            SeedIconsOnFirstRun();
            SyncIcons();
            RaiseNotifications();
            RefreshIconsMenu();

            if (_details is { Visible: true }) _details.ShowNearTray(_lastResults, _lastUpdate, _nextPoll);
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>
    /// Paints whatever the cache holds before the first poll returns, so a restart does not
    /// start with a row of "?" — least of all when polling is slow because something is wrong.
    /// </summary>
    private void ShowCachedReadings()
    {
        if (_lastGood.Count == 0) return;

        _lastResults = _lastGood
            .Select(entry => ProviderResult.Success(entry.Key, entry.Value.Readings))
            .ToList();
        _lastReadings = _lastResults
            .SelectMany(r => r.Readings)
            .GroupBy(r => r.Id)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _lastUpdate = _lastGood.Values.Min(entry => entry.FetchedAt);

        SyncIcons();
        RefreshIconsMenu();
    }

    /// <summary>
    /// Records a successful poll, and on a failed one hands back the last numbers that did
    /// arrive. A transient error — a rate limit, a dropped connection — should show up as a
    /// message next to slightly stale values, not blank every icon out.
    /// </summary>
    private ProviderResult RememberAndRestore(ProviderResult result)
    {
        if (result.Ok)
        {
            _lastGood[result.Group] = new CachedReadings(DateTimeOffset.Now, result.Readings);
            _schedule.RecordSuccess(result.Group);
            UsageCache.Save(_lastGood);
            return result;
        }

        var now = DateTimeOffset.Now;
        var retryAt = _schedule.RecordFailure(
            result.Group, result.RetryAfter, now, _config.RefreshSeconds, result.RateLimited);

        if (!_lastGood.TryGetValue(result.Group, out var previous))
            return result with { RetryAt = retryAt };

        // Numbers that are too old to load from disk are too old to keep showing. Dropping the
        // entry also stops it being written back to the cache on the next successful poll.
        if (!UsageCache.IsFresh(previous, now))
        {
            _lastGood.Remove(result.Group);
            UsageCache.Save(_lastGood);
            return result with { RetryAt = retryAt };
        }

        return result with { Readings = previous.Readings, RetryAt = retryAt };
    }

    private void ScheduleNextPoll() => _nextPoll = DateTimeOffset.Now.AddMilliseconds(_timer.Interval);

    private ProviderResult LastResultFor(string group) =>
        _lastResults.FirstOrDefault(r => r.Group == group)
        ?? ProviderResult.Failed(group, Loc.T("error.backoff"));

    /// <summary>
    /// Shows every limit the account reports the first time round. Only after a poll do we
    /// know whether this plan has a session window, per-model windows such as Fable, or a
    /// second Codex bucket — so the icon list is discovered, not guessed.
    ///
    /// Seeds from whatever answered, rather than waiting for a complete set: someone signed in
    /// to only one of the two CLIs, or holding a rate limit, would otherwise be left with an
    /// empty tray forever. The list stays open until every enabled provider has been heard from
    /// once, so a service that recovers later still contributes its limits.
    /// </summary>
    private void SeedIconsOnFirstRun()
    {
        if (_config.IconsInitialized) return;

        var added = _lastResults
            .Where(result => result.Ok)
            .SelectMany(result => result.Readings)
            .OrderBy(reading => reading.Group, StringComparer.Ordinal)
            .ThenBy(reading => reading.Id, StringComparer.Ordinal)
            .Where(reading => !_config.Icons.Contains(reading.Id, StringComparer.OrdinalIgnoreCase))
            .Select(reading => reading.Id)
            .ToList();

        _config.Icons.AddRange(added);

        var complete = _lastResults.Count > 0 && _lastResults.All(result => result.Ok);
        if (complete) _config.IconsInitialized = true;

        if (added.Count > 0 || complete) ConfigStore.Save(_config);
    }

    // ------------------------------------------------------------- tray icons

    private void SyncIcons()
    {
        var wanted = new List<string>();

        foreach (var id in _config.Icons)
        {
            var reading = _lastReadings.GetValueOrDefault(id);
            var error = reading is null ? ErrorForId(id) : null;

            // A configured id that simply doesn't exist for this account (and whose provider
            // answered fine) is dropped rather than shown as a permanent "?".
            if (reading is null && error is null)
            {
                RemoveIcon(id);
                continue;
            }

            wanted.Add(id);
            var icon = _icons.TryGetValue(id, out var existing) ? existing : CreateIcon(id);

            var dark = TrayIconRenderer.UsesDarkTaskbar(_config.Theme);
            var color = reading is null
                ? Harmony.Legible(_palette.Unknown, dark)
                : _palette.ForReading(reading.Group, reading.Percent, reading.Variant, reading.VariantCount, dark);

            var previous = icon.Icon;
            icon.Icon = TrayIconRenderer.Render(reading?.IconText ?? "?", color, _config.FontFamily);
            previous?.Dispose();

            // With the hover card active the native tooltip must stay empty, or Windows
            // would show its own text balloon alongside it.
            icon.Text = _config.RichTooltips ? string.Empty : Tooltip(id, reading, error);
            icon.Visible = true;
        }

        foreach (var stale in _icons.Keys.Except(wanted, StringComparer.OrdinalIgnoreCase).ToList())
            RemoveIcon(stale);
    }

    private NotifyIcon CreateIcon(string id)
    {
        var icon = new NotifyIcon { ContextMenuStrip = _menu, Visible = false };

        icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleDetails(); };
        icon.MouseMove += (_, _) => ShowTooltip(id);
        _icons[id] = icon;
        return icon;
    }

    private void RemoveIcon(string id)
    {
        if (!_icons.Remove(id, out var icon)) return;

        icon.Visible = false;
        var image = icon.Icon;
        icon.Dispose();
        image?.Dispose();
    }

    /// <summary>
    /// Fallback for <c>richTooltips: false</c>. NotifyIcon.Text is capped at 63 characters by
    /// WinForms, so this carries only value and reset.
    /// </summary>
    internal static string Tooltip(string id, LimitReading? reading, string? error)
    {
        if (reading is null) return Clamp($"{id}\n{error ?? "?"}");

        var line = $"{reading.Label}: {Math.Round(reading.Percent)} %";
        return reading.ResetsAt is null ? Clamp(line) : Clamp($"{line}\n{reading.ResetText()}");
    }

    internal static string Clamp(string text) => text.Length <= 63 ? text : text[..62] + "…";

    private string? ErrorForId(string id)
    {
        var group = GroupOf(id);
        return _lastResults.FirstOrDefault(r => r.Group == group)?.Error;
    }

    internal static string GroupOf(string id) =>
        id.StartsWith("codex.", StringComparison.OrdinalIgnoreCase) ? "Codex" : "Claude";

    // --------------------------------------------------------------- tooltips

    private void ShowTooltip(string id)
    {
        var pos = Cursor.Position;
        _lastHover = DateTime.UtcNow;
        _lastHoverPos = pos;
        if (!_config.RichTooltips) return;

        // The pinned details window is the rich view; a hover card would both cover it and, by
        // pulling the foreground off it, dismiss it on the very next mouse move (issue #5). The
        // context menu likewise owns the screen while it is open.
        if (_details is { Visible: true } || _menu.Visible) return;

        _tooltipTimer.Start();

        // MouseMove fires repeatedly while the pointer rests on one icon. Re-showing the card on
        // every message repositioned and repainted it constantly; only move it when the pointer
        // actually crosses to a different icon.
        if (id == _hoveredId && _tooltip is { Visible: true }) return;

        _hoveredId = id;
        _tooltip ??= new TooltipWindow(_config, _palette);
        _tooltip.ShowFor(_lastReadings.GetValueOrDefault(id), GroupOf(id), ErrorForId(id), pos);
    }

    /// <summary>
    /// The shell reports no "mouse left the icon" event, so the card is dismissed once the
    /// stream of MouseMove notifications stops.
    /// </summary>
    private void HideTooltipWhenIdle()
    {
        if (_tooltip is not { Visible: true }) { _tooltipTimer.Stop(); return; }

        // The shell stops sending MouseMove once the pointer is still, so idle time alone would
        // dismiss a card the user is actively hovering — very visible in the overflow flyout, where
        // MouseMove is sparse. Treat "pointer hasn't moved" as "still hovering" and keep the card
        // up; only once it has clearly moved away, with no MouseMove to refresh us, does it hide.
        var pos = Cursor.Position;
        if (Math.Abs(pos.X - _lastHoverPos.X) <= HoverSlack && Math.Abs(pos.Y - _lastHoverPos.Y) <= HoverSlack)
        {
            _lastHover = DateTime.UtcNow;
            return;
        }

        if ((DateTime.UtcNow - _lastHover).TotalMilliseconds < TooltipIdleMs) return;

        _tooltipTimer.Stop();
        HideTooltip();
    }

    /// <summary>
    /// Hides the hover card and forgets which icon it was showing, so the next hover re-shows it
    /// rather than treating the pointer as still parked on the icon it last drew for.
    /// </summary>
    private void HideTooltip()
    {
        _tooltip?.Hide();
        _hoveredId = null;
    }

    // ---------------------------------------------------------- notifications

    private void RaiseNotifications()
    {
        if (!_config.Notifications.Enabled) return;

        var anchor = _icons.Values.FirstOrDefault(i => i.Visible);
        if (anchor is null) return;

        foreach (var reading in _lastReadings.Values)
        {
            // Keying on the reset timestamp makes the warning repeat in the next window.
            var key = $"{reading.Id}@{reading.ResetsAt?.ToUnixTimeSeconds() ?? 0}";

            if (reading.Percent < _config.Notifications.AtPercent)
            {
                _notified.Remove(key);
                continue;
            }

            if (!_notified.Add(key)) continue;

            anchor.BalloonTipTitle = Loc.T("toast.title", reading.Group, Math.Round(reading.Percent));
            anchor.BalloonTipText = $"{reading.Label}\n{reading.ResetText()}";
            anchor.BalloonTipIcon = reading.Percent >= _config.Thresholds.Critical
                ? ToolTipIcon.Error
                : ToolTipIcon.Warning;
            anchor.ShowBalloonTip(10_000);
        }
    }

    // ------------------------------------------------------------------- menu

    private void BuildMenu()
    {
        _menu.Items.Clear();

        _menu.Items.Add(new ToolStripMenuItem(Loc.T("menu.details"), null, (_, _) => ToggleDetails())
        {
            Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold),
        });
        _menu.Items.Add(new ToolStripMenuItem(Loc.T("menu.refresh"), null, (_, _) => _ = RefreshAsync(force: true)));
        _menu.Items.Add(new ToolStripSeparator());

        _iconsMenu = new ToolStripMenuItem(Loc.T("menu.icons"));
        _menu.Items.Add(_iconsMenu);

        _languageMenu = new ToolStripMenuItem(Loc.T("menu.language"));
        _menu.Items.Add(_languageMenu);
        BuildLanguageMenu();

        _menu.Items.Add(new ToolStripSeparator());

        var autostart = new ToolStripMenuItem(Loc.T("menu.autostart")) { Checked = AutoStart.IsEnabled, CheckOnClick = true };
        autostart.Click += (_, _) =>
        {
            if (AutoStart.TrySet(autostart.Checked, out var error)) return;

            autostart.Checked = AutoStart.IsEnabled;
            MessageBox.Show(Loc.T("dialog.autostartFailed", error), "RateTray",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        _menu.Items.Add(autostart);

        var notify = new ToolStripMenuItem(Loc.T("menu.notifyAt", _config.Notifications.AtPercent))
        {
            Checked = _config.Notifications.Enabled,
            CheckOnClick = true,
        };
        notify.Click += (_, _) =>
        {
            _config.Notifications.Enabled = notify.Checked;
            ConfigStore.Save(_config);
        };
        _menu.Items.Add(notify);

        _menu.Items.Add(new ToolStripMenuItem(Loc.T("menu.settings"), null, (_, _) => OpenSettings()));
        _aboutMenu = new ToolStripMenuItem(Loc.T("menu.about"), null, (_, _) => OpenAbout());
        _menu.Items.Add(_aboutMenu);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem(Loc.T("menu.quit"), null, (_, _) => Quit()));

        UpdateAboutMarker();
        RefreshIconsMenu();
    }

    private void BuildLanguageMenu()
    {
        _languageMenu.DropDownItems.Clear();

        var auto = new ToolStripMenuItem(Loc.T("menu.languageAuto", Loc.DisplayName(Loc.SystemLanguage)))
        {
            Checked = _config.Language.Equals("auto", StringComparison.OrdinalIgnoreCase),
        };
        auto.Click += (_, _) => ApplyLanguage("auto");
        _languageMenu.DropDownItems.Add(auto);
        _languageMenu.DropDownItems.Add(new ToolStripSeparator());

        foreach (var code in Loc.Available)
        {
            var item = new ToolStripMenuItem(Loc.DisplayName(code))
            {
                Checked = _config.Language.Equals(code, StringComparison.OrdinalIgnoreCase),
            };
            item.Click += (_, _) => ApplyLanguage(code);
            _languageMenu.DropDownItems.Add(item);
        }
    }

    private void ApplyLanguage(string language)
    {
        _config.Language = language;
        ConfigStore.Save(_config);
        Loc.Use(language);

        BuildMenu();

        // Labels are produced by the providers, so they only pick up the new language on the
        // next poll; the details window is rebuilt from that result.
        HideTooltip();
        _ = RefreshAsync();
    }

    /// <summary>
    /// Lists every limit discovered on this account plus anything still referenced by the
    /// config, so an id can always be unchecked again even after it stopped being reported.
    /// </summary>
    private void RefreshIconsMenu()
    {
        _iconsMenu.DropDownItems.Clear();

        var known = _lastReadings.Values
            .Select(r => (r.Id, r.Label, r.Group))
            .Concat(_config.Icons
                .Where(id => !_lastReadings.ContainsKey(id))
                .Select(id => (Id: id, Label: Loc.T("menu.notReported", id), Group: "")))
            .DistinctBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (known.Count == 0)
        {
            _iconsMenu.DropDownItems.Add(new ToolStripMenuItem(Loc.T("menu.noData")) { Enabled = false });
            return;
        }

        string? lastGroup = null;
        foreach (var entry in known.OrderBy(e => e.Group, StringComparer.Ordinal).ThenBy(e => e.Id, StringComparer.Ordinal))
        {
            if (entry.Group != lastGroup && lastGroup is not null)
                _iconsMenu.DropDownItems.Add(new ToolStripSeparator());
            lastGroup = entry.Group;

            var item = new ToolStripMenuItem(entry.Label)
            {
                Checked = _config.Icons.Contains(entry.Id, StringComparer.OrdinalIgnoreCase),
                CheckOnClick = true,
            };
            item.Click += (_, _) => ToggleIcon(entry.Id, item.Checked);
            _iconsMenu.DropDownItems.Add(item);
        }
    }

    private void ToggleIcon(string id, bool enabled)
    {
        if (enabled)
        {
            if (!_config.Icons.Contains(id, StringComparer.OrdinalIgnoreCase)) _config.Icons.Add(id);
        }
        else
        {
            _config.Icons.RemoveAll(existing => existing.Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        ConfigStore.Save(_config);
        SyncIcons();
    }

    private void OpenSettings()
    {
        HideTooltip();

        using var form = new SettingsForm(_config, _lastReadings.Values.ToList());
        if (form.ShowDialog() != DialogResult.OK) return;

        Loc.Use(_config.Language);
        _timer.Interval = PollIntervalMs;
        ScheduleNextPoll();

        // Both windows cache fonts and colours at construction, so they are rebuilt rather
        // than patched after a settings change.
        _tooltip?.Dispose();
        _tooltip = null;
        _details?.Dispose();
        _details = null;

        BuildMenu();
        _ = RefreshAsync();
    }

    // ------------------------------------------------------------------ about

    private void OpenAbout()
    {
        HideTooltip();
        using var about = new AboutForm(_config, _latestUpdate, result => { if (result is not null) SetLatestUpdate(result); });
        about.ShowDialog();
    }

    /// <summary>
    /// Fires the daily start-up update check, unless it is switched off or has already run in the
    /// last 24 hours. The result only marks the About entry — nothing interrupts the user.
    /// </summary>
    private void MaybeCheckForUpdates()
    {
        if (!_config.AutoUpdateCheck) return;
        if (_config.LastUpdateCheck is { } last && DateTimeOffset.Now - last < TimeSpan.FromHours(24)) return;

        _ = CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
        var result = await UpdateCheck.LatestAsync(AppInfo.SemVer).ConfigureAwait(true);

        _config.LastUpdateCheck = DateTimeOffset.Now;
        ConfigStore.Save(_config);
        if (result is not null) SetLatestUpdate(result);
    }

    private void SetLatestUpdate(UpdateCheck.Result result)
    {
        _latestUpdate = result;
        UpdateAboutMarker();
    }

    /// <summary>Appends a dot to the About entry while a newer version is known.</summary>
    private void UpdateAboutMarker() =>
        _aboutMenu.Text = _latestUpdate is { IsNewer: true } ? Loc.T("menu.about") + "  •" : Loc.T("menu.about");

    private void ToggleDetails()
    {
        HideTooltip();
        _details ??= new DetailsForm(_config, _palette);

        if (_details.Visible)
        {
            _details.Hide();
            return;
        }

        // Clicking a tray icon deactivates the details window first, so it may have auto-hidden
        // itself a few milliseconds ago — that same click therefore means "close", not "reopen".
        if ((DateTime.UtcNow - _details.LastAutoHidden).TotalMilliseconds < 250) return;

        _details.ShowNearTray(_lastResults, _lastUpdate, _nextPoll);
    }

    // --------------------------------------------------------------- shutdown

    private void Quit()
    {
        _timer.Stop();
        _tooltipTimer.Stop();
        _shutdown.Cancel();

        foreach (var id in _icons.Keys.ToList()) RemoveIcon(id);

        _tooltip?.Dispose();
        _details?.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _tooltipTimer.Dispose();
            _menu.Dispose();
            _shutdown.Dispose();
        }

        base.Dispose(disposing);
    }
}
