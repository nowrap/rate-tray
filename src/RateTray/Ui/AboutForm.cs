using System.Diagnostics;
using RateTray.Configuration;
using RateTray.Localization;

namespace RateTray.Ui;

/// <summary>
/// About box: version, links, a nudge to star the repo, and a manual update check. Ordinary
/// WinForms controls, like the settings dialog — a custom look buys nothing here.
/// </summary>
public sealed class AboutForm : Form
{
    private readonly AppConfig _config;
    private readonly Action<UpdateCheck.Result?>? _onChecked;

    private readonly Button _check = new() { Text = Loc.T("about.checkUpdates"), AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(10, 8, 3, 3) };
    private readonly LinkLabel _download = new() { Text = Loc.T("about.download"), AutoSize = true, Visible = false, Margin = new Padding(10, 8, 3, 3) };

    /// <param name="known">Result of the start-up check, so an available update shows at once.</param>
    /// <param name="onChecked">Invoked after a manual check so the tray can update its menu marker.</param>
    public AboutForm(AppConfig config, UpdateCheck.Result? known = null, Action<UpdateCheck.Result?>? onChecked = null)
    {
        _config = config;
        _onChecked = onChecked;

        Text = Loc.T("about.title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = SystemFonts.MessageBoxFont ?? Font;
        KeyPreview = true;
        AppIcon.ApplyTo(this);

        Controls.Add(BuildBody());
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        _check.Click += async (_, _) => await CheckAsync();
        _download.LinkClicked += (_, _) => Open(AppInfo.ReleasesUrl);

        if (known is not null) ShowResult(known);
    }

    private Control BuildBody()
    {
        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(18),
        };

        root.Controls.Add(Header());

        if (!string.IsNullOrWhiteSpace(AppInfo.Copyright))
            root.Controls.Add(Muted(AppInfo.Copyright, new Padding(3, 10, 3, 0)));

        var github = new LinkLabel { Text = Loc.T("about.github"), AutoSize = true, Margin = new Padding(3, 10, 3, 0) };
        github.LinkClicked += (_, _) => Open(AppInfo.RepoUrl);
        root.Controls.Add(github);

        var star = new Button { Text = Loc.T("about.star"), AutoSize = true, Margin = new Padding(3, 12, 3, 0) };
        star.Click += (_, _) => Open(AppInfo.RepoUrl);
        root.Controls.Add(star);
        root.Controls.Add(Muted(Loc.T("about.starHint"), new Padding(3, 4, 3, 0)));

        root.Controls.Add(new Label { AutoSize = false, Height = 10, Width = 1, Margin = Padding.Empty });

        var update = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        update.Controls.Add(_check);
        update.Controls.Add(_status);
        update.Controls.Add(_download);
        root.Controls.Add(update);

        var auto = new CheckBox
        {
            Text = Loc.T("about.autoCheck"),
            AutoSize = true,
            Checked = _config.AutoUpdateCheck,
            Margin = new Padding(3, 8, 3, 0),
        };
        auto.CheckedChanged += (_, _) => { _config.AutoUpdateCheck = auto.Checked; ConfigStore.Save(_config); };
        root.Controls.Add(auto);

        var close = new Button
        {
            Text = Loc.T("about.close"),
            AutoSize = true,
            DialogResult = DialogResult.OK,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(3, 16, 3, 0),
        };
        AcceptButton = close;
        CancelButton = close;
        root.Controls.Add(close);

        return root;
    }

    private Control Header()
    {
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };

        if (AppIcon.Value is { } icon)
        {
            using var big = new Icon(icon, 48, 48);
            panel.Controls.Add(new PictureBox
            {
                Image = big.ToBitmap(),
                SizeMode = PictureBoxSizeMode.AutoSize,
                Margin = new Padding(0, 2, 14, 0),
            });
        }

        var text = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
        text.Controls.Add(new Label
        {
            Text = "RateTray",
            AutoSize = true,
            Font = new Font(Font.FontFamily, Font.Size * 1.6f, FontStyle.Bold),
            Margin = new Padding(0, 2, 0, 0),
        });
        text.Controls.Add(Muted(Loc.T("about.version", AppInfo.Version), new Padding(0, 2, 0, 0)));
        text.Controls.Add(new Label
        {
            Text = Loc.T("about.tagline"),
            AutoSize = true,
            MaximumSize = new Size(300, 0),
            Margin = new Padding(0, 6, 0, 0),
        });
        panel.Controls.Add(text);
        return panel;
    }

    private static Label Muted(string text, Padding margin) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
        MaximumSize = new Size(360, 0),
        Margin = margin,
    };

    private async Task CheckAsync()
    {
        _check.Enabled = false;
        _download.Visible = false;
        _status.ForeColor = SystemColors.GrayText;
        _status.Text = Loc.T("about.checking");

        var result = await UpdateCheck.LatestAsync(AppInfo.SemVer);

        _config.LastUpdateCheck = DateTimeOffset.Now;
        ConfigStore.Save(_config);
        _onChecked?.Invoke(result);

        _check.Enabled = true;
        if (result is null) { _status.Text = Loc.T("about.checkFailed"); return; }
        ShowResult(result);
    }

    private void ShowResult(UpdateCheck.Result result)
    {
        if (result.IsNewer)
        {
            _status.ForeColor = SystemColors.ControlText;
            _status.Text = Loc.T("about.updateAvailable", result.Latest.ToString(3));
            _download.Visible = true;
        }
        else
        {
            _status.ForeColor = SystemColors.GrayText;
            _status.Text = Loc.T("about.upToDate", AppInfo.Version);
            _download.Visible = false;
        }
    }

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }
}
