using System.Net;
using Airlock.Application.Ports;
using Airlock.Web.Tests.Support;

namespace Airlock.Web.Tests.Endpoints;

// WR-17 — readiness as GetSystemHealth reports it, always 200, never calling NVD.
public sealed class HealthEndpointTests
{
    [Theory]
    [InlineData(true, true, "ONLINE")]
    [InlineData(false, true, "DEGRADED")]
    [InlineData(true, false, "DEGRADED")]
    [InlineData(false, false, "DEGRADED")]
    public async Task Health_reports_both_checks_and_answers_200_either_way(bool keyConfigured, bool syftAvailable, string status)
    {
        await using var factory = new AirlockWebFactory();
        factory.Catalog.IsConfigured = keyConfigured;
        if (!syftAvailable)
        {
            factory.Sbom.Status = new SbomToolStatus(false, null, "Syft not found at /opt/airlock/bin/syft");
        }

        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v2/system/health", Test.Token);
        var health = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(status, health.GetProperty("status").GetString());
        Assert.Equal("2.0.0", health.GetProperty("version").GetString());
        var checks = health.GetProperty("checks").EnumerateArray().ToList();
        Assert.Equal(["Syft", "NVD API key"], checks.Select(check => check.GetProperty("name").GetString()));
        Assert.Equal(syftAvailable, checks[0].GetProperty("passing").GetBoolean());
        Assert.Equal(syftAvailable ? "1.55.0" : "Syft not found at /opt/airlock/bin/syft", checks[0].GetProperty("detail").GetString());
        Assert.Equal(keyConfigured, checks[1].GetProperty("passing").GetBoolean());
        Assert.Equal(keyConfigured ? "configured" : "NVD API key not configured", checks[1].GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Health_never_calls_nvd()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            using var response = await client.GetAsync("/api/v2/system/health", Test.Token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(0, factory.Catalog.Lookups);
    }
}
