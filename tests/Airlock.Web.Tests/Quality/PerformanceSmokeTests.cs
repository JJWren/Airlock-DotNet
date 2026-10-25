using System.Diagnostics;
using System.Net;
using Airlock.Application.Ports;
using Airlock.Domain.Intelligence;
using Airlock.Web.Tests.Support;

namespace Airlock.Web.Tests.Quality;

// U4-NFR-08 — bounds ten times the targets; Build and Test measures the targets themselves.
[Collection(nameof(Measurements))]
public sealed class PerformanceSmokeTests
{
    [Fact]
    public async Task Refusals_before_the_body_and_health_answer_quickly()
    {
        await using var factory = new AirlockWebFactory();
        factory.Catalog.IsConfigured = false;
        using var client = factory.CreateClient();
        using (var warm = await client.GetAsync("/api/v2/system/health", Test.Token))
        {
            Assert.Equal(HttpStatusCode.OK, warm.StatusCode);
        }

        var refusal = await TimeAsync(() => client.PostAsync(Http.ScansUrl, Http.Requirements(), Test.Token));
        var health = await TimeAsync(() => client.GetAsync("/api/v2/system/health", Test.Token));

        Assert.True(refusal < TimeSpan.FromSeconds(1), $"The refusal took {refusal.TotalMilliseconds} ms.");
        Assert.True(health < TimeSpan.FromMilliseconds(500), $"Health took {health.TotalMilliseconds} ms.");
    }

    [Fact]
    public async Task A_500_finding_scan_is_read_quickly()
    {
        await using var factory = new AirlockWebFactory();
        var packages = Enumerable.Range(0, 100).Select(i => A.Sbom($"package-{i:000}", "1.0.0")).ToArray();
        factory.Sbom.Produces(FakeSbomGenerator.Manifests, packages);
        var description = string.Join(' ', Enumerable.Repeat("A vulnerability with a long description.", 25));
        for (var i = 0; i < packages.Length; i++)
        {
            factory.Catalog.Cpe(
                A.CpeFor($"package-{i:000}", "1.0.0"),
                [.. Enumerable.Range(0, 5).Select(j => A.Cve($"CVE-2026-{(i * 10) + j + 10000}", 9.8m, Severity.Critical, description))]);
        }

        using var client = factory.CreateClient();
        var id = await client.StartScanAsync(Http.Requirements());
        var scan = await client.WaitForEndAsync(id);
        Assert.Equal(500, scan.GetProperty("summary").GetProperty("criticalFindings").GetInt32());

        var read = await TimeAsync(() => client.GetAsync($"{Http.ScansUrl}/{id}", Test.Token));

        Assert.True(read < TimeSpan.FromMilliseconds(2_500), $"Reading the scan took {read.TotalMilliseconds} ms.");
    }

    [Fact]
    public async Task An_event_reaches_its_client_quickly()
    {
        await using var factory = new AirlockWebFactory();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Sbom.ProducesAfter(FakeSbomGenerator.Manifests, release.Task, A.Sbom("pyyaml", "5.3"));
        using var client = factory.CreateClient();
        var id = await client.StartScanAsync(Http.Requirements());
        await factory.Sbom.RunStarted.Task.Within();
        using var response = await client.GetAsync($"{Http.ScansUrl}/{id}/events", HttpCompletionOption.ResponseHeadersRead, Test.Token);
        using var events = new SseReader(await response.Content.ReadAsStreamAsync(Test.Token));
        await events.NextAsync(Test.Token).Within();

        var watch = Stopwatch.StartNew();
        release.SetResult();
        while (await events.NextAsync(Test.Token).Within() is { Name: not "phase" })
        {
        }

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"The phase event took {watch.Elapsed.TotalMilliseconds} ms.");
    }

    private static async Task<TimeSpan> TimeAsync(Func<Task<HttpResponseMessage>> request)
    {
        var watch = Stopwatch.StartNew();
        using var response = await request();
        await response.Content.ReadAsByteArrayAsync(Test.Token);
        return watch.Elapsed;
    }
}
