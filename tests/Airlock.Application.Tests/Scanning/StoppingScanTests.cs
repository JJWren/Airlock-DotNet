using Airlock.Application.Events;
using Airlock.Application.Options;
using Airlock.Application.Outcomes;
using Airlock.Application.Tests.Fakes;
using Airlock.Domain.Scanning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Airlock.Application.Tests.Scanning;

// AR-27 to AR-32; defect 3 (Emergency Stop left NVD running).
public sealed class StoppingScanTests
{
    [Fact]
    public async Task Cancelling_a_running_scan_records_Cancelled_by_request()
    {
        using var harness = new ScanHarness();
        harness.Sbom.BlocksUntilCancelled(ScanHarness.Directory);
        var scan = await harness.StartDirectoryScanAsync();
        await harness.Sbom.RunStarted.Task.WaitAsync(harness.Token);

        var cancelled = await harness.CancelScan.ExecuteAsync(scan.Id, harness.Token);

        Assert.Equal(ScanStatus.Cancelled, cancelled.Value!.Status);
        Assert.Equal("Cancelled by request", cancelled.Value.CancellationReason);
        Assert.Null(cancelled.Value.Summary);
        await harness.WaitForIdleAsync();
    }

    [Fact]
    public async Task Cancel_waits_at_most_the_grace_period()
    {
        using var harness = new ScanHarness();
        var release = new TaskCompletionSource();
        harness.Sbom.IgnoresCancellation(ScanHarness.Directory, release.Task);
        var scan = await harness.StartDirectoryScanAsync();
        await harness.Sbom.RunStarted.Task.WaitAsync(harness.Token);

        var cancelling = harness.CancelScan.ExecuteAsync(scan.Id, harness.Token).AsTask();
        harness.Clock.Advance(TimeSpan.FromSeconds(5));
        var returned = await cancelling;

        Assert.Equal(ScanStatus.Running, returned.Value!.Status);
        release.SetResult();
        var ended = await harness.WaitForEndAsync(scan.Id);
        Assert.Equal(ScanStatus.Cancelled, ended.Status);
    }

    [Fact]
    public async Task Cancelling_an_unknown_or_finished_scan_is_refused()
    {
        using var harness = new ScanHarness();
        var scan = await harness.RunDirectoryScanAsync();

        var finished = await harness.CancelScan.ExecuteAsync(scan.Id, harness.Token);
        var unknown = await harness.CancelScan.ExecuteAsync(ScanId.New(), harness.Token);

        Assert.Equal(RefusalCode.ScanAlreadyFinished, finished.Refusal!.Code);
        Assert.Equal("The scan has already finished", finished.Refusal.Message);
        Assert.Equal(RefusalCode.ScanNotFound, unknown.Refusal!.Code);
        Assert.Equal("Scan not found or no longer retained", unknown.Refusal.Message);
    }

    [Fact]
    public async Task Emergency_Stop_during_SBOM_generation_ends_Syft_and_cancels_the_scan()
    {
        using var harness = new ScanHarness();
        harness.Sbom.BlocksUntilCancelled(ScanHarness.Directory);
        var scan = await harness.StartDirectoryScanAsync();
        await harness.Sbom.RunStarted.Task.WaitAsync(harness.Token);

        var stopped = await harness.EmergencyStop.ExecuteAsync(harness.Token);
        var ended = await harness.WaitForEndAsync(scan.Id);

        Assert.Equal(new Airlock.Application.UseCases.EmergencyStopResult(true, scan.Id), stopped);
        Assert.Equal(ScanStatus.Cancelled, ended.Status);
        Assert.Equal("Emergency Stop", ended.CancellationReason);
        Assert.Contains(harness.LogRecords, record => record.Message == $"🛑 EMERGENCY STOP: Terminating scan {scan.Id}");
        Assert.Contains(harness.LogRecords, record => record.Message == "🚨 CRITICAL: Sequence aborted. System resources released.");
    }

