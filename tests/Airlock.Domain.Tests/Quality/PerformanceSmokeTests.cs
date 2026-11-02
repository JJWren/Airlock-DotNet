using System.Diagnostics;
using System.Globalization;
using Airlock.Domain.Intelligence;
using Airlock.Domain.Results;
using Airlock.Domain.Scanning;

namespace Airlock.Domain.Tests.Quality;

// U1-NFR-05 at ten times its target: ScanSummary.From and Finish handle 10,000 packages in well under 50 ms. Build and
// Test reads the measurement from the test results.
public sealed class PerformanceSmokeTests
{
    [Fact]
    public void Ten_thousand_packages_are_summarized_and_finished_well_inside_half_a_second()
    {
        List<VulnerablePackage> vulnerable =
        [
            .. Enumerable.Range(0, 3_000)
                .Select(i => A.Vulnerable(A.Package($"package-{i}", "1.0"), A.Finding($"CVE-2026-{10_000 + i}", 9.8m, Severity.Critical))),
        ];
        List<UnverifiedPackage> unverified = [.. Enumerable.Range(3_000, 2_000).Select(i => A.Unverified(A.Package($"package-{i}", "1.0")))];
        A.ScanInCorrelation().Finish(10_000, vulnerable, unverified, A.ScanStart.AddMinutes(1));

        var watch = Stopwatch.StartNew();
        var summary = ScanSummary.From(10_000, vulnerable, unverified);
        var summarized = watch.Elapsed;
        var scan = A.ScanInCorrelation();
        watch.Restart();
        var finished = scan.Finish(10_000, vulnerable, unverified, A.ScanStart.AddMinutes(1));
        var finishing = watch.Elapsed;

        Assert.Equal(5_000, summary.CleanPackages);
        Assert.Equal(ScanStatus.Incomplete, finished.Status);
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"measured U1-NFR-05: 10,000 packages summarized in {summarized.TotalMilliseconds:0.0} ms and finished in {finishing.TotalMilliseconds:0.0} ms (target 50 ms)"));
        Assert.True(summarized < TimeSpan.FromMilliseconds(500), $"Summarizing took {summarized.TotalMilliseconds} ms.");
        Assert.True(finishing < TimeSpan.FromMilliseconds(500), $"Finishing took {finishing.TotalMilliseconds} ms.");
    }
}
