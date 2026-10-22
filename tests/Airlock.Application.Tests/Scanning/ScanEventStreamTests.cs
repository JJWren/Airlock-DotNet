using Airlock.Application.Events;
using Airlock.Application.Scanning;
using Airlock.Domain.Scanning;

namespace Airlock.Application.Tests.Scanning;

// AR-21 to AR-24 and AR-26.
public sealed class ScanEventStreamTests
{
    [Fact]
    public async Task A_subscriber_gets_a_snapshot_then_phases_and_progress_in_order_and_the_finished_event_last()
    {
        using var harness = new ScanHarness();
        var release = new TaskCompletionSource();
        harness.Sbom.ProducesAfter(
            ScanHarness.Directory,
            release.Task,
            [.. Enumerable.Range(0, 10).Select(i => A.SbomPackage(A.Package($"package-{i}", "1.0", "npm")))]);
        var scan = await harness.StartDirectoryScanAsync();

        var collecting = harness.CollectEventsAsync(scan.Id);
        release.SetResult();
        var events = await collecting;

        var snapshot = Assert.IsType<ScanSnapshot>(events[0]);
        Assert.Equal(ScanPhase.SbomGeneration, snapshot.Scan.Phase);
        Assert.Equal(ScanStatus.Completed, Assert.IsType<ScanFinished>(events[^1]).Status);
        Assert.Equal([ScanPhase.Correlation, ScanPhase.Summary], events.OfType<PhaseChanged>().Select(change => change.Phase));
        Assert.Equal(Enumerable.Range(0, 11), events.OfType<PackagesCorrelated>().Select(progress => progress.Progress.Correlated));
        Assert.True(IndexOf<PhaseChanged>(events, 0) < IndexOf<PackagesCorrelated>(events, 0));
        Assert.True(IndexOf<PackagesCorrelated>(events, 10) < IndexOf<PhaseChanged>(events, 1));
    }

    [Fact]
    public async Task Each_event_is_published_after_the_registry_has_it()
    {
        using var harness = new ScanHarness();
        var release = new TaskCompletionSource();
        harness.Sbom.ProducesAfter(
            ScanHarness.Directory,
            release.Task,
            [.. Enumerable.Range(0, 20).Select(i => A.SbomPackage(A.Package($"package-{i}", "1.0", "npm")))]);
        var scan = await harness.StartDirectoryScanAsync();

        var checks = 0;
        await using var events = harness.WatchScan.SubscribeAsync(scan.Id, harness.Token).GetAsyncEnumerator(harness.Token);
        Assert.True(await events.MoveNextAsync());
        release.SetResult();
        while (await events.MoveNextAsync())
        {
            var recorded = harness.Registry.Find(scan.Id)!;
            if (events.Current is PhaseChanged change)
            {
                Assert.True(recorded.IsFinished || recorded.Phase >= change.Phase);
                checks++;
            }
            else if (events.Current is PackagesCorrelated progress)
            {
                Assert.True(recorded.IsFinished || recorded.Progress!.Correlated >= progress.Progress.Correlated);
                checks++;
            }
        }

        Assert.Equal(2 + 21, checks);
        await harness.WaitForIdleAsync();
    }

    [Fact]
    public async Task A_finished_scan_streams_its_snapshot_and_finished_event_and_an_unknown_scan_nothing()
    {
        using var harness = new ScanHarness();
        var scan = await harness.RunDirectoryScanAsync();

        var finished = await harness.CollectEventsAsync(scan.Id);
        var unknown = await harness.CollectEventsAsync(ScanId.New());

        Assert.Equal(2, finished.Count);
        Assert.Equal(scan, Assert.IsType<ScanSnapshot>(finished[0]).Scan);
        Assert.Equal(ScanStatus.Completed, Assert.IsType<ScanFinished>(finished[1]).Status);
        Assert.Empty(unknown);
    }

    [Fact]
    public async Task A_completed_hub_answers_late_subscribers_with_its_final_version()
    {
        using var harness = new ScanHarness();
        var scan = await harness.RunDirectoryScanAsync();
        var final = harness.Registry.Find(scan.Id)!;
        var hub = new ScanEventHub(final);
        hub.Complete(final);
        hub.Publish(final, new PhaseChanged(final.Id, ScanPhase.Summary));

        List<ScanEvent> events = [];
        await foreach (var scanEvent in hub.SubscribeAsync(harness.Token))
        {
            events.Add(scanEvent);
        }

        Assert.Equal(2, events.Count);
        Assert.Same(final, hub.Latest);
    }

    [Fact]
    public async Task Log_lines_written_for_a_scan_arrive_as_its_events()
    {
        using var harness = new ScanHarness();
        var release = new TaskCompletionSource();
        harness.Sbom.ProducesAfter(ScanHarness.Directory, release.Task, A.SbomPackage(A.Package("minimist", "1.2.5", "npm")));
        var scan = await harness.StartDirectoryScanAsync();

        var collecting = harness.CollectEventsAsync(scan.Id);
        release.SetResult();
        var logged = (await collecting).OfType<ScanLogged>().Select(line => line.Entry).ToList();

        Assert.All(logged, entry => Assert.Equal(scan.Id, entry.ScanId));
        Assert.Contains(logged, entry => entry.Message == "📦 1 package(s) identified in /uploads/acme-app");
        Assert.Contains(logged, entry => entry.Message == "✅ Vetting sequence complete: 0 vulnerable, 0 unverified, 1 clean of 1 package(s)");
    }

    [Fact]
    public async Task Log_entries_without_a_running_scan_are_not_routed()
    {
        using var harness = new ScanHarness();

        harness.Router.Route(new LiveLogEntry(ScanHarness.StartTime, Microsoft.Extensions.Logging.LogLevel.Information, "x", "y", ScanId.New()));
        harness.Router.Route(new LiveLogEntry(ScanHarness.StartTime, Microsoft.Extensions.Logging.LogLevel.Information, "x", "y", null));

        Assert.Throws<ArgumentNullException>(() => harness.Router.Route(null!));
        await harness.WaitForIdleAsync();
    }

    private static int IndexOf<T>(IReadOnlyList<ScanEvent> events, int occurrence)
        where T : ScanEvent =>
        events.Select((scanEvent, index) => (scanEvent, index)).Where(pair => pair.scanEvent is T).ElementAt(occurrence).index;
}
