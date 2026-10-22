using Airlock.Application.Events;
using Airlock.Application.Scanning;
using Airlock.Domain.Scanning;
using Microsoft.Extensions.Logging;

namespace Airlock.Application.Tests.Quality;

// U2-NFR-07: bounded buffers; the pipeline never waits for a subscriber.
public sealed class SlowSubscriberTests
{
    [Fact]
    public async Task A_subscriber_that_stops_reading_neither_slows_the_scan_nor_loses_the_phases_or_the_end()
    {
        using var harness = new ScanHarness();
        var release = new TaskCompletionSource();
        harness.Sbom.ProducesAfter(
            ScanHarness.Directory,
            release.Task,
            [.. Enumerable.Range(0, 1_500).Select(i => A.SbomPackage(A.Package($"package-{i}", "1.0", "npm")))]);
        var scan = await harness.StartDirectoryScanAsync();
        await using var events = harness.WatchScan.SubscribeAsync(scan.Id, harness.Token).GetAsyncEnumerator(harness.Token);
        Assert.True(await events.MoveNextAsync());

        release.SetResult();
        await harness.WaitForIdleAsync();

        List<ScanEvent> rest = [];
        while (await events.MoveNextAsync())
        {
            rest.Add(events.Current);
        }

        Assert.InRange(rest.Count, 3, ScanEventHub.SubscriberCapacity);
        Assert.Equal(2, rest.OfType<PhaseChanged>().Count());
        Assert.Equal(ScanStatus.Completed, Assert.IsType<ScanFinished>(rest[^1]).Status);
        Assert.True(rest.OfType<PackagesCorrelated>().Count() < 1_501);
    }

    [Fact]
    public async Task A_full_queue_drops_the_oldest_log_or_progress_event_first()
    {
        var id = ScanId.New();
        var queue = new EventQueue(3);
        var phase = new PhaseChanged(id, ScanPhase.Correlation);
        var lines = Enumerable.Range(1, 3)
            .Select(i => new ScanLogged(id, new LiveLogEntry(ScanHarness.StartTime, LogLevel.Information, "Airlock", $"line {i}", id)))
            .ToList();
        var finished = new ScanFinished(id, ScanStatus.Completed);

        queue.Write(phase);
        lines.ForEach(queue.Write);
        queue.Write(finished);
        queue.Complete();
        queue.Write(new PhaseChanged(id, ScanPhase.Summary));

        List<ScanEvent> read = [];
        await foreach (var scanEvent in queue.ReadAllAsync(TestContext.Current.CancellationToken))
        {
            read.Add(scanEvent);
        }

        Assert.Equal([phase, lines[2], finished], read);
    }
}
