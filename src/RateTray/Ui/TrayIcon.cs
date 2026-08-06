using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace RateTray.Ui;

/// <summary>
/// A single tray icon with a <em>stable per-icon identity</em>: it registers through
/// <c>Shell_NotifyIcon</c> with <c>NIF_GUID</c>, so Windows lists and remembers each icon
/// separately in taskbar settings (the way Core Temp does). WinForms <c>NotifyIcon</c> cannot —
/// it never sets <c>NIF_GUID</c>, so every icon of one executable collapses into a single settings
/// entry that they all share.
///
/// Deliberately a drop-in for the slice of <c>NotifyIcon</c> that <see cref="TrayApp"/> uses —
/// <see cref="Icon"/>, <see cref="Text"/>, <see cref="Visible"/>, <see cref="ContextMenuStrip"/>,
/// <see cref="MouseClick"/>, <see cref="MouseMove"/>, <see cref="ShowBalloonTip"/> and the
/// BalloonTip* properties — so nothing downstream changes.
///
/// One hidden top-level window per icon, exactly like WinForms: the tray callback is then
/// dispatched by window handle, which sidesteps the fact that the shell does not guarantee to
/// echo a caller <c>uID</c> back for GUID icons. Top-level (not message-only) so it receives the
/// <c>TaskbarCreated</c> broadcast after an Explorer restart.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int NimAdd = 0, NimModify = 1, NimDelete = 2, NimSetVersion = 4;
    private const int NifMessage = 0x01, NifIcon = 0x02, NifTip = 0x04, NifInfo = 0x10, NifGuid = 0x20, NifShowTip = 0x80;
    private const int NotifyIconVersion4 = 4;
    private const int WmTrayCallback = 0x0400 + 1;      // WM_APP + 1
    private const int WmMouseMove = 0x0200, WmContextMenu = 0x007B;
    private const int NinSelect = 0x0400, NinKeySelect = 0x0401;
    private const int NiifNone = 0, NiifInfo = 1, NiifWarning = 2, NiifError = 3;

    private static readonly int TaskbarCreated = RegisterWindowMessage("TaskbarCreated");

    /// <summary>Fixed namespace so the same limit id always maps to the same GUID across runs.</summary>
    private static readonly Guid Namespace = new("6f3b2e10-9c44-4b7a-8e2d-1a5f0c9d7e21");

    private readonly Guid _guid;
    private readonly bool _nativeTooltip;
    private readonly MessageWindow _window;

    private Icon? _icon;
    private string _tip;
    private bool _added;
    private bool _guidWorks = true;
    private bool _disposed;

    public ContextMenuStrip? ContextMenuStrip { get; set; }
    public string? BalloonTipTitle { get; set; }
    public string? BalloonTipText { get; set; }
    public ToolTipIcon BalloonTipIcon { get; set; }

    public event MouseEventHandler? MouseClick;
    public event MouseEventHandler? MouseMove;

    /// <param name="guid">Stable identity — see <see cref="GuidFor"/>.</param>
    /// <param name="label">
    /// Names the Windows settings entry. It is frozen when the icon is first added (the shell keeps
    /// it as the entry's "initial tooltip"), so it must be a stable name for the limit, not the
    /// changing percentage.
    /// </param>
    /// <param name="nativeTooltip">
    /// True in the plain-tooltip fallback (<c>richTooltips: false</c>): the live <see cref="Text"/>
    /// is shown as the native balloon tooltip. False when the rich hover card is used: no native
    /// tooltip (the label still names the settings entry).
    /// </param>
    public TrayIcon(Guid guid, string label, bool nativeTooltip)
    {
        _guid = guid;
        _nativeTooltip = nativeTooltip;
        _tip = Clamp(label, 127);
        _window = new MessageWindow(WndProc);
    }

    /// <summary>Deterministic RFC-4122-v5-style GUID from the fixed namespace and the limit id.</summary>
    public static Guid GuidFor(string id)
    {
        var idBytes = Encoding.UTF8.GetBytes(id);
        var buffer = new byte[16 + idBytes.Length];
        Namespace.ToByteArray().CopyTo(buffer, 0);
        idBytes.CopyTo(buffer, 16);

#pragma warning disable CA5350 // name-based identity, not a security context
        var hash = SHA1.HashData(buffer);
#pragma warning restore CA5350

        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);    // version 5
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);    // RFC variant
        return new Guid(bytes);
    }

    public Icon? Icon
    {
        get => _icon;
        set { _icon = value; if (_added) Modify(NifIcon); }
    }

    /// <summary>Live tooltip text. Only used in the native-tooltip fallback; ignored with the rich
    /// card, where the stable label stays as the tip and the hover card does the work.</summary>
    public string Text
    {
        set
        {
            if (!_nativeTooltip) return;
            var clamped = Clamp(value, 127);
            if (clamped == _tip) return;
            _tip = clamped;
            if (_added) Modify(NifTip);
        }
    }

    public bool Visible
    {
        get => _added;
        set { if (value) Add(); else Remove(); }
    }

    public void ShowBalloonTip(int timeout)
    {
        _ = timeout;                                    // ignored by the shell since Vista
        if (!_added) return;

        var data = Data(NifInfo);
        data.szInfo = Clamp(BalloonTipText ?? "", 255);
        data.szInfoTitle = Clamp(BalloonTipTitle ?? "", 63);
        data.dwInfoFlags = BalloonTipIcon switch
        {
            ToolTipIcon.Info => NiifInfo,
            ToolTipIcon.Warning => NiifWarning,
            ToolTipIcon.Error => NiifError,
            _ => NiifNone,
        };
        Shell_NotifyIcon(NimModify, ref data);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_added) Delete();
        _added = false;
        _window.Dispose();
    }

    // -------------------------------------------------------------- shell calls

    private void Add()
    {
        if (_added || _disposed) return;

        // Ladder: a plain add can fail if the GUID is still held by a stale registration (delete by
        // GUID and retry) or — only on older Windows — if it was first registered from another exe
        // path. As a last resort drop the GUID for this icon, which merely folds it back into the
        // shared, un-named entry rather than failing to show at all.
        if (!ShellAdd())
        {
            var stale = Data(0);
            Shell_NotifyIcon(NimDelete, ref stale);
            if (!ShellAdd())
            {
                _guidWorks = false;
                if (!ShellAdd()) return;
            }
        }

        var version = Data(0);
        version.uVersion = NotifyIconVersion4;
        Shell_NotifyIcon(NimSetVersion, ref version);
        _added = true;
    }

    private bool ShellAdd()
    {
        var data = Data(NifMessage | NifIcon | NifTip | (_nativeTooltip ? NifShowTip : 0));
        return Shell_NotifyIcon(NimAdd, ref data);
    }

    private void Remove()
    {
        if (!_added) return;
        Delete();
        _added = false;
    }

    private void Delete()
    {
        var data = Data(0);
        Shell_NotifyIcon(NimDelete, ref data);
    }

    private void Modify(int flags)
    {
        var data = Data(flags);
        Shell_NotifyIcon(NimModify, ref data);
    }

    /// <summary>
    /// Every call carries the identity: with a GUID icon the shell keys on <c>guidItem</c>, so
    /// <c>NIF_GUID</c> and the guid must be set on modify/delete/set-version too, not just add.
    /// </summary>
    private NotifyIconData Data(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = _window.Handle,
        uID = 1,
        uFlags = flags | (_guidWorks ? NifGuid : 0),
        uCallbackMessage = WmTrayCallback,
        hIcon = _icon?.Handle ?? IntPtr.Zero,
        szTip = _tip,
        szInfo = "",
        szInfoTitle = "",
        guidItem = _guidWorks ? _guid : Guid.Empty,
    };

    // ------------------------------------------------------------- window / dispatch

    private void WndProc(Message message)
    {
        if (message.Msg == TaskbarCreated)
        {
            // Explorer restarted and forgot our icon: re-add it (Add() no-ops if we are not shown).
            if (_added) { _added = false; Add(); }
            return;
        }

        if (message.Msg != WmTrayCallback) return;

        var evt = (int)(message.LParam.ToInt64() & 0xFFFF);
        var x = (short)(message.WParam.ToInt64() & 0xFFFF);
        var y = (short)((message.WParam.ToInt64() >> 16) & 0xFFFF);

        switch (evt)
        {
            case WmMouseMove:
                MouseMove?.Invoke(this, new MouseEventArgs(MouseButtons.None, 0, x, y, 0));
                break;

            // Left click (or keyboard activation). Deliberately the ONLY click event mapped: raw
            // WM_LBUTTONUP arrives as well under version 4, and firing both would toggle the details
            // window open and shut in a single click.
            case NinSelect:
            case NinKeySelect:
                MouseClick?.Invoke(this, new MouseEventArgs(MouseButtons.Left, 1, x, y, 0));
                break;

            case WmContextMenu:
                ShowContextMenu(x, y);
                break;
        }
    }

    private void ShowContextMenu(int x, int y)
    {
        if (ContextMenuStrip is not { } menu) return;

        // Without foreground the popup would not dismiss when the user clicks away — the classic
        // tray-menu quirk. WinForms' NotifyIcon does the same.
        SetForegroundWindow(_window.Handle);
        menu.Show(new Point(x, y));
    }

    private static string Clamp(string? text, int max)
    {
        text ??= "";
        return text.Length <= max ? text : text[..max];
    }

    /// <summary>Hidden top-level window that forwards its messages to the owning icon.</summary>
    private sealed class MessageWindow : NativeWindow, IDisposable
    {
        private readonly Action<Message> _handler;

        public MessageWindow(Action<Message> handler)
        {
            _handler = handler;
            CreateHandle(new CreateParams());           // default style: a top-level, invisible window
        }

        protected override void WndProc(ref Message m)
        {
            _handler(m);
            base.WndProc(ref m);
        }

        public void Dispose() => DestroyHandle();
    }

    // -------------------------------------------------------------------- interop

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;                            // union with uTimeout (ignored since Vista)
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
