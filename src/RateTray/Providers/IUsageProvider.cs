using RateTray.Model;

namespace RateTray.Providers;

public interface IUsageProvider
{
    /// <summary>Display name, also used as <see cref="LimitReading.Group"/>.</summary>
    string Group { get; }

    bool Enabled { get; }

    Task<ProviderResult> ReadAsync(CancellationToken ct);
}
