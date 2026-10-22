using Airlock.Application.Events;
using Airlock.Application.Tests.Fakes;
using Airlock.Domain.Intelligence;
using Airlock.Domain.Scanning;

namespace Airlock.Application.Tests.Quality;

// U2-NFR-06 and U2-NFR-11: a failing port never yields Completed or Incomplete, and the slot is always freed.
public sealed class FaultInjectionTests
{
    [Fact]
    public async Task A_registry_failure_at_any_update_ends_the_scan_Failed_and_frees_the_slot()
    {
        int updates;
        using (var reference = Build(out var referenceRelease))
        {
            var scan = await reference.StartDirectoryScanAsync();
            var collecting = reference.CollectEventsAsync(scan.Id);
            referenceRelease.SetResult();
            Assert.Equal(ScanStatus.Completed, Assert.IsType<ScanFinished>((await collecting)[^1]).Status);
            updates = reference.Registry.UpdateCount;
        }

        Assert.True(updates >= 8);
        List<string> endings = [];
        for (var failingUpdate = 1; failingUpdate <= updates; failingUpdate++)
        {
            using var harness = Build(out var release);
            var count = 0;
            var failAt = failingUpdate;
            Scan? failedVersion = null;
            harness.Registry.FailUpdateWhen = next =>
            {
                if (++count != failAt)
                {
                    return false;
                }

                failedVersion = next;
                return true;
            };
            var scan = await harness.StartDirectoryScanAsync();

            var collecting = harness.CollectEventsAsync(scan.Id);
            release.SetResult();
            var events = await collecting;

            endings.Add($"{failingUpdate}: {Assert.IsType<ScanFinished>(events[^1]).Status} after failing {failedVersion?.Phase} {failedVersion?.Status}");
            Assert.False(harness.WatchActiveScan.Current.IsActive);
        }

        Assert.All(endings, ending => Assert.Contains(": Failed after", ending, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_registry_that_fails_every_update_still_ends_the_scan_Failed_and_frees_the_slot()
    {
        using var harness = Build(out var release);
        harness.Registry.FailUpdateWhen = _ => true;
        var scan = await harness.StartDirectoryScanAsync();

        var collecting = harness.CollectEventsAsync(scan.Id);
        release.SetResult();
        var events = await collecting;

        Assert.Equal(ScanStatus.Failed, Assert.IsType<ScanFinished>(events[^1]).Status);
        Assert.False(harness.WatchActiveScan.Current.IsActive);
        Assert.Contains(harness.LogRecords, record => record.Message == "⚠️ Cleanup step failed: record the failure: Registry unavailable");
    }

    [Fact]
    public async Task A_registry_that_cannot_add_the_scan_leaves_nothing_running()
    {
        using var harness = new ScanHarness();
        harness.Registry.FailAdd = true;
        var reservation = await harness.ReserveAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await reservation.StartAsync(ScanHarness.Directory, harness.Token));
        await reservation.DisposeAsync();

        Assert.False(harness.WatchActiveScan.Current.IsActive);
        Assert.Empty(harness.Sbom.Calls);
    }

    [Fact]
    public async Task A_staging_area_that_cannot_be_deleted_still_frees_the_slot()
    {
        using var harness = new ScanHarness();
        harness.Staging.DisposeFailure = new IOException("disk busy");

        var scan = await harness.RunUploadScanAsync("requirements.txt");

        Assert.Equal(ScanStatus.Completed, scan.Status);
        Assert.False(harness.WatchActiveScan.Current.IsActive);
        Assert.Contains(harness.LogRecords, record => record.Message == "⚠️ Cleanup step failed: delete the staging area: disk busy");
    }

    [Fact]
    public async Task An_abandoned_reservation_whose_staging_cannot_be_deleted_still_frees_the_slot()
    {
        using var harness = new ScanHarness();
        harness.Staging.DisposeFailure = new IOException("disk busy");
        var reservation = await harness.ReserveAsync();
        using (var content = new MemoryStream([1]))
        {
            await reservation.AddArtifactAsync("requirements.txt", content, harness.Token);
        }

        await reservation.DisposeAsync();

        Assert.False(harness.WatchActiveScan.Current.IsActive);
    }

    [Fact]
    public async Task A_catalog_that_throws_fails_the_scan()
    {
        using var harness = new ScanHarness();
        harness.Sbom.Produces(ScanHarness.Directory, A.SbomPackage("minimist", "1.2.5", "npm", "minimist:minimist"));
        harness.Catalog.Fallback = (_, _) => throw new InvalidOperationException("socket closed");

        var scan = await harness.RunDirectoryScanAsync();

        Assert.Equal(ScanStatus.Failed, scan.Status);
        Assert.Equal(ScanFailureCode.Unexpected, scan.Failure!.Code);
    }

    private static ScanHarness Build(out TaskCompletionSource release)
    {
        var harness = new ScanHarness();
        release = new TaskCompletionSource();
        harness.Sbom.ProducesAfter(
            ScanHarness.Directory,
            release.Task,
            A.SbomPackage("pyyaml", "5.3", "python", "pyyaml:pyyaml"),
            A.SbomPackage("busybox", "1.36.1-r31", "apk", "busybox:busybox"),
            A.SbomPackage(A.Package("minimist", "1.2.5", "npm")));
        harness.Catalog
            .Cpe(A.CpeValue("pyyaml:pyyaml", "5.3"), A.Cve("CVE-2020-1747", 9.8m, Severity.Critical))
            .Cpe(A.CpeValue("busybox:busybox", "1.36.1-r31"), A.UnscoredCve("CVE-2025-60876"));
        return harness;
    }
}
