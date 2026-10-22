using Airlock.Application.Correlation;
using Airlock.Domain.Results;
using Airlock.Domain.Scanning;
using Microsoft.Extensions.Logging;

namespace Airlock.Application.Tests.Correlation;

// AR-18 (Q-U2-2) and U2-NFR-05.
public sealed class NvdOutageTests
{
    [Fact]
    public async Task After_the_threshold_of_failures_in_a_row_no_further_lookup_starts()
    {
        using var harness = new ScanHarness(configure: options => options.Correlation.MaxParallelism = 1);
        harness.Catalog.FailEverything("NVD returned 503");
        harness.Sbom.Produces(
            ScanHarness.Directory,
            [.. Enumerable.Range(0, 10).Select(i => A.SbomPackage($"package-{i}", "1.0", "npm", $"vendor:package-{i}"))]);

        var scan = await harness.RunDirectoryScanAsync();

        Assert.Equal(5, harness.Catalog.CallCount);
        Assert.Equal(ScanStatus.Incomplete, scan.Status);
        Assert.Equal(10, scan.UnverifiedPackages.Count);
        Assert.Equal(5, scan.UnverifiedPackages.Count(package => package.Detail == "NVD returned 503"));
        Assert.Equal(
            5,
            scan.UnverifiedPackages.Count(package => package.Detail == "NVD unavailable — not checked after 5 failed lookups in a row"));
        var outage = Assert.Single(harness.LogRecords, record => record.Id.Id == 1311);
        Assert.Equal(LogLevel.Error, outage.Level);
        Assert.Equal("❌ NVD unavailable: 5 lookups failed in a row; remaining packages not checked", outage.Message);
    }

    [Fact]
    public async Task With_parallel_lookups_only_those_already_in_flight_finish()
    {
        using var harness = new ScanHarness(configure: options => options.Correlation.MaxParallelism = 4);
        harness.Catalog.FailEverything("NVD returned 503");
        harness.Catalog.Delay = TimeSpan.FromMilliseconds(5);
        harness.Sbom.Produces(
            ScanHarness.Directory,
            [.. Enumerable.Range(0, 40).Select(i => A.SbomPackage($"package-{i}", "1.0", "npm", $"vendor:package-{i}"))]);

        var scan = await harness.RunDirectoryScanAsync();

        Assert.InRange(harness.Catalog.CallCount, 5, 5 + 4 - 1);
        Assert.Equal(40, scan.UnverifiedPackages.Count);
        Assert.All(scan.UnverifiedPackages, package => Assert.Equal(UnverifiedReason.LookupFailed, package.Reason));
        Assert.Equal(ScanStatus.Incomplete, scan.Status);
    }

    [Fact]
    public void Any_answer_resets_the_count_and_an_open_breaker_stays_open()
    {
        var breaker = new NvdOutageBreaker(3);

        Assert.False(breaker.RecordFailure());
        Assert.False(breaker.RecordFailure());
        breaker.RecordAnswer();
        Assert.False(breaker.RecordFailure());
        Assert.False(breaker.RecordFailure());
        Assert.False(breaker.IsOpen);

        Assert.True(breaker.RecordFailure());
        Assert.True(breaker.IsOpen);
        Assert.False(breaker.RecordFailure());
        breaker.RecordAnswer();
        Assert.True(breaker.IsOpen);
        Assert.Equal(3, breaker.Threshold);
    }
}
