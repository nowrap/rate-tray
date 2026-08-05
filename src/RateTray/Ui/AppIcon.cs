namespace RateTray.Ui;

/// <summary>
/// The application icon, loaded from the embedded <c>app.ico</c>.
///
/// Embedded rather than read from disk: a single-file publish leaves no app.ico next to the
/// executable, so anything loading it by path would silently fall back to the default window
/// icon in exactly the builds users download.
/// </summary>
public static class AppIcon
{
    private static readonly Lazy<Icon?> Loaded = new(Load);

    /// <summary>Null when the resource is missing, which callers treat as "keep the default".</summary>
    public static Icon? Value => Loaded.Value;

    /// <summary>Gives a window the app icon, and does nothing if it could not be loaded.</summary>
    public static void ApplyTo(Form form)
    {
        if (Value is { } icon) form.Icon = icon;
    }

    private static Icon? Load()
    {
        try
        {
            using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream("RateTray.app.ico");
            return stream is null ? null : new Icon(stream);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return null;
        }
    }
}
