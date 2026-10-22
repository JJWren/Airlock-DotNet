using Airlock.Application.Events;
using Airlock.Application.Outcomes;
using Airlock.Domain.Scanning;

namespace Airlock.Application.Tests.Quality;

// U2-NFR-02: one holder of the slot, one final status.
public sealed class ConcurrencyTests
{
    [Fact]
    public async Task Exactly_one_of_100_concurrent_reservations_wins_every_time()
    {
        using var harness = new ScanHarness();
        for (var round = 0; round < 50; round++)
        {
            var outcomes = await Task.WhenAll(
                Enumerable.Range(0, 100).Select(_ => Task.Run(() => harness.StartScan.ReserveAsync(harness.Token).AsTask())));

            var winner = Assert.Single(outcomes, outcome => outcome.IsSuccess);
            Assert.All(
                outcomes.Where(outcome => !outcome.IsSuccess),
                outcome => Assert.Equal(RefusalCode.ScanInProgress, outcome.Refusal!.Code));
            await winner.Value!.DisposeAsync();
            Assert.False(harness.WatchActiveScan.Current.IsActive);
        }
    }

    [Fact]
    public async Task Emergency_Stop_racing_the_end_of_a_scan_gives_exactly_one_final_status()
    {
        for (var round = 0; round < 30; round++)
        {
            using var harness = new ScanHarness();
            harness.Sbom.Produces(ScanHarness.Directory, A.SbomPackage(A.Package("minimist", "1.2.5", "npm")));
            var scan = await harness.StartDirectoryScanAsync();

            var stopping = Task.Run(() => harness.EmergencyStop.ExecuteAsync(harness.Token).AsTask());
            var events = await harness.CollectEventsAsync(scan.Id);
            await stopping;

            var finished = Assert.Single(events.OfType<ScanFinished>());
            Assert.Contains(finished.Status, new[] { ScanStatus.Completed, ScanStatus.Cancelled });
            Assert.Equal(finished.Status, harness.Registry.Find(scan.Id)!.Status);
        }
    }
}
