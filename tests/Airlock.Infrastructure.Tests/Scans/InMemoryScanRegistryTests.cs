using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;
using Airlock.Infrastructure.Scans;
using Airlock.Infrastructure.Tests.Support;

namespace Airlock.Infrastructure.Tests.Scans;

// IR-14 — the latest version of each scan, updated atomically.
public sealed class InMemoryScanRegistryTests
{
    private readonly InMemoryScanRegistry registry = new();

    [Fact]
    public void A_scan_is_added_found_listed_and_removed()
    {
        var scan = Running();

        registry.Add(scan);

        Assert.Same(scan, registry.Find(scan.Id));
        Assert.Equal([scan], registry.List());
        registry.Remove(scan.Id);
        Assert.Null(registry.Find(scan.Id));
        Assert.Empty(registry.List());
    }

    [Fact]
    public void An_update_replaces_the_latest_version_and_returns_it()
    {
        var scan = Running();
        registry.Add(scan);

        var updated = registry.Update(scan.Id, current => current.EnterPhase(ScanPhase.Correlation));

        Assert.Equal(ScanPhase.Correlation, updated.Phase);
        Assert.Same(updated, registry.Find(scan.Id));
    }

    [Fact]
    public async Task Concurrent_updates_of_one_scan_apply_one_after_another()
    {
        var scan = Running();
        registry.Add(scan);

        await Task.WhenAll(Enumerable.Range(0, 100).Select(index => Task.Run(
            () => registry.Update(scan.Id, current => current.AddWarning(new ScanWarning(ScanWarningCode.NoPackagesIdentified, $"warning {index}"))),
            Test.Token)));

        Assert.Equal(100, registry.Find(scan.Id)!.Warnings.Count);
    }

    [Fact]
    public void A_transition_that_throws_leaves_the_scan_as_it_was()
    {
        var scan = Running();
        registry.Add(scan);

        Assert.Throws<InvalidOperationException>(() => registry.Update(scan.Id, current => current.EnterPhase(ScanPhase.SbomGeneration)));

        Assert.Same(scan, registry.Find(scan.Id));
    }

    [Fact]
    public void Adding_twice_updating_a_missing_scan_and_changing_its_id_are_refused()
    {
        var scan = Running();
        registry.Add(scan);

        Assert.Throws<InvalidOperationException>(() => registry.Add(scan));
        Assert.Throws<InvalidOperationException>(() => registry.Update(ScanId.New(), current => current));
        Assert.Throws<InvalidOperationException>(() => registry.Update(scan.Id, _ => Running()));
        Assert.Throws<InvalidOperationException>(() => registry.Update(scan.Id, _ => null!));
        Assert.Throws<ArgumentNullException>(() => registry.Add(null!));
        Assert.Throws<ArgumentNullException>(() => registry.Update(scan.Id, null!));
    }

    [Fact]
    public void Removing_a_scan_that_is_not_there_is_fine()
    {
        registry.Remove(ScanId.New());

        Assert.Empty(registry.List());
    }

    private static Scan Running() => Scan.Start(ScanId.New(), new DirectoryTarget("/uploads/acme-app"), Test.StartTime);
}
