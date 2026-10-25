using System.Net;
using Airlock.Web.Tests.Support;

namespace Airlock.Web.Tests.Security;

// WR-19 to WR-21, Q-U4-3 — POSTs from other websites refused; host names limited to AllowedHosts; no antiforgery token.
public sealed class CrossSiteTests
{
    public static TheoryData<string, string?> RefusedHeaders => new()
    {
        { "Sec-Fetch-Site", "cross-site" },
        { "Sec-Fetch-Site", "same-site" },
        { "Origin", "https://evil.example" },
        { "Origin", "http://localhost:3000" },
        { "Origin", "null" },
    };

    public static TheoryData<string> PostEndpoints => new()
    {
        "/api/v2/system/stop",
        $"/api/v2/scans/{Guid.NewGuid()}/cancel",
        Http.ScansUrl,
    };

    [Theory]
    [MemberData(nameof(RefusedHeaders))]
    public async Task A_post_a_browser_marks_as_cross_site_is_refused_on_every_post_endpoint(string header, string? value)
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();

        foreach (var url in PostEndpoints)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = Http.Requirements() };
            request.Headers.TryAddWithoutValidation(header, value);

            using var response = await client.SendAsync(request, Test.Token);

            var problem = await response.ProblemAsync(HttpStatusCode.Forbidden, "cross-site-request");
            Assert.Equal("Requests from other websites are not accepted", problem.Detail());
        }

        Assert.Empty(factory.Sbom.Calls);
        Assert.Contains(factory.Logs.GetSnapshot(), record => record.Id.Id == 3101);
    }

    [Theory]
    [InlineData("Sec-Fetch-Site", "same-origin")]
    [InlineData("Sec-Fetch-Site", "none")]
    [InlineData("Origin", "http://localhost")]
    [InlineData(null, null)]
    public async Task A_post_from_airlock_itself_or_from_a_client_that_is_not_a_browser_is_accepted(string? header, string? value)
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/system/stop");
        if (header is not null)
        {
            request.Headers.TryAddWithoutValidation(header, value);
        }

        using var response = await client.SendAsync(request, Test.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("IDLE", (await response.JsonAsync()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Get_endpoints_are_not_checked()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v2/system/health");
        request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");

        using var response = await client.SendAsync(request, Test.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_host_outside_allowed_hosts_is_refused_before_any_api_rule()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v2/system/health");
        request.Headers.Host = "attacker.example";

        using var response = await client.SendAsync(request, Test.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    public async Task The_default_hosts_are_answered(string host)
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v2/system/health");
        request.Headers.Host = host + ":8080";

        using var response = await client.SendAsync(request, Test.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_operator_can_add_a_host_name()
    {
        await using var factory = new AirlockWebFactory(settings => settings["AllowedHosts"] = "localhost;airlock");
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v2/system/health");
        request.Headers.Host = "airlock:8080";

        using var response = await client.SendAsync(request, Test.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
