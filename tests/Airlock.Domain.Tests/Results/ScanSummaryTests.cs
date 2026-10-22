using Airlock.Domain.Intelligence;
using Airlock.Domain.Results;
using Airlock.Domain.Scanning;

namespace Airlock.Domain.Tests.Results;

// BR-13
public sealed class ScanSummaryTests
{
    [Fact]
    public void The_87_package_example_leaves_73_clean_and_finishes_Incomplete()
    {
        var vulnerable = new[]
        {
            A.Vulnerable(A.Package("pyyaml", "5.3"),
                A.Finding("CVE-2020-1747", 9.8m, Severity.Critical),
                A.Finding("CVE-2020-14343", 9.8m, Severity.Critical)),
            A.Vulnerable(A.Package("jinja2", "2.10"),
                A.Finding("CVE-2019-8341", 9.8m, Severity.Critical),
                A.Finding("CVE-2019-10906", 8.6m, Severity.High)),
        };
        var unverified = Enumerable.Range(1, 12).Select(i => A.Unverified(A.Package($"package-{i}", "1.0"))).ToArray();

        var summary = ScanSummary.From(87, vulnerable, unverified);
        var scan = A.ScanInCorrelation().Finish(87, vulnerable, unverified, A.ScanStart.AddMinutes(3));

        Assert.Equal(87, summary.TotalPackages);
        Assert.Equal(2, summary.VulnerablePackages);
        Assert.Equal(12, summary.UnverifiedPackages);
        Assert.Equal(73, summary.CleanPackages);
        Assert.Equal(3, summary.CriticalFindings);
        Assert.Equal(1, summary.HighFindings);
        Assert.Equal(ScanStatus.Incomplete, scan.Status);
        Assert.Equal(73, scan.Summary!.CleanPackages);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(5, 0, 0)]
    [InlineData(5, 2, 3)]
    [InlineData(10, 4, 1)]
    public void Counts_add_up(int total, int vulnerableCount, int unverifiedCount)
    {
        var vulnerable = Enumerable.Range(1, vulnerableCount)
            .Select(i => A.Vulnerable(A.Package($"v-{i}", "1.0"), A.Finding("CVE-2020-1747", 9.8m, Severity.Critical)));
        var unverified = Enumerable.Range(1, unverifiedCount).Select(i => A.Unverified(A.Package($"u-{i}", "1.0")));

        var summary = ScanSummary.From(total, vulnerable, unverified);

        Assert.Equal(summary.TotalPackages, summary.CleanPackages + summary.VulnerablePackages + summary.UnverifiedPackages);
    }

    [Fact]
    public void A_package_is_listed_once_in_one_category()
    {
        var package = A.Package();
        var vulnerable = A.Vulnerable(package, A.Finding("CVE-2020-1747", 9.8m, Severity.Critical));

        Assert.Throws<ArgumentException>(() => ScanSummary.From(5, [vulnerable], [A.Unverified(package)]));
        Assert.Throws<ArgumentException>(() => ScanSummary.From(5, [vulnerable, vulnerable], []));
        Assert.Throws<ArgumentException>(() => ScanSummary.From(5, [], [A.Unverified(package), A.Unverified(package)]));
    }

    [Fact]
    public void Inconsistent_totals_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => ScanSummary.From(1, [], [A.Unverified(A.Package("a", "1")), A.Unverified(A.Package("b", "1"))]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScanSummary.From(-1, [], []));
        Assert.Throws<ArgumentNullException>(() => ScanSummary.From(1, null!, []));
        Assert.Throws<ArgumentNullException>(() => ScanSummary.From(1, [], null!));
        Assert.Throws<ArgumentNullException>(() => ScanSummary.From(1, [null!], []));
        Assert.Throws<ArgumentNullException>(() => ScanSummary.From(1, [], [null!]));
    }
}
