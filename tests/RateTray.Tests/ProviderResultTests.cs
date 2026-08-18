using RateTray.Model;

namespace RateTray.Tests;

public class ProviderResultTests
{
    /// <summary>What the Codex app-server actually answers with when the account rejects the token.</summary>
    private const string ServerAnswer = """
    failed to fetch codex rate limits: GET https://chatgpt.com/backend-api/wham/usage failed: {
      "error": {
        "message": "Provided authentication token is expired",
        "code": "token_expired"
      },
      "status": 401
    }
    """;

    [Fact]
    public void A_multi_line_error_is_collapsed_into_one_line()
    {
        var result = ProviderResult.Failed("Codex", ServerAnswer);

        Assert.NotNull(result.Error);
        Assert.DoesNotContain("\n", result.Error);
        Assert.DoesNotContain("\r", result.Error);
        Assert.DoesNotContain("  ", result.Error);
        Assert.StartsWith("failed to fetch codex rate limits", result.Error);
    }

    [Fact]
    public void A_long_error_is_cut_rather_than_painted_across_the_window()
    {
        var result = ProviderResult.Failed("Claude", new string('x', 500));

        Assert.True(result.Error!.Length <= 200, $"error kept {result.Error.Length} characters");
        Assert.EndsWith("…", result.Error);
    }

    [Fact]
    public void An_error_added_with_with_is_normalised_too()
    {
        var result = ProviderResult.Success("Codex", []) with { Error = "two\nlines" };

        Assert.Equal("two lines", result.Error);
    }

    [Fact]
    public void A_successful_result_has_no_error()
    {
        Assert.Null(ProviderResult.Success("Claude", []).Error);
        Assert.True(ProviderResult.Success("Claude", []).Ok);
    }
}
