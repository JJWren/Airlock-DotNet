using Airlock.Application.Events;
using Airlock.Application.Outcomes;
using Airlock.Application.Ports;

namespace Airlock.Application.Tests.UseCases;

// AR-01 to AR-03 and AR-26; defects 7 and 15.
public sealed class StartScanTests
{
    [Fact]
    public async Task A_ready_server_reserves_the_slot_for_a_new_scan()
    {
        using var harness = new ScanHarness();

        await using var reservation = await harness.ReserveAsync();

        Assert.Equal(reservation.Id, harness.WatchActiveScan.Current.ScanId);
        Assert.False(harness.WatchActiveScan.Current.ReceivingUpload);
    }

    [Theory]
    [InlineData(false, true, "NVD API key not configured")]
    [InlineData(true, false, "Syft not found at /opt/airlock/bin/syft")]
    [InlineData(false, false, "NVD API key not configured; Syft not found at /opt/airlock/bin/syft")]
    public async Task A_server_that_is_not_ready_refuses_with_every_reason(bool keyConfigured, bool syftAvailable, string reason)
    {
        using var harness = new ScanHarness();
        harness.Catalog.IsConfigured = keyConfigured;
        if (!syftAvailable)
        {
            harness.Sbom.Status = new SbomToolStatus(false, null, "Syft not found at /opt/airlock/bin/syft");
        }

        var reserved = await harness.StartScan.ReserveAsync(harness.Token);

        Assert.False(reserved.IsSuccess);
        Assert.Equal(RefusalCode.NotReady, reserved.Refusal!.Code);
        Assert.Equal(reason, reserved.Refusal.Message);
        Assert.False(harness.WatchActiveScan.Current.IsActive);
        Assert.Contains(harness.LogRecords, record => record.Message == "❌ Vetting sequence refused: " + reason);
    }

    [Fact]
    public async Task A_second_reservation_is_refused_and_the_first_is_unaffected()
    {
        using var harness = new ScanHarness();
        await using var first = await harness.ReserveAsync();

        var second = await harness.StartScan.ReserveAsync(harness.Token);

        Assert.Equal(RefusalCode.ScanInProgress, second.Refusal!.Code);
        Assert.Equal("A scan is already in progress", second.Refusal.Message);
        Assert.Equal(first.Id, harness.WatchActiveScan.Current.ScanId);
    }

    [Fact]
    public async Task A_running_scan_holds_the_slot_too()
    {
        using var harness = new ScanHarness();
        harness.Sbom.BlocksUntilCancelled(ScanHarness.Directory);
        var scan = await harness.StartDirectoryScanAsync();

        var second = await harness.StartScan.ReserveAsync(harness.Token);

        Assert.Equal(RefusalCode.ScanInProgress, second.Refusal!.Code);
        await harness.EmergencyStop.ExecuteAsync(harness.Token);
        await harness.WaitForEndAsync(scan.Id);
    }

    [Fact]
    public async Task An_abandoned_reservation_frees_the_slot_deletes_its_staging_and_records_nothing()
    {
        using var harness = new ScanHarness();
        var reservation = await harness.ReserveAsync();
        using (var content = new MemoryStream([1, 2, 3]))
        {
            Assert.True((await reservation.AddArtifactAsync("requirements.txt", content, harness.Token)).IsSuccess);
        }

        await reservation.DisposeAsync();
        await reservation.DisposeAsync();

        Assert.False(harness.WatchActiveScan.Current.IsActive);
        Assert.True(Assert.Single(harness.Staging.Created).Disposed);
        Assert.Empty(harness.Registry.List());
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await reservation.StartAsync(null, harness.Token));
    }

    [Fact]
    public async Task A_refused_reservation_keeps_refusing()
    {
        using var harness = new ScanHarness(configure: options => options.Limits.MaxArtifactsPerScan = 1);
        await using var reservation = await harness.ReserveAsync();

        Assert.True((await AddAsync(reservation, "requirements.txt", harness.Token)).IsSuccess);
        var refused = await AddAsync(reservation, "package-lock.json", harness.Token);
        var again = await AddAsync(reservation, "poetry.lock", harness.Token);
        var start = await reservation.StartAsync(null, harness.Token);

        Assert.Equal(RefusalCode.TooManyArtifacts, refused.Refusal!.Code);
        Assert.Equal("At most 1 files per scan", refused.Refusal.Message);
        Assert.Equal(refused.Refusal, again.Refusal);
        Assert.Equal(refused.Refusal, start.Refusal);
    }

    [Fact]
    public async Task A_started_reservation_takes_no_more_files()
    {
        using var harness = new ScanHarness();
        harness.Sbom.BlocksUntilCancelled(ScanHarness.Directory);
        await using var reservation = await harness.ReserveAsync();
        var started = await reservation.StartAsync(ScanHarness.Directory, harness.Token);
        Assert.True(started.IsSuccess);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await AddAsync(reservation, "requirements.txt", harness.Token));

        await harness.EmergencyStop.ExecuteAsync(harness.Token);
        await harness.WaitForEndAsync(started.Value!.Id);
    }

    [Fact]
    public async Task Every_change_of_the_slot_reaches_its_watchers()
    {
        using var harness = new ScanHarness();
        harness.Sbom.BlocksUntilCancelled(FakesManifests);
        await using var states = harness.WatchActiveScan.SubscribeAsync(harness.Token).GetAsyncEnumerator(harness.Token);

        Assert.Equal(ActiveScanState.Idle, await NextAsync(states));
        await using var reservation = await harness.ReserveAsync();
        Assert.Equal(new ActiveScanState(reservation.Id, false), await NextAsync(states));
        Assert.True((await AddAsync(reservation, "requirements.txt", harness.Token)).IsSuccess);
        Assert.Equal(new ActiveScanState(reservation.Id, true), await NextAsync(states));
        Assert.True((await reservation.StartAsync(null, harness.Token)).IsSuccess);
        Assert.Equal(new ActiveScanState(reservation.Id, false), await NextAsync(states));
        await harness.EmergencyStop.ExecuteAsync(harness.Token);
        Assert.Equal(ActiveScanState.Idle, await NextAsync(states));
    }

    private const string FakesManifests = Fakes.FakeSbomGenerator.Manifests;

    private static async Task<Outcome<Airlock.Domain.Targets.Artifact>> AddAsync(
        Airlock.Application.UseCases.ScanReservation reservation, string fileName, CancellationToken cancellationToken)
    {
        using var content = new MemoryStream([1, 2, 3]);
        return await reservation.AddArtifactAsync(fileName, content, cancellationToken);
    }

    private static async Task<ActiveScanState> NextAsync(IAsyncEnumerator<ActiveScanState> states)
    {
        Assert.True(await states.MoveNextAsync());
        return states.Current;
    }
}
