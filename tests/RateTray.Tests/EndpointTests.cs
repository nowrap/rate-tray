using RateTray.Configuration;

namespace RateTray.Tests;

/// <summary>
/// The rule is about the transport, not the host: a token may not leave over plain http, but it
/// may go to an endpoint other than the shipped one, because being able to correct a moved
/// endpoint without a rebuild is why these are settings in the first place.
/// </summary>
public class EndpointTests
{
    [Theory]
    [InlineData("https://api.anthropic.com/api/oauth/usage")]
    [InlineData("https://usage.internal.example.com/oauth/usage")]
    [InlineData("http://localhost:8080/usage")]
    [InlineData("http://127.0.0.1:8080/usage")]
    [InlineData("http://[::1]:8080/usage")]
    public void A_credential_may_travel_over_tls_or_stay_on_the_machine(string url) =>
        Assert.True(Endpoint.IsSecure(url));

    [Theory]
    [InlineData("http://api.anthropic.com/api/oauth/usage")]   // the typo this exists to catch
    [InlineData("http://192.168.1.10/usage")]                  // not loopback: it leaves the machine
    [InlineData("ftp://example.com/usage")]
    [InlineData("api.anthropic.com/usage")]                    // not absolute
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_refused(string? url) => Assert.False(Endpoint.IsSecure(url));

    [Fact]
    public void The_shipped_endpoint_is_not_worth_pointing_out()
    {
        Assert.Null(Endpoint.ForeignHost(ClaudeOptions.UsageUrlDefault, ClaudeOptions.UsageUrlDefault));
        Assert.Null(Endpoint.ForeignHost(ClaudeOptions.TokenUrlDefault, ClaudeOptions.TokenUrlDefault));
    }

    [Fact]
    public void A_different_path_on_the_same_api_is_a_version_change_not_a_destination()
    {
        // Flagging this would train people to ignore the notice that matters.
        Assert.Null(Endpoint.ForeignHost("https://api.anthropic.com/api/oauth/usage_v2", ClaudeOptions.UsageUrlDefault));
    }

    [Fact]
    public void A_different_host_is_named_so_it_cannot_be_a_silent_redirection()
    {
        Assert.Equal(
            "usage.example.com",
            Endpoint.ForeignHost("https://usage.example.com/api/oauth/usage", ClaudeOptions.UsageUrlDefault));
    }

    [Fact]
    public void The_host_comparison_ignores_case_as_dns_does()
    {
        Assert.Null(Endpoint.ForeignHost("https://API.Anthropic.COM/api/oauth/usage", ClaudeOptions.UsageUrlDefault));
    }

    [Fact]
    public void An_unparseable_url_is_not_reported_as_a_foreign_host()
    {
        // It is refused by IsSecure instead, which is the error worth showing.
        Assert.Null(Endpoint.ForeignHost("not a url", ClaudeOptions.UsageUrlDefault));
    }
}
