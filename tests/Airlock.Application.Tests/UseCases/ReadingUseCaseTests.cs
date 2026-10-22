using Airlock.Application.Events;
using Airlock.Application.Options;
using Airlock.Application.Outcomes;
using Airlock.Application.Ports;
using Airlock.Application.Scanning;
using Airlock.Application.Tests.Fakes;
using Airlock.Application.UseCases;
using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;

namespace Airlock.Application.Tests.UseCases;

// AR-33: retention.
public sealed class RetentionTests
{
    private static readonly RetentionOptions Retention = new();

    [Fact]
    public void A_scan_finished_exactly_24_hours_ago_is_kept_and_one_finished_longer_ago_is_removed()
    {
        var now = ScanHarness.StartTime.AddDays(2);
        var kept = Finished(now.AddHours(-24));
        var removed = Finished(now.AddHours(-24).AddMilliseconds(-1));

        var toRemove = RetentionPolicy.ScansToRemove([kept, removed], now, Retention);

        Assert.Equal([removed.Id], toRemove);
    }

    [Fact]
    public void Beyond_twenty_finished_scans_the_earliest_finished_goes_and_running_scans_stay()
    {
        var now = ScanHarness.StartTime.AddHours(1);
        var finished = Enumerable.Range(0, 21).Select(i => Finished(ScanHarness.StartTime.AddMinutes(i))).ToList();
        var running = Scan.Start(ScanId.New(), new DirectoryTarget("/uploads"), ScanHarness.StartTime);

        var toRemove = RetentionPolicy.ScansToRemove([running, .. finished], now, Retention);

        Assert.Equal([finished[0].Id], toRemove);
    }

    [Fact]
    public async Task Lookups_prune_first()
    {
        using var harness = new ScanHarness();
        var scan = await harness.RunDirectoryScanAsync();
        Assert.NotNull(harness.GetScan.Execute(scan.Id));

        harness.Clock.Advance(TimeSpan.FromHours(25));

        Assert.Null(harness.GetScan.Execute(scan.Id));
        Assert.Empty(await harness.CollectEventsAsync(scan.Id));
    }

    private static Scan Finished(DateTimeOffset at) =>
        Scan.Start(ScanId.New(), new DirectoryTarget("/uploads"), at.AddMinutes(-1)).Finish(0, [], [], at);
}

// AR-34: report export.
public sealed class RenderScanReportTests
{
    [Fact]
    public async Task A_completed_scan_is_rendered_and_named_after_its_finish_time()
    {
        using var harness = new ScanHarness();
        var scan = await harness.RunDirectoryScanAsync();

        var report = await harness.RenderScanReport.ExecuteAsync(scan.Id, harness.Token);

        Assert.Equal("airlock_audit_2026-10-22T12-00-00-000Z.pdf", report.Value!.FileName);
        Assert.Equal("application/pdf", report.Value.ContentType);
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44 }, report.Value.Content.ToArray());
        Assert.Equal(scan.Id, Assert.Single(harness.Renderer.Rendered).Id);
    }

    [Fact]
    public async Task An_unknown_a_running_a_failed_and_a_cancelled_scan_are_refused()
    {
        using var harness = new ScanHarness();
        Assert.Equal(RefusalCode.ScanNotFound, (await harness.RenderScanReport.ExecuteAsync(ScanId.New(), harness.Token)).Refusal!.Code);

        harness.Sbom.BlocksUntilCancelled(ScanHarness.Directory);
        var running = await harness.StartDirectoryScanAsync();
        var whileRunning = await harness.RenderScanReport.ExecuteAsync(running.Id, harness.Token);
        await harness.EmergencyStop.ExecuteAsync(harness.Token);
        await harness.WaitForEndAsync(running.Id);
        var cancelled = await harness.RenderScanReport.ExecuteAsync(running.Id, harness.Token);

        harness.Sbom.Fails("/uploads/broken", new ScanFailure(ScanFailureCode.SyftFailed, "Syft exited with code 1"));
        harness.FileSystem.WithDirectory("/uploads/broken");
        var failed = await harness.RunDirectoryScanAsync("/uploads/broken");
        var afterFailure = await harness.RenderScanReport.ExecuteAsync(failed.Id, harness.Token);

        Assert.Equal(RefusalCode.ScanNotFinished, whileRunning.Refusal!.Code);
        Assert.Equal("The scan is still running", whileRunning.Refusal.Message);
        Assert.Equal(RefusalCode.ScanHasNoResults, cancelled.Refusal!.Code);
        Assert.Equal("The scan ended without results", cancelled.Refusal.Message);
        Assert.Equal(RefusalCode.ScanHasNoResults, afterFailure.Refusal!.Code);
        Assert.Empty(harness.Renderer.Rendered);
    }
}

// AR-35: health.
public sealed class GetSystemHealthTests
{
    [Fact]
    public async Task With_Syft_and_a_key_Airlock_is_online()
    {
        using var harness = new ScanHarness();

        var health = await harness.GetSystemHealth.ExecuteAsync(harness.Token);

        Assert.Equal(HealthStatus.Online, health.Status);
        Assert.Equal([new HealthCheck("Syft", true, "1.55.0"), new HealthCheck("NVD API key", true, "configured")], health.Checks);
        Assert.Equal(0, harness.Catalog.CallCount);
    }

    [Fact]
    public async Task Without_Syft_or_a_key_Airlock_is_degraded_and_says_why()
    {
        using var harness = new ScanHarness();
        harness.Sbom.Status = new SbomToolStatus(false, null, "Syft not found at /opt/airlock/bin/syft");
        harness.Catalog.IsConfigured = false;

        var health = await harness.GetSystemHealth.ExecuteAsync(harness.Token);

        Assert.Equal(HealthStatus.Degraded, health.Status);
        Assert.Equal(
            [
                new HealthCheck("Syft", false, "Syft not found at /opt/airlock/bin/syft"),
                new HealthCheck("NVD API key", false, "NVD API key not configured"),
            ],
            health.Checks);
    }

    [Fact]
    public async Task A_Syft_without_a_reported_version_shows_its_detail()
    {
        using var harness = new ScanHarness();
        harness.Sbom.Status = new SbomToolStatus(true, null, "Syft found");

        var health = await harness.GetSystemHealth.ExecuteAsync(harness.Token);

        Assert.Equal("Syft found", health.Checks[0].Detail);
    }
}

// The Live Log use case passes the port's stream through (FR-18).
public sealed class WatchLiveLogTests
{
    [Fact]
    public async Task Subscribers_receive_the_Live_Log()
    {
        using var harness = new ScanHarness();
        var liveLog = new FakeLiveLog();
        var entry = new LiveLogEntry(ScanHarness.StartTime, Microsoft.Extensions.Logging.LogLevel.Information, "Airlock", "📡 NVD Service initialized.", null);
        liveLog.Write(entry);

        await using var entries = new WatchLiveLog(liveLog).SubscribeAsync(harness.Token).GetAsyncEnumerator(harness.Token);

        Assert.True(await entries.MoveNextAsync());
        Assert.Equal(entry, entries.Current);
    }
}
