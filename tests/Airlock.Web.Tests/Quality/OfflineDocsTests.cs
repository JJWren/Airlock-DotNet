using System.Net;
using System.Text.RegularExpressions;
using Airlock.Web.Tests.Support;

namespace Airlock.Web.Tests.Quality;

// U4-NFR-07 — /docs is served from Airlock itself and sends nothing anywhere: no font CDN, no telemetry, no AI agent,
// no developer toolbar, no proxy. Build and Test confirms it from a browser's network log.
public sealed partial class OfflineDocsTests
{
    [Fact]
    public async Task The_docs_page_names_no_other_host_and_switches_off_what_would_call_out()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/docs/", Test.Token);
        var page = await response.Content.ReadAsStringAsync(Test.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotMatch("https?://", page);
        Assert.Contains("\"withDefaultFonts\":false", page, StringComparison.Ordinal);
        Assert.Contains("\"telemetry\":false", page, StringComparison.Ordinal);
        Assert.Contains("\"agent\":{\"disabled\":true", page, StringComparison.Ordinal);
        Assert.Contains("\"showDeveloperTools\":\"never\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("proxyUrl", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_scripts_the_page_loads_come_from_airlock()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        var page = await client.GetStringAsync("/docs/", Test.Token);

        var scripts = Script().Matches(page).Select(match => match.Groups[1].Value).ToList();

        Assert.NotEmpty(scripts);
        foreach (var script in scripts)
        {
            Assert.DoesNotContain("//", script, StringComparison.Ordinal);
            using var asset = await client.GetAsync("/docs/" + script, Test.Token);
            Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        }
    }

    [GeneratedRegex("<script[^>]*src=\"([^\"]+)\"")]
    private static partial Regex Script();
}
