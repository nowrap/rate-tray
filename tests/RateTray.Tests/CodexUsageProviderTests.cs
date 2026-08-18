using System.Text.Json.Nodes;
using RateTray.Localization;
using RateTray.Providers;

namespace RateTray.Tests;

public class CodexUsageProviderTests
{
    /// <summary>Shape of an account/rateLimits/read result with synthetic numbers.</summary>
    private const string Payload = """
    {
      "rateLimits": {
        "limitId": "codex",
        "primary":   { "usedPercent": 6,  "windowDurationMins": 10080, "resetsAt": 1786468203 },
        "secondary": { "usedPercent": 41, "windowDurationMins": 300,   "resetsAt": 1786400000 },
        "planType": "plus"
      },
      "rateLimitsByLimitId": {
        "codex":     { "limitId": "codex",     "primary": { "usedPercent": 6, "windowDurationMins": 10080, "resetsAt": 1786468203 }, "planType": "plus" },
        "codex-max": { "limitId": "codex-max", "primary": { "usedPercent": 3, "windowDurationMins": 1440,  "resetsAt": 1786468203 }, "planType": "pro" }
      }
    }
    """;

    private static JsonObject Json(string text) => JsonNode.Parse(text)!.AsObject();

    public CodexUsageProviderTests() => Loc.Use("en");

    [Fact]
    public void Parse_reads_both_windows_of_the_default_bucket()
    {
        var readings = CodexUsageProvider.Parse(Json(Payload), out var plan);

        Assert.Equal("plus", plan);
        Assert.Contains(readings, r => r.Id == "codex.primary" && r.Percent == 6);
        Assert.Contains(readings, r => r.Id == "codex.secondary" && r.Percent == 41);
    }

    [Fact]
    public void Parse_marks_the_primary_window_as_active()
    {
        var readings = CodexUsageProvider.Parse(Json(Payload), out _);

        Assert.True(readings.Single(r => r.Id == "codex.primary").IsActive);
        Assert.False(readings.Single(r => r.Id == "codex.secondary").IsActive);
    }

    [Fact]
    public void Parse_converts_window_minutes_and_unix_reset()
    {
        var primary = CodexUsageProvider.Parse(Json(Payload), out _).Single(r => r.Id == "codex.primary");

        Assert.Equal(TimeSpan.FromDays(7), primary.Window);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1786468203), primary.ResetsAt);
    }

    [Fact]
    public void Parse_adds_extra_buckets_without_duplicating_the_default_one()
    {
        var readings = CodexUsageProvider.Parse(Json(Payload), out _);

        Assert.Single(readings, r => r.Id == "codex.primary");
        Assert.Contains(readings, r => r.Id == "codex.codex_max.primary" && r.Percent == 3);
    }

    [Fact]
    public void Parse_skips_a_window_without_a_used_percentage()
    {
        const string payload = """
        { "rateLimits": { "limitId": "codex", "primary": { "windowDurationMins": 10080 }, "planType": "plus" } }
        """;

        Assert.Empty(CodexUsageProvider.Parse(Json(payload), out _));
    }

    [Fact]
    public void Parse_tolerates_a_missing_window_duration()
    {
        const string payload = """
        { "rateLimits": { "limitId": "codex", "primary": { "usedPercent": 12, "resetsAt": null } } }
        """;

        var reading = Assert.Single(CodexUsageProvider.Parse(Json(payload), out _));

        Assert.Null(reading.Window);
        Assert.Null(reading.ResetsAt);
        Assert.Equal(12, reading.Percent);
    }

    [Fact]
    public void Parse_returns_nothing_for_an_empty_result()
    {
        Assert.Empty(CodexUsageProvider.Parse(Json("{}"), out var plan));
        Assert.Null(plan);
    }

    /// <summary>An app-server error handing the upstream HTTP body back verbatim.</summary>
    private const string ServerAnswer = """
    failed to fetch codex rate limits: GET https://chatgpt.com/backend-api/wham/usage failed: {
      "error": {
        "message": "Provided authentication token is expired",
        "type": null,
        "code": "token_expired",
        "param": null
      },
      "status": 401
    }
    """;

    [Fact]
    public void A_server_error_keeps_its_sentence_and_drops_the_payload()
    {
        var summary = CodexServerException.Summarize(ServerAnswer);

        Assert.Equal(
            "failed to fetch codex rate limits: GET https://chatgpt.com/backend-api/wham/usage failed: "
            + "Provided authentication token is expired",
            summary);
    }

    [Fact]
    public void A_server_error_without_a_payload_is_passed_on_unchanged()
    {
        Assert.Equal("account not authenticated", CodexServerException.Summarize("account not authenticated"));
    }

    [Fact]
    public void An_unreadable_payload_leaves_the_part_the_app_server_wrote()
    {
        Assert.Equal("request failed", CodexServerException.Summarize("request failed: {not json at all"));
    }

    [Fact]
    public void A_refused_token_is_recognised_as_an_expired_sign_in()
    {
        Assert.True(new CodexServerException(ServerAnswer).TokenExpired);
        Assert.False(new CodexServerException("app-server exited").TokenExpired);
    }
}
