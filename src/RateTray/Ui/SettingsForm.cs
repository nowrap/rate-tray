using System.Diagnostics;
using RateTray.Configuration;
using RateTray.Localization;
using RateTray.Model;

namespace RateTray.Ui;

/// <summary>
/// Editor for everything in settings.json.
///
/// Unlike the tray fly-out this uses ordinary WinForms controls rather than owner drawing:
/// a settings dialog gains nothing from a custom look, and standard controls come with
/// keyboard navigation, screen-reader support and DPI scaling for free.
///
/// Values are written back only on Save, so Cancel needs no snapshot of the config.
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppConfig _config;
    private readonly IReadOnlyList<LimitReading> _known;

    private readonly ComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _theme = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _refresh = new() { Minimum = 30, Maximum = 3600 };
    private readonly TextBox _fontFamily = new();
    private readonly CheckBox _richTooltips = new();
    private readonly NumericUpDown _warn = new() { Minimum = 1, Maximum = 100 };
    private readonly NumericUpDown _critical = new() { Minimum = 1, Maximum = 100 };
    private readonly CheckBox _notify = new();
    private readonly NumericUpDown _notifyAt = new() { Minimum = 1, Maximum = 100 };
    private readonly NumericUpDown _maxBackoff = new() { Minimum = 1, Maximum = 120 };

    private readonly Button _claudeColor = new();
    private readonly Button _codexColor = new();
    private readonly NumericUpDown _warnHue = new() { Minimum = 0, Maximum = 359 };
    private readonly NumericUpDown _criticalHue = new() { Minimum = 0, Maximum = 359 };
    private readonly NumericUpDown _shadeSpread = new() { Minimum = 0, Maximum = 50 };
    private readonly Panel _preview = new() { Height = 44 };

    private readonly CheckedListBox _icons = new() { CheckOnClick = true, IntegralHeight = false };
    private readonly CheckBox _rediscover = new();

    private readonly CheckBox _claudeEnabled = new();
    private readonly CheckBox _claudeAutoRefresh = new();
    private readonly TextBox _claudeCredentials = new();
    private readonly CheckBox _codexEnabled = new();
    private readonly TextBox _codexPath = new();
    private readonly NumericUpDown _claudeTimeout = new() { Minimum = 5, Maximum = 300 };
    private readonly NumericUpDown _codexTimeout = new() { Minimum = 5, Maximum = 300 };

    private Color _claudeValue;
    private Color _codexValue;

    public SettingsForm(AppConfig config, IReadOnlyList<LimitReading> known)
    {
        _config = config;
        _known = known;

        Text = Loc.T("settings.title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(700, 545);
        Font = SystemFonts.MessageBoxFont ?? Font;
        AppIcon.ApplyTo(this);

        Controls.Add(BuildTabs());
        Controls.Add(BuildButtons());

        Load += (_, _) => ReadConfig();
    }

    // ------------------------------------------------------------------ layout

    private TabControl BuildTabs()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 6) };
        tabs.TabPages.Add(GeneralPage());
        tabs.TabPages.Add(ColorsPage());
        tabs.TabPages.Add(IconsPage());
        tabs.TabPages.Add(ServicesPage());
        return tabs;
    }

    private TabPage GeneralPage()
    {
        var grid = Grid();
        AddRow(grid, Loc.T("settings.language"), _language);
        AddRow(grid, Loc.T("settings.theme"), _theme);
        AddRow(grid, Loc.T("settings.refresh"), _refresh);
        AddRow(grid, Loc.T("settings.maxBackoff"), _maxBackoff);
        AddRow(grid, Loc.T("settings.fontFamily"), _fontFamily);
        AddFullRow(grid, Check(_richTooltips, Loc.T("settings.richTooltips")));
        AddRow(grid, Loc.T("settings.warn"), _warn);
        AddRow(grid, Loc.T("settings.critical"), _critical);
        AddFullRow(grid, Check(_notify, Loc.T("settings.notifyEnabled")));
        AddRow(grid, Loc.T("settings.notifyAt"), _notifyAt);
        AddSpacer(grid);

        return Page(Loc.T("settings.tab.general"), grid);
    }

    private TabPage ColorsPage()
    {
        var grid = Grid();

        _claudeColor.Height = 26;
        _codexColor.Height = 26;
        _claudeColor.FlatStyle = FlatStyle.Flat;
        _codexColor.FlatStyle = FlatStyle.Flat;
        _claudeColor.Click += (_, _) => PickColor(ref _claudeValue, _claudeColor);
        _codexColor.Click += (_, _) => PickColor(ref _codexValue, _codexColor);

        AddRow(grid, Loc.T("settings.color.claude"), _claudeColor);
        AddRow(grid, Loc.T("settings.color.codex"), _codexColor);
        AddRow(grid, Loc.T("settings.color.warnHue"), _warnHue);
        AddRow(grid, Loc.T("settings.color.criticalHue"), _criticalHue);
        AddRow(grid, Loc.T("settings.color.shadeSpread"), _shadeSpread);

        AddFullRow(grid, Hint(Loc.T("settings.color.derived")));

        _preview.Dock = DockStyle.Fill;
        _preview.Paint += (_, e) => PaintPreview(e.Graphics);
        AddRow(grid, Loc.T("settings.preview"), _preview);

        var reset = new Button { Text = Loc.T("settings.color.reset"), AutoSize = true };
        reset.Click += (_, _) => ResetColors();
        AddFullRow(grid, reset, stretch: false);
        AddSpacer(grid);

        _warnHue.ValueChanged += (_, _) => _preview.Invalidate();
        _criticalHue.ValueChanged += (_, _) => _preview.Invalidate();
        _shadeSpread.ValueChanged += (_, _) => _preview.Invalidate();

        return Page(Loc.T("settings.tab.colors"), grid);
    }

    private TabPage IconsPage()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(Hint(Loc.T("settings.icons.hint")));

        _icons.Dock = DockStyle.Fill;
        layout.Controls.Add(_icons);
        layout.Controls.Add(Check(_rediscover, Loc.T("settings.icons.rediscover")));

        return Page(Loc.T("settings.tab.icons"), layout);
    }

    private TabPage ServicesPage()
    {
        var grid = Grid();
        AddFullRow(grid, Check(_claudeEnabled, Loc.T("settings.claude.enabled")));
        AddFullRow(grid, Check(_claudeAutoRefresh, Loc.T("settings.claude.autoRefresh")));
        AddRow(grid, Loc.T("settings.claude.credentials"), WithBrowse(_claudeCredentials));
        AddRow(grid, Loc.T("settings.claude.timeout"), _claudeTimeout);
        AddFullRow(grid, new Label { Height = 8, Dock = DockStyle.Fill });
        AddFullRow(grid, Check(_codexEnabled, Loc.T("settings.codex.enabled")));
        AddRow(grid, Loc.T("settings.codex.path"), WithBrowse(_codexPath));
        AddRow(grid, Loc.T("settings.codex.timeout"), _codexTimeout);
        AddSpacer(grid);

        return Page(Loc.T("settings.tab.services"), grid);
    }

    /// <summary>
    /// The button bar sizes itself to the buttons rather than to a fixed height. A hand-picked
    /// height is a guess about font and DPI, and being three pixels short clips the bottom
    /// border off the default button — visible, but easy to mistake for a rendering artefact.
    /// </summary>
    private FlowLayoutPanel BuildButtons()
    {
        var save = new Button { Text = Loc.T("settings.ok"), DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = Loc.T("settings.cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
        var openJson = new Button { Text = Loc.T("settings.openJson"), AutoSize = true };

        save.Click += (_, _) => WriteConfig();
        openJson.Click += (_, _) => OpenJson();

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12, 10, 12, 10),
        };
        bar.Controls.Add(save);
        bar.Controls.Add(cancel);
        bar.Controls.Add(openJson);

        AcceptButton = save;
        CancelButton = cancel;
        return bar;
    }

    // ------------------------------------------------------------- layout bits

    private static TabPage Page(string title, Control content)
    {
        var page = new TabPage(title);
        page.Controls.Add(content);
        return page;
    }

    private static TableLayoutPanel Grid()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
            AutoScroll = true,
        };

        // AutoSize rather than a fixed width: German labels are noticeably longer than the
        // English ones and were being clipped at any width that looked right for English.
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return grid;
    }

    private static void AddRow(TableLayoutPanel grid, string label, Control control)
    {
        control.Dock = DockStyle.Fill;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 7, 18, 3),
        });
        grid.Controls.Add(control);
    }

    /// <param name="stretch">
    /// False for buttons, which look broken when docked across the full dialog width.
    /// </param>
    private static void AddFullRow(TableLayoutPanel grid, Control control, bool stretch = true)
    {
        if (stretch) control.Dock = DockStyle.Fill;
        else control.Anchor = AnchorStyles.Left;

        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(control);
        grid.SetColumnSpan(control, 2);
    }

    /// <summary>
    /// Explanatory text that has to wrap: a fixed height silently cut off the German
    /// translations, which run noticeably longer than the English original.
    /// </summary>
    private static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(640, 0),
        Margin = new Padding(3, 8, 3, 8),
        ForeColor = SystemColors.GrayText,
    };

    /// <summary>
    /// Soaks up the leftover height at the bottom of a page. Without it the last content row
    /// stretches to fill the page and its label drifts away from its control.
    /// </summary>
    private static void AddSpacer(TableLayoutPanel grid)
    {
        var filler = new Panel { Margin = Padding.Empty };
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.Controls.Add(filler);
        grid.SetColumnSpan(filler, 2);
    }

    private static CheckBox Check(CheckBox box, string text)
    {
        box.Text = text;
        box.AutoSize = false;
        box.Height = 26;
        return box;
    }

    private Control WithBrowse(TextBox box)
    {
        var host = new TableLayoutPanel { ColumnCount = 2, Height = 28, Dock = DockStyle.Fill, Margin = Padding.Empty };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        box.Dock = DockStyle.Fill;
        var browse = new Button { Text = Loc.T("settings.browse"), AutoSize = true };
        browse.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { CheckFileExists = true };
            if (dialog.ShowDialog(this) == DialogResult.OK) box.Text = dialog.FileName;
        };

        host.Controls.Add(box);
        host.Controls.Add(browse);
        return host;
    }

    // -------------------------------------------------------------- config i/o

    private void ReadConfig()
    {
        _language.Items.Add(Loc.T("menu.languageAuto", Loc.DisplayName(Loc.SystemLanguage)));
        foreach (var code in Loc.Available) _language.Items.Add(Loc.DisplayName(code));
        _language.SelectedIndex = _config.Language.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? 0
            : Math.Max(0, Loc.Available.ToList().FindIndex(c => c.Equals(_config.Language, StringComparison.OrdinalIgnoreCase)) + 1);

        _theme.Items.AddRange([Loc.T("settings.theme.auto"), Loc.T("settings.theme.light"), Loc.T("settings.theme.dark")]);
        _theme.SelectedIndex = _config.Theme.ToLowerInvariant() switch { "light" => 1, "dark" => 2, _ => 0 };

        _refresh.Value = Math.Clamp(_config.RefreshSeconds, 30, 3600);
        _maxBackoff.Value = Math.Clamp(_config.MaxBackoffMinutes, 1, 120);
        _fontFamily.Text = _config.FontFamily;
        _richTooltips.Checked = _config.RichTooltips;
        _warn.Value = Math.Clamp(_config.Thresholds.Warn, 1, 100);
        _critical.Value = Math.Clamp(_config.Thresholds.Critical, 1, 100);
        _notify.Checked = _config.Notifications.Enabled;
        _notifyAt.Value = Math.Clamp(_config.Notifications.AtPercent, 1, 100);

        var palette = new Palette(_config);
        SetColor(ref _claudeValue, _claudeColor, palette.Service("Claude"));
        SetColor(ref _codexValue, _codexColor, palette.Service("Codex"));
        _warnHue.Value = Math.Clamp(_config.Colors.WarnHue, 0, 359);
        _criticalHue.Value = Math.Clamp(_config.Colors.CriticalHue, 0, 359);
        _shadeSpread.Value = (decimal)Math.Clamp(Math.Round(_config.Colors.ShadeSpread * 100), 0, 50);

        foreach (var reading in _known)
            _icons.Items.Add(new IconEntry(reading.Id, reading.Label),
                _config.Icons.Contains(reading.Id, StringComparer.OrdinalIgnoreCase));

        // Ids from the config that the account no longer reports stay listed, so they can be
        // unticked instead of lingering invisibly.
        foreach (var id in _config.Icons.Where(id => _known.All(r => !r.Id.Equals(id, StringComparison.OrdinalIgnoreCase))))
            _icons.Items.Add(new IconEntry(id, Loc.T("menu.notReported", id)), true);

        _claudeEnabled.Checked = _config.Claude.Enabled;
        _claudeAutoRefresh.Checked = _config.Claude.AutoRefreshToken;
        _claudeCredentials.Text = _config.Claude.CredentialsPath ?? "";
        _claudeTimeout.Value = Math.Clamp(_config.Claude.TimeoutSeconds, 5, 300);
        _codexEnabled.Checked = _config.Codex.Enabled;
        _codexPath.Text = _config.Codex.ExecutablePath ?? "";
        _codexTimeout.Value = Math.Clamp(_config.Codex.TimeoutSeconds, 5, 300);
    }

    private void WriteConfig()
    {
        _config.Language = _language.SelectedIndex <= 0 ? "auto" : Loc.Available[_language.SelectedIndex - 1];
        _config.Theme = _theme.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "auto" };
        _config.RefreshSeconds = (int)_refresh.Value;
        _config.MaxBackoffMinutes = (int)_maxBackoff.Value;
        _config.FontFamily = string.IsNullOrWhiteSpace(_fontFamily.Text) ? "Segoe UI" : _fontFamily.Text.Trim();
        _config.RichTooltips = _richTooltips.Checked;

        // Keeping warn below critical avoids a state where no value can ever read as a warning.
        _config.Thresholds.Warn = Math.Min((int)_warn.Value, (int)_critical.Value);
        _config.Thresholds.Critical = Math.Max((int)_warn.Value, (int)_critical.Value);
        _config.Notifications.Enabled = _notify.Checked;
        _config.Notifications.AtPercent = (int)_notifyAt.Value;

        _config.Colors.Claude = Hex(_claudeValue);
        _config.Colors.Codex = Hex(_codexValue);
        _config.Colors.WarnHue = (int)_warnHue.Value;
        _config.Colors.CriticalHue = (int)_criticalHue.Value;
        _config.Colors.ShadeSpread = (double)_shadeSpread.Value / 100.0;

        _config.Icons = _icons.CheckedItems.OfType<IconEntry>().Select(e => e.Id).ToList();
        if (_rediscover.Checked) _config.IconsInitialized = false;
        else if (_config.Icons.Count > 0) _config.IconsInitialized = true;

        _config.Claude.Enabled = _claudeEnabled.Checked;
        _config.Claude.AutoRefreshToken = _claudeAutoRefresh.Checked;
        _config.Claude.CredentialsPath = Empty(_claudeCredentials.Text);
        _config.Claude.TimeoutSeconds = (int)_claudeTimeout.Value;
        _config.Codex.Enabled = _codexEnabled.Checked;
        _config.Codex.ExecutablePath = Empty(_codexPath.Text);
        _config.Codex.TimeoutSeconds = (int)_codexTimeout.Value;

        ConfigStore.Save(_config);
    }

    private static string? Empty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    // ------------------------------------------------------------------ colour

    private void PickColor(ref Color target, Button button)
    {
        using var dialog = new ColorDialog { Color = target, FullOpen = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        SetColor(ref target, button, dialog.Color);
        _preview.Invalidate();
    }

    private static void SetColor(ref Color target, Button button, Color value)
    {
        target = value;
        button.BackColor = value;
        button.ForeColor = Harmony.ToHsl(value).L > 0.55 ? Color.Black : Color.White;
        button.Text = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
    }

    private void ResetColors()
    {
        var defaults = new ColorOptions();
        var palette = new Palette(new AppConfig { Colors = defaults });

        SetColor(ref _claudeValue, _claudeColor, palette.Service("Claude"));
        SetColor(ref _codexValue, _codexColor, palette.Service("Codex"));
        _warnHue.Value = defaults.WarnHue;
        _criticalHue.Value = defaults.CriticalHue;
        _shadeSpread.Value = (decimal)Math.Round(defaults.ShadeSpread * 100);
        _preview.Invalidate();
    }

    /// <summary>
    /// Renders the actual tray icons with the pending colours, so the effect of a change —
    /// including the derived warning and critical colours — is visible before saving.
    /// </summary>
    private void PaintPreview(Graphics g)
    {
        var pending = new AppConfig
        {
            Theme = _theme.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "auto" },
            Thresholds = { Warn = (int)_warn.Value, Critical = (int)_critical.Value },
            Colors =
            {
                Claude = Hex(_claudeValue),
                Codex = Hex(_codexValue),
                WarnHue = (int)_warnHue.Value,
                CriticalHue = (int)_criticalHue.Value,
                ShadeSpread = (double)_shadeSpread.Value / 100.0,
            },
        };

        var palette = new Palette(pending);
        var dark = TrayIconRenderer.UsesDarkTaskbar(pending.Theme);

        using (var back = new SolidBrush(dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(238, 238, 238)))
            g.FillRectangle(back, _preview.ClientRectangle);

        // Three Claude limits so the shading between them is visible, then Codex, then the two
        // severity states — the same set the tray would show for a typical account.
        (string Group, double Percent, int Variant, int Count)[] samples =
        [
            ("Claude", 12, 0, 3),
            ("Claude", 24, 1, 3),
            ("Claude", 18, 2, 3),
            ("Codex", 8, 0, 1),
            ("Claude", pending.Thresholds.Warn, 0, 3),
            ("Codex", pending.Thresholds.Critical, 0, 1),
        ];

        var size = TrayIconRenderer.IconSize;
        var x = 8;

        foreach (var (group, percent, variant, count) in samples)
        {
            var color = palette.ForReading(group, percent, variant, count, dark);
            using var icon = TrayIconRenderer.Render(Math.Round(percent).ToString("0"), color, _fontFamily.Text);
            g.DrawIcon(icon, new Rectangle(x, (_preview.Height - size) / 2, size, size));
            x += size + 10;
        }
    }

    private void OpenJson()
    {
        try
        {
            ConfigStore.Save(_config);
            Process.Start(new ProcessStartInfo(ConfigStore.Path_) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Loc.T("dialog.settingsFailed", ex.Message), "RateTray",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private sealed record IconEntry(string Id, string Label)
    {
        public override string ToString() => Label;
    }
}