    [Fact]
    public async Task Emergency_Stop_during_correlation_sends_no_further_lookups()
    {
        using var harness = new ScanHarness(configure: options => options.Correlation.MaxParallelism = 1);
        var packages = Enumerable.Range(0, 87)
            .Select(i => A.SbomPackage($"{(char)('a' + (i / 26))}{(char)('a' + (i % 26))}", "1.0", "npm", $"vendor:p{i}"))
            .ToArray();
        harness.Sbom.Produces(ScanHarness.Directory, packages);
        harness.Catalog.BlockFromCall(31);
        var scan = await harness.StartDirectoryScanAsync();
        await harness.Catalog.Blocked.Task.WaitAsync(harness.Token);

        await harness.EmergencyStop.ExecuteAsync(harness.Token);
        var ended = await harness.WaitForEndAsync(scan.Id);

        Assert.Equal(31, harness.Catalog.CallCount);
        Assert.Equal(ScanStatus.Cancelled, ended.Status);
        Assert.Equal(new ScanProgress(30, 87), ended.Progress);
        Assert.Null(ended.Summary);
        Assert.Empty(ended.VulnerablePackages);
    }

    [Fact]
    public async Task Emergency_Stop_during_an_upload_ends_it_and_records_nothing()
    {
        using var harness = new ScanHarness();
        await using var reservation = await harness.ReserveAsync();
        using var stalled = new StalledStream();
        var adding = reservation.AddArtifactAsync("app-image.tar", stalled, harness.Token).AsTask();
        await stalled.Reading.Task.WaitAsync(harness.Token);

        var stopped = await harness.EmergencyStop.ExecuteAsync(harness.Token);
        var added = await adding;

        Assert.Equal(new Airlock.Application.UseCases.EmergencyStopResult(true, reservation.Id), stopped);
        Assert.Equal(RefusalCode.ReservationStopped, added.Refusal!.Code);
        Assert.Equal("Emergency Stop ended the upload before the scan started", added.Refusal.Message);
        Assert.False(harness.WatchActiveScan.Current.IsActive);
        Assert.Equal(RefusalCode.ReservationStopped, (await reservation.StartAsync(null, harness.Token)).Refusal!.Code);
        await reservation.DisposeAsync();
        Assert.True(Assert.Single(harness.Staging.Created).Disposed);
        Assert.Empty(harness.Registry.List());
    }

    [Fact]
    public async Task Emergency_Stop_with_nothing_active_is_idle()
    {
        using var harness = new ScanHarness();

        var stopped = await harness.EmergencyStop.ExecuteAsync(harness.Token);

        Assert.False(stopped.Stopped);
        Assert.Null(stopped.ScanId);
    }

    [Fact]
    public async Task A_scan_still_running_at_the_time_limit_fails()
    {
        using var harness = new ScanHarness();
        harness.Sbom.BlocksUntilCancelled(ScanHarness.Directory);
        var scan = await harness.StartDirectoryScanAsync();
        await harness.Sbom.RunStarted.Task.WaitAsync(harness.Token);

        harness.Clock.Advance(TimeSpan.FromMinutes(60));
        var ended = await harness.WaitForEndAsync(scan.Id);

        Assert.Equal(ScanStatus.Failed, ended.Status);
        Assert.Equal(ScanFailureCode.ScanTimedOut, ended.Failure!.Code);
        Assert.Equal("Scan exceeded the 60-minute limit", ended.Failure.Reason);
    }

    [Fact]
    public async Task Shutting_down_cancels_the_running_scan_and_waits_for_its_cleanup()
    {
        using var harness = new ScanHarness(configure: options => options.AllowedScanRoots = ["/missing"]);
        harness.Sbom.BlocksUntilCancelled("/missing/acme");
        var lifetime = new ScanLifetimeService(
            harness.Coordinator,
            harness.FileSystem,
            new OptionsWrapper<AirlockOptions>(harness.Options),
            harness.LoggerFactory.CreateLogger<ScanLifetimeService>());
        await lifetime.StartAsync(harness.Token);
        harness.FileSystem.WithDirectory("/missing/acme");
        var scan = await harness.StartDirectoryScanAsync("/missing/acme");
        await harness.Sbom.RunStarted.Task.WaitAsync(harness.Token);

        await lifetime.StopAsync(harness.Token);

        var ended = harness.Registry.Find(scan.Id)!;
        Assert.Equal(ScanStatus.Cancelled, ended.Status);
        Assert.Equal("Server shutting down", ended.CancellationReason);
        Assert.False(harness.WatchActiveScan.Current.IsActive);
        Assert.Contains(harness.LogRecords, record => record.Message == "⚠️ Allowed scan root does not exist: /missing");
    }

