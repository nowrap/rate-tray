using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using RateTray.Model;

namespace RateTray.Configuration;

/// <summary>Last readings that arrived from one provider, and when.</summary>
public sealed record CachedReadings(DateTimeOffset FetchedAt, IReadOnlyList<LimitReading> Readings);

/// <summary>
/// Persists the last successful readings so the tray shows numbers the moment it starts,
/// instead of a row of "?" while the first poll runs — which matters most in exactly the
/// situations where a poll is slow or failing: a rate-limited endpoint, an expired sign-in,
/// no network.
///
/// Only values are stored — limit ids, percentages, reset times, plan names. No credentials
/// ever reach this file.
/// </summary>
public static class UsageCache
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Readings older than this are dropped: a stale reset time is worse than none.</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(2);

    public static string DefaultPath => Path.Combine(ConfigStore.Directory, "cache.json");

    /// <summary>Never throws — a missing or corrupt cache simply means starting empty.</summary>
    public static Dictionary<string, CachedReadings> Load(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            if (!File.Exists(file)) return [];

            var loaded = JsonSerializer.Deserialize<Dictionary<string, CachedReadings>>(
                File.ReadAllText(file), Options);
            if (loaded is null) return [];

            return loaded
                .Where(entry => DateTimeOffset.Now - entry.Value.FetchedAt <= MaxAge)
                .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or SecurityException or JsonException or NotSupportedException)
        {
            return [];
        }
    }

    public static void Save(IReadOnlyDictionary<string, CachedReadings> entries, string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);

            var temp = file + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(entries, Options));
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or SecurityException or NotSupportedException)
        {
            // A cache that cannot be written is a missing optimisation, not a failure.
        }
    }
}
