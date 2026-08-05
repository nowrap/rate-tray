namespace RateTray.Configuration;

/// <summary>
/// What a token may travel over, and where it is going.
///
/// <c>claude.usageUrl</c> and <c>claude.tokenUrl</c> are settings on purpose — an endpoint that
/// moves should be fixable without a rebuild — so what is checked here is the transport, not the
/// host. OAuth requires TLS for exactly this reason: over plain http the bearer token is on the
/// wire in clear, and the app would have handed it out before anyone noticed the typo. Loopback
/// is the one exception, because a local mock is the legitimate use for http and nothing leaves
/// the machine.
///
/// A host other than the shipped one is *not* refused. Endpoint discovery is why these are
/// configurable at all, and a rule that forbids it would remove the only reason they exist.
/// It is reported instead, so that pointing the tray somewhere else stays a visible choice
/// rather than a silent redirection someone inherited with a copied settings file.
/// </summary>
public static class Endpoint
{
    /// <summary>True when a credential may be sent to this URL.</summary>
    public static bool IsSecure(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));

    /// <summary>
    /// The host worth pointing out, or null when this is the endpoint the app ships with.
    /// Compared by host alone: a different path on the same API is a version change, not a
    /// different destination, and flagging it would train people to ignore the notice.
    /// </summary>
    public static string? ForeignHost(string? url, string shipped) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        Uri.TryCreate(shipped, UriKind.Absolute, out var expected) &&
        !string.Equals(uri.Host, expected.Host, StringComparison.OrdinalIgnoreCase)
            ? uri.Host
            : null;
}
