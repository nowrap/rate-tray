using System.Net.Http;
using System.Text.Json;

namespace RateTray;

/// <summary>
/// Asks GitHub whether a newer release exists. Compares against the repo's <em>tags</em> rather
/// than its Releases: the project tags every version (v0.1.0, v0.2.0, …) but may not publish a
/// formal Release for each, and a tag list is all the "is there a newer one" question needs.
///
/// Best-effort by design — any network or parsing failure returns null, which callers treat as
/// "could not check" rather than an error. Its own <see cref="HttpClient"/> has a short timeout,
/// unlike the usage providers' deliberately unbounded one.
/// </summary>
public static class UpdateCheck
{
    private static readonly HttpClient Http = CreateClient();

    public sealed record Result(Version Latest, bool IsNewer);

    public static async Task<Result?> LatestAsync(Version current, CancellationToken token = default)
    {
        try
        {
            await using var stream = await Http.GetStreamAsync(AppInfo.ApiTagsUrl, token).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);

            Version? best = null;
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.TryGetProperty("name", out var name) &&
                    TryParseTag(name.GetString(), out var tag) &&
                    (best is null || tag > best))
                    best = tag;
            }

            return best is null ? null : new Result(best, best > AppInfo.Normalize(current));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                      or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Parses a "v1.2.3" (or "1.2.3") tag into a normalised version; false if it is not one.</summary>
    internal static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;

        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var parsed)) return false;

        version = AppInfo.Normalize(parsed);
        return true;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // GitHub rejects requests without a User-Agent; the JSON media type pins the API version.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RateTray");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
