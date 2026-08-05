using RateTray.Configuration;
using RateTray.Localization;
using RateTray.Providers;

namespace RateTray.Tests;

public class ClaudeUsageProviderTests
{
    /// <summary>
    /// Shape of a real /api/oauth/usage response with synthetic numbers. Both the top-level
    /// windows and the limits[] array are present, because the parser must use limits[] only.
    /// </summary>
    private const string Payload = """
    {
      "five_hour":  { "utilization": 6.0,  "resets_at": "2026-08-05T11:59:59.797679+00:00" },
      "seven_day":  { "utilization": 17.0, "resets_at": "2026-08-11T02:59:59.797705+00:00" },
      "seven_day_opus": null,
      "extra_usage": { "is_enabled": true, "utilization": 25.0, "currency": "EUR" },
      "limits": [
        { "kind": "session",       "percent": 6,  "severity": "normal",  "resets_at": "2026-08-05T11:59:59+00:00", "scope": null, "is_active": false },
        { "kind": "weekly_all",    "percent": 17, "severity": "normal",  "resets_at": "2026-08-11T02:59:59+00:00", "scope": null, "is_active": true },
        { "kind": "weekly_scoped", "percent": 14, "severity": "warning", "resets_at": "2026-08-11T02:59:59+00:00",
          "scope": { "model": { "id": null, "display_name": "Fable" } }, "is_active": false }
      ]
    }
    """;

    public ClaudeUsageProviderTests() => Loc.Use("en");

    [Fact]
    public void Parse_derives_ids_from_kind_and_model_scope()
    {
        var readings = ClaudeUsageProvider.Parse(Payload);

        Assert.Equal(
            ["claude.session", "claude.weekly_all", "claude.weekly_scoped.fable", "claude.extra_usage"],
            readings.Select(r => r.Id));
    }

    [Fact]
    public void Parse_reads_percent_reset_and_active_flag()
    {
        var weekly = ClaudeUsageProvider.Parse(Payload).Single(r => r.Id == "claude.weekly_all");

        Assert.Equal(17, weekly.Percent);
        Assert.True(weekly.IsActive);
        Assert.Equal(new DateTimeOffset(2026, 8, 11, 2, 59, 59, TimeSpan.Zero), weekly.ResetsAt);
        Assert.Equal(TimeSpan.FromDays(7), weekly.Window);
    }

    [Fact]
    public void Parse_labels_a_scoped_window_with_its_model_name()
    {
        var fable = ClaudeUsageProvider.Parse(Payload).Single(r => r.Id == "claude.weekly_scoped.fable");

        Assert.Contains("Fable", fable.Label);
        Assert.Equal("warning", fable.Note);
    }

    [Fact]
    public void Parse_ignores_extra_usage_when_it_is_disabled()
    {
        var withoutCredits = Payload.Replace("\"is_enabled\": true", "\"is_enabled\": false");

        Assert.DoesNotContain(ClaudeUsageProvider.Parse(withoutCredits), r => r.Id == "claude.extra_usage");
    }

    [Fact]
    public void Parse_keeps_an_unknown_kind_visible_instead_of_dropping_it()
    {
        const string payload = """
        { "limits": [ { "kind": "monthly_experiment", "percent": 3, "resets_at": null, "scope": null } ] }
        """;

        var reading = Assert.Single(ClaudeUsageProvider.Parse(payload));

        Assert.Equal("claude.monthly_experiment", reading.Id);
        Assert.Equal("monthly_experiment", reading.Label);
    }

    [Fact]
    public void Parse_disambiguates_two_windows_of_the_same_kind_and_scope()
    {
        const string payload = """
        {
          "limits": [
            { "kind": "weekly_all", "percent": 1, "resets_at": null, "scope": null },
            { "kind": "weekly_all", "percent": 2, "resets_at": null, "scope": null }
          ]
        }
        """;

        Assert.Equal(["claude.weekly_all", "claude.weekly_all.2"],
            ClaudeUsageProvider.Parse(payload).Select(r => r.Id));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "limits": [] }""")]
    [InlineData("""{ "limits": null }""")]
    public void Parse_returns_nothing_when_the_payload_carries_no_limits(string payload)
    {
        Assert.Empty(ClaudeUsageProvider.Parse(payload));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    public void Parse_throws_on_malformed_json_so_the_caller_reports_it(string payload)
    {
        // Swallowing this would show an account with zero limits instead of a failed request.
        Assert.ThrowsAny<Exception>(() => ClaudeUsageProvider.Parse(payload));
    }

    [Fact]
    public async Task An_endpoint_that_is_not_https_fails_the_poll_rather_than_sending_the_token()
    {
        var credentials = Path.Combine(Path.GetTempPath(), $"tbm-creds-{Guid.NewGuid():N}.json");
        var expires = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeMilliseconds();
        File.WriteAllText(credentials, $$"""
            { "claudeAiOauth": { "accessToken": "secret", "refreshToken": "also-secret", "expiresAt": {{expires}} } }
            """);

        try
        {
            var provider = new ClaudeUsageProvider(new ClaudeOptions
            {
                CredentialsPath = credentials,
                UsageUrl = "http://usage.example.com/api/oauth/usage",
            });

            var result = await provider.ReadAsync(CancellationToken.None);

            // The check sits in front of the socket, not after it, so nothing was sent to fail.
            Assert.False(result.Ok);
            Assert.Contains("https", result.Error);
        }
        finally
        {
            File.Delete(credentials);
        }
    }

    [Fact]
    public void A_token_url_is_only_named_while_the_refresh_can_actually_run()
    {
        var options = new ClaudeOptions { TokenUrl = "https://token.example.com/v1/oauth/token" };

        // Pointing at a setting nothing reads would teach people to ignore the line.
        Assert.Null(new ClaudeUsageProvider(options).EndpointNotice());

        options.AutoRefreshToken = true;
        Assert.Equal("Endpoint: token.example.com", new ClaudeUsageProvider(options).EndpointNotice());
    }

    [Fact]
    public void One_host_is_named_once_even_when_both_urls_point_at_it()
    {
        var provider = new ClaudeUsageProvider(new ClaudeOptions
        {
            UsageUrl = "https://mirror.example.com/api/oauth/usage",
            TokenUrl = "https://mirror.example.com/v1/oauth/token",
            AutoRefreshToken = true,
        });

        Assert.Equal("Endpoint: mirror.example.com", provider.EndpointNotice());
    }

    [Fact]
    public void The_shipped_configuration_says_nothing()
    {
        Assert.Null(new ClaudeUsageProvider(new ClaudeOptions { AutoRefreshToken = true }).EndpointNotice());
    }
}
