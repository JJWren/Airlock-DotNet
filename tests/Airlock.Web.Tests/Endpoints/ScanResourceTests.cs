using System.Net;
using Airlock.Domain.Intelligence;
using Airlock.Domain.Scanning;
using Airlock.Web.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Airlock.Web.Tests.Endpoints;

// WR-01, WR-10 — the scan resource in each state, the ids it answers to, and retention (E03, E06).
public sealed class ScanResourceTests
{
    [Fact]
    public async Task A_running_scan_shows_its_status_phase_and_target()
    {
        await using var factory = new AirlockWebFactory();
        factory.Sbom.BlocksUntilCancelled(FakeSbomGenerator.Manifests);
        using var client = factory.CreateClient();
        var id = await client.StartScanAsync(Http.Requirements());
        await factory.Sbom.RunStarted.Task.Within();

        var scan = await client.GetScanAsync(id);

        Assert.Equal("RUNNING", scan.GetProperty("status").GetString());
        Assert.Equal("SBOM_GENERATION", scan.GetProperty("phase").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, scan.GetProperty("progress").ValueKind);
        var artifact = scan.GetProperty("target").GetProperty("artifacts")[0];
        Assert.Equal("requirements.txt", artifact.GetProperty("fileName").GetString());
        Assert.Equal(56, artifact.GetProperty("sizeBytes").GetInt64());
        Assert.Equal("MANIFEST", artifact.GetProperty("kind").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, scan.GetProperty("summary").ValueKind);
        using var stop = await client.PostAsync("/api/v2/system/stop", null, Test.Token);
        await client.WaitForEndAsync(id);
    }

    [Fact]
    public async Task A_finished_scan_carries_its_summary_and_its_packages()
    {
        await using var factory = new AirlockWebFactory();
        factory.Sbom.Produces(FakeSbomGenerator.Manifests, A.Sbom("pyyaml", "5.3"), A.Sbom("requests", "2.25.1"));
        factory.Catalog.Cpe(A.CpeFor("pyyaml", "5.3"), A.Cve("CVE-2020-14343"), A.Cve("CVE-2020-1747"));
        factory.Catalog.Cpe(A.CpeFor("requests", "2.25.1"), A.Cve("CVE-2023-32681", 6.1m, Severity.Medium));
        using var client = factory.CreateClient();

        var scan = await client.WaitForEndAsync(await client.StartScanAsync(Http.Requirements()));

        Assert.Equal("COMPLETED", scan.GetProperty("status").GetString());
        var summary = scan.GetProperty("summary");
        Assert.Equal(2, summary.GetProperty("totalPackages").GetInt32());
        Assert.Equal(1, summary.GetProperty("vulnerablePackages").GetInt32());
        Assert.Equal(1, summary.GetProperty("cleanPackages").GetInt32());
        Assert.Equal(2, summary.GetProperty("criticalFindings").GetInt32());
        var pyyaml = scan.GetProperty("vulnerablePackages")[0];
        Assert.Equal("pyyaml", pyyaml.GetProperty("name").GetString());
        Assert.Equal("CRITICAL", pyyaml.GetProperty("highestSeverity").GetString());
        Assert.Equal("CPE", pyyaml.GetProperty("matchMethod").GetProperty("kind").GetString());
        Assert.Equal(["CVE-2020-14343", "CVE-2020-1747"], pyyaml.GetProperty("findings").EnumerateArray().Select(finding => finding.GetProperty("cveId").GetString()));
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, scan.GetProperty("finishedAt").ValueKind);
    }

    [Fact]
    public async Task A_scan_with_a_failed_lookup_is_incomplete_and_lists_it()
    {
        await using var factory = new AirlockWebFactory();
        factory.Sbom.Produces(FakeSbomGenerator.Manifests, A.Sbom("jinja2", "2.10"));
        factory.Catalog.FailsFor(A.CpeFor("jinja2", "2.10"), "NVD returned 503");
        using var client = factory.CreateClient();

        var scan = await client.WaitForEndAsync(await client.StartScanAsync(Http.Requirements()));

        Assert.Equal("INCOMPLETE", scan.GetProperty("status").GetString());
        var unverified = scan.GetProperty("unverifiedPackages")[0];
        Assert.Equal("jinja2", unverified.GetProperty("name").GetString());
        Assert.Equal("LOOKUP_FAILED", unverified.GetProperty("reason").GetString());
        Assert.Equal("NVD returned 503", unverified.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_failed_scan_carries_its_failure_and_no_results()
    {
        await using var factory = new AirlockWebFactory();
        factory.Sbom.Fails(FakeSbomGenerator.Manifests, new ScanFailure(ScanFailureCode.SyftFailed, "Syft exited with code 1: unreadable input"));
        using var client = factory.CreateClient();

        var scan = await client.WaitForEndAsync(await client.StartScanAsync(Http.Requirements()));

        Assert.Equal("FAILED", scan.GetProperty("status").GetString());
        Assert.Equal("SYFT_FAILED", scan.GetProperty("failure").GetProperty("code").GetString());
        Assert.StartsWith("Syft exited with code 1", scan.GetProperty("failure").GetProperty("reason").GetString(), StringComparison.Ordinal);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, scan.GetProperty("summary").ValueKind);
        Assert.Empty(scan.GetProperty("vulnerablePackages").EnumerateArray());
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("01a1637b7e007c3a9f126b84d0e5a1c7")]
    [InlineData("01a1637b-7e00-7c3a-9f12-6b84d0e5a1c7")]
    public async Task An_id_that_is_no_retained_scan_is_not_found(string id)
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();

        foreach (var url in new[] { $"{Http.ScansUrl}/{id}", $"{Http.ScansUrl}/{id}/events", $"{Http.ScansUrl}/{id}/report.pdf" })
        {
            using var response = await client.GetAsync(url, Test.Token);
            Assert.Equal("Scan not found or no longer retained", (await response.ProblemAsync(HttpStatusCode.NotFound, "scan-not-found")).Detail());
        }

        using var cancel = await client.PostAsync($"{Http.ScansUrl}/{id}/cancel", null, Test.Token);
        Assert.Equal("Scan not found or no longer retained", (await cancel.ProblemAsync(HttpStatusCode.NotFound, "scan-not-found")).Detail());
    }

    [Fact]
    public async Task A_scan_finished_more_than_a_day_ago_is_not_found()
    {
        var clock = new FakeTimeProvider(Test.StartTime);
        await using var factory = new AirlockWebFactory(clock: clock);
        using var client = factory.CreateClient();
        var id = await client.StartScanAsync(Http.Requirements());
        await client.WaitForEndAsync(id);

        clock.Advance(TimeSpan.FromHours(25));

        using var response = await client.GetAsync($"{Http.ScansUrl}/{id}", Test.Token);
        await response.ProblemAsync(HttpStatusCode.NotFound, "scan-not-found");
    }
}
