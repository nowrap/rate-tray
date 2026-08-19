using RateTray.Model;

namespace RateTray.Providers;

public interface IUsageProvider
{
    /// <summary>Display name, also used as <see cref="LimitReading.Group"/>.</summary>
    string Group { get; }

    bool Enabled { get; }

    /// <summary>
    /// Shortest gap this provider may be polled at, regardless of the configured refresh
    /// interval. A local process can be asked as often as the timer ticks; an endpoint with a
    /// request quota cannot, and the tray is not the only thing spending it. Zero means the
    /// refresh interval alone decides.
    /// </summary>
    TimeSpan MinInterval => TimeSpan.Zero;

    Task<ProviderResult> ReadAsync(CancellationToken ct);
}
