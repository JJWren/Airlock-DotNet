using Airlock.Application.Ports;
using Airlock.Application.Tests.Fakes;
using Airlock.Domain.Intelligence;
using Airlock.Domain.Scanning;

namespace Airlock.Application.Tests.Scanning;

// AR-19 and AR-20; defect 8 (NVD failures reported clean).
public sealed class ScanResultTests
{
    [Fact]
    public async Task The_87_package_example_finishes_Incomplete_with_73_clean()
    {
        using var harness = new ScanHarness(configure: options => options.Correlation.MaxParallelism = 1);
        List<SbomPackage> packages =
        [
            A.SbomPackage("pyyaml", "5.3", "python", "pyyaml:pyyaml"),
            A.SbomPackage("jinja2", "2.10", "python", "pocoo:jinja2"),
        ];
        harness.Catalog
            .Cpe(A.CpeValue("pyyaml:pyyaml", "5.3"), A.Cve("CVE-2020-1747", 9.8m, Severity.Critical), A.Cve("CVE-2020-14343", 9.8m, Severity.Critical))
            .Cpe(A.CpeValue("pocoo:jinja2", "2.10"), A.Cve("CVE-2019-8341", 9.8m, Severity.Critical), A.Cve("CVE-2019-10906", 8.6m, Severity.High));

        // Twelve packages NVD keeps failing for, spread out so that NVD is never declared unavailable.
        var failing = 0;
        for (var i = 2; i < 87; i++)
        {
            if ((i - 2) % 7 == 0 && failing++ < 12)
            {
                packages.Add(A.SbomPackage($"failing-{i}", "1.0", "npm", $"vendor:failing-{i}"));
                harness.Catalog.FailCpe(A.CpeValue($"vendor:failing-{i}", "1.0"), "NVD returned 503");
            }
            else
            {
                packages.Add(A.SbomPackage(A.Package($"clean-{i}", "1.0", "npm")));
            }
        }

        harness.Sbom.Produces(ScanHarness.Directory, [.. packages]);

        var scan = await harness.RunDirectoryScanAsync();

        Assert.Equal(ScanStatus.Incomplete, scan.Status);
        var summary = scan.Summary!;
        Assert.Equal(87, summary.TotalPackages);
        Assert.Equal(2, summary.VulnerablePackages);
        Assert.Equal(12, summary.UnverifiedPackages);
        Assert.Equal(73, summary.CleanPackages);
        Assert.Equal(3, summary.CriticalFindings);
        Assert.Equal(1, summary.HighFindings);
    }

    [Fact]
    public async Task Every_lookup_answering_completes_the_scan()
    {
        using var harness = new ScanHarness();
        harness.Sbom.Produces(ScanHarness.Directory, A.SbomPackage("flask", "2.0.0", "python", "flask:flask"));

        var scan = await harness.RunDirectoryScanAsync();

        Assert.Equal(ScanStatus.Completed, scan.Status);
        Assert.Equal(1, scan.Summary!.CleanPackages);
    }

    [Fact]
    public async Task Warnings_come_in_run_order_then_generic_names_then_unscored_CVEs_by_package()
    {
        using var harness = new ScanHarness();
        harness.Sbom
            .Produces(
                FakeSbomGenerator.Manifests,
                A.SbomPackage("zlib", "1.2", "apk", "zlib:zlib"),
                A.SbomPackage("busybox", "1.36.1-r31", "apk", "busybox:busybox"),
                A.SbomPackage(A.Package("core", "2.0", "npm")),
                A.SbomPackage(A.Package("auth", "v1", "go-module")))
            .ProducesImage("alpine.tar", A.SbomPackage(A.Package("musl", "1.2.5", "apk")));
        harness.Catalog
            .Cpe(A.CpeValue("zlib:zlib", "1.2"), A.UnscoredCve("CVE-2099-0002"), A.Cve("CVE-2099-0003", 7.5m, Severity.High))
            .Cpe(A.CpeValue("busybox:busybox", "1.36.1-r31"), A.UnscoredCve("CVE-2025-60876"));

        var scan = await harness.RunUploadScanAsync("requirements.txt", "notes.docx", "alpine.tar");

        Assert.Equal(
            [
                (ScanWarningCode.NoPackagesIdentified, "No packages identified in notes.docx"),
                (ScanWarningCode.DistributionBackportsNotConsidered,
                    "Distribution backports are not considered for alpine.tar: versions are checked as the image reports them"),
                (ScanWarningCode.NotProductMatched, "Not product-matched, name too generic: auth v1, core 2.0"),
                (ScanWarningCode.CvesWithoutSeverity, "busybox 1.36.1-r31: CVE-2025-60876 not yet scored by NVD"),
                (ScanWarningCode.CvesWithoutSeverity, "zlib 1.2: CVE-2099-0002 not yet scored by NVD"),
            ],
            scan.Warnings.Select(warning => (warning.Code, warning.Message)));
        Assert.Equal("busybox 1.36.1-r31", scan.Warnings[3].Input);
    }

    [Fact]
    public async Task The_generic_name_warning_lists_twenty_packages_and_counts_the_rest()
    {
        using var harness = new ScanHarness();
        var names = Enumerable.Range(0, 23).Select(i => $"{(char)('x' + (i / 10))}{i % 10}").ToList();
        harness.Sbom.Produces(ScanHarness.Directory, [.. names.Select(name => A.SbomPackage(A.Package(name, "1.0", "npm")))]);

        var scan = await harness.RunDirectoryScanAsync();

        var warning = Assert.Single(scan.Warnings);
        Assert.Equal(ScanWarningCode.NotProductMatched, warning.Code);
        Assert.Equal(
            "Not product-matched, name too generic: " + string.Join(", ", names.Take(20).Select(name => name + " 1.0")) + " and 3 more",
            warning.Message);
        Assert.Equal(23, scan.Summary!.CleanPackages);
    }
}