    [Fact]
    public async Task The_scan_is_cancelled_as_soon_as_the_host_begins_to_stop()
    {
        using var harness = new ScanHarness();
        harness.Sbom.BlocksUntilCancelled(ScanHarness.Directory);
        var lifetime = new ScanLifetimeService(
            harness.Coordinator,
            harness.FileSystem,
            new OptionsWrapper<AirlockOptions>(harness.Options),
            harness.LoggerFactory.CreateLogger<ScanLifetimeService>());
        var scan = await harness.StartDirectoryScanAsync(ScanHarness.Directory);
        await harness.Sbom.RunStarted.Task.WaitAsync(harness.Token);

        await lifetime.StoppingAsync(harness.Token);

        Assert.Equal("Server shutting down", harness.Registry.Find(scan.Id)!.CancellationReason);
        await lifetime.StopAsync(harness.Token);
        await lifetime.StoppedAsync(harness.Token);
    }

    [Fact]
    public async Task Shutting_down_also_ends_an_open_reservation()
    {
        using var harness = new ScanHarness();
        await using var reservation = await harness.ReserveAsync();

        await harness.Coordinator.StopAllAsync();

        Assert.False(harness.WatchActiveScan.Current.IsActive);
        Assert.Equal(RefusalCode.ReservationStopped, (await reservation.StartAsync(ScanHarness.Directory, harness.Token)).Refusal!.Code);
    }

    [Fact]
    public async Task An_unexpected_error_fails_the_scan_with_a_generic_reason()
    {
        using var harness = new ScanHarness();
        harness.Sbom.Throws(ScanHarness.Directory, new InvalidOperationException("boom"));

        var scan = await harness.RunDirectoryScanAsync();

        Assert.Equal(ScanStatus.Failed, scan.Status);
        Assert.Equal(ScanFailureCode.Unexpected, scan.Failure!.Code);
        Assert.Equal("Unexpected error — see the server log", scan.Failure.Reason);
        var crash = Assert.Single(harness.LogRecords, record => record.Id.Id == 1106);
        Assert.Equal("boom", crash.Exception!.Message);
    }

    public static TheoryData<string> Endings => new() { "Completed", "Incomplete", "Failed", "Cancelled" };

    [Theory]
    [MemberData(nameof(Endings))]
    public async Task However_a_scan_ends_its_staging_is_deleted_its_slot_freed_and_its_finished_event_last(string ending)
    {
        using var harness = new ScanHarness();
        var package = A.SbomPackage("minimist", "1.2.5", "npm", "minimist:minimist");
        switch (ending)
        {
            case "Completed":
                harness.Sbom.Produces(FakeSbomGenerator.Manifests, package);
                break;
            case "Incomplete":
                harness.Sbom.Produces(FakeSbomGenerator.Manifests, package);
                harness.Catalog.FailCpe(A.CpeValue("minimist:minimist", "1.2.5"), "NVD returned 503");
                break;
            case "Failed":
                harness.Sbom.Fails(FakeSbomGenerator.Manifests, new ScanFailure(ScanFailureCode.SyftOutputUnreadable, "Unreadable output"));
                break;
            default:
                harness.Sbom.BlocksUntilCancelled(FakeSbomGenerator.Manifests);
                break;
        }

        var scan = await harness.StartUploadScanAsync("package-lock.json");
        var collecting = harness.CollectEventsAsync(scan.Id);
        if (ending == "Cancelled")
        {
            await harness.Sbom.RunStarted.Task.WaitAsync(harness.Token);
            await harness.EmergencyStop.ExecuteAsync(harness.Token);
        }

        var events = await collecting;

        Assert.Equal(Enum.Parse<ScanStatus>(ending), Assert.IsType<ScanFinished>(events[^1]).Status);
        Assert.True(Assert.Single(harness.Staging.Created).Disposed);
        Assert.False(harness.WatchActiveScan.Current.IsActive);
    }
}
