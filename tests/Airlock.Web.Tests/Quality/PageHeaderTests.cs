using System.Text.RegularExpressions;
using Airlock.Web.Security;
using Airlock.Web.Tests.Support;

namespace Airlock.Web.Tests.Quality;

// U5-NFR-08, U5-NFR-11: the pages cannot be framed and carry the policy; /docs keeps no policy for Scalar's inline
// script; /api keeps U4's headers; the static files are fingerprinted and served by Airlock itself.
public sealed partial class PageHeaderTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/logs")]
    [InlineData("/reports")]
    [InlineData("/reports/01a1637b-7e00-7c3a-9f12-6b84d0e5a1c7")]
    [InlineData("/nowhere")]
    public async Task Pages_cannot_be_framed_and_carry_the_policy(string address)
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(address, Test.Token);

        Assert.Equal("DENY", Single(response, "X-Frame-Options"));
        Assert.Equal("nosniff", Single(response, "X-Content-Type-Options"));
        Assert.Equal(PageSecurityHeaders.ContentSecurityPolicy, Single(response, "Content-Security-Policy"));
        Assert.Contains("frame-ancestors 'none'", PageSecurityHeaders.ContentSecurityPolicy, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/docs")]
    [InlineData("/openapi/v2.json")]
    public async Task The_api_documentation_keeps_no_policy_but_cannot_be_framed(string address)
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(address, Test.Token);

        Assert.Equal("DENY", Single(response, "X-Frame-Options"));
        Assert.Equal("nosniff", Single(response, "X-Content-Type-Options"));
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task The_api_keeps_its_own_headers()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v2/system/health", Test.Token);

        Assert.Equal("nosniff", Single(response, "X-Content-Type-Options"));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
        Assert.False(response.Headers.Contains("X-Frame-Options"));
    }

    [Fact]
    public async Task The_shell_loads_fingerprinted_files_from_airlock_only()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();

        var shell = await client.GetStringAsync("/", Test.Token);

        var addresses = Address().Matches(shell).Select(match => match.Groups["address"].Value).ToList();
        Assert.Contains(addresses, address => FingerprintedStylesheet().IsMatch(address));
        Assert.Contains(addresses, address => address.StartsWith("_content/MudBlazor/MudBlazor.min.", StringComparison.Ordinal));
        Assert.All(addresses, address => Assert.DoesNotContain("//", address, StringComparison.Ordinal));
        Assert.DoesNotContain("importmap", shell, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("<script>[^<]", shell);

        // Airlock's own files; those of packages and the framework are static web assets, which a host run from its
        // build output serves in Development only.
        foreach (var address in addresses.Where(address => !address.StartsWith('_') && address != "/"))
        {
            using var asset = await client.GetAsync("/" + address.TrimStart('/'), Test.Token);
            Assert.True(asset.IsSuccessStatusCode, $"{address} answered {(int)asset.StatusCode}.");
        }
    }

    private static string Single(HttpResponseMessage response, string header) =>
        Assert.Single(response.Headers.TryGetValues(header, out var values) ? values : []);

    [GeneratedRegex("(?:src|href)=\"(?<address>[^\"]+)\"")]
    private static partial Regex Address();

    [GeneratedRegex(@"^css/airlock\.[a-z0-9]+\.css$")]
    private static partial Regex FingerprintedStylesheet();
}