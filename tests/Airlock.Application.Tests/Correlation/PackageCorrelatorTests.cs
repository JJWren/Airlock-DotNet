using Airlock.Application.Correlation;
using Airlock.Application.Tests.Fakes;
using Airlock.Domain.Intelligence;
using Airlock.Domain.Results;
using Airlock.Domain.Targets;

namespace Airlock.Application.Tests.Correlation;

// AR-13 to AR-17 — rows of the correlation tables, the original's two tests and the mockData.js false positives.
public sealed class PackageCorrelatorTests
{
    private static readonly string[] PyyamlGuesses =
    [
        "python-pyyaml:python-pyyaml", "python-pyyaml:python_pyyaml", "python_pyyaml:python-pyyaml",
        "python_pyyaml:python_pyyaml", "python-pyyaml:pyyaml", "python:python-pyyaml", "python:python_pyyaml",
        "python_pyyaml:pyyaml", "pyyaml:python-pyyaml", "pyyaml:python_pyyaml", "python:pyyaml", "pyyaml:pyyaml",
    ];

    private static readonly string[] FlaskGuesses =
    [
        "python-flask:python-flask", "python-flask:python_flask", "python_flask:python-flask",
        "python_flask:python_flask", "python:python-flask", "python:python_flask", "flask:python-flask",
        "flask:python_flask", "python-flask:flask", "python_flask:flask", "python:flask", "flask:flask",
    ];

    [Fact]
    public async Task Pyyaml_is_a_CPE_match_on_its_likeliest_candidate()
    {
        using var harness = new ScanHarness();
        var cpe = A.CpeValue("pyyaml:pyyaml", "5.3");
        harness.Catalog.Cpe(cpe, A.Cve("CVE-2020-1747", 9.8m, Severity.Critical), A.Cve("CVE-2020-14343", 9.8m, Severity.Critical));

        var result = await CorrelateAsync(harness, A.SbomPackage("pyyaml", "5.3", "python", PyyamlGuesses));

        var vulnerable = Assert.IsType<VulnerableCorrelation>(result);
        Assert.Equal(cpe, Assert.IsType<CpeMatch>(vulnerable.Result.Method).Cpe.Value);
        // BR-07: equal scores are ordered by CVE id.
        Assert.Equal(["CVE-2020-14343", "CVE-2020-1747"], vulnerable.Result.Findings.Select(finding => finding.Cve.Id));
        Assert.Equal(Severity.Critical, vulnerable.Result.HighestSeverity);
        Assert.Equal([cpe], harness.Catalog.Calls);
    }

    [Fact]
    public async Task A_CPE_match_with_only_lower_severity_CVEs_is_clean_and_makes_no_product_lookup()
    {
        using var harness = new ScanHarness();
        var cpe = A.CpeValue("flask:flask", "2.0.0");
        harness.Catalog.Cpe(cpe, A.Cve("CVE-2099-1001", 5.3m, Severity.Medium));

        var result = await CorrelateAsync(harness, A.SbomPackage("flask", "2.0.0", "python", FlaskGuesses));

        var clean = Assert.IsType<CleanCorrelation>(result);
        Assert.Equal(cpe, Assert.IsType<CpeMatch>(clean.Method).Cpe.Value);
        Assert.Equal([cpe], harness.Catalog.Calls);
    }

    [Fact]
    public async Task A_product_match_finds_what_the_CPE_guesses_missed()
    {
        using var harness = new ScanHarness();
        harness.Catalog.Product("minimist", "1.2.5", A.Cve("CVE-2021-44906", 9.8m, Severity.Critical));

        var result = await CorrelateAsync(harness, A.SbomPackage("minimist", "1.2.5", "npm", "minimist:minimist"));

        var vulnerable = Assert.IsType<VulnerableCorrelation>(result);
        Assert.Equal("minimist", Assert.IsType<ProductMatch>(vulnerable.Result.Method).Product);
        Assert.Equal("CVE-2021-44906", Assert.Single(vulnerable.Result.Findings).Cve.Id);
        Assert.Equal(
            [A.CpeValue("minimist:minimist", "1.2.5"), FakeVulnerabilityCatalog.ProductKey("minimist", "1.2.5")],
            harness.Catalog.Calls);
    }

    [Fact]
    public async Task The_original_CPE_test_still_finds_its_CVE()
    {
        using var harness = new ScanHarness();
        var cpe = A.CpeValue("python-requests:requests", "2.25.1");
        harness.Catalog.Cpe(cpe, A.Cve("CVE-2021-28363", 7.5m, Severity.High));

        var result = await CorrelateAsync(harness, A.SbomPackage("requests", "2.25.1", "python", "python-requests:requests"));

        var vulnerable = Assert.IsType<VulnerableCorrelation>(result);
        Assert.IsType<CpeMatch>(vulnerable.Result.Method);
        Assert.Equal("CVE-2021-28363", Assert.Single(vulnerable.Result.Findings).Cve.Id);
        Assert.Equal([cpe], harness.Catalog.Calls);
    }

    [Fact]
    public async Task The_original_keyword_fallback_test_is_now_a_product_match()
    {
        using var harness = new ScanHarness();
        harness.Catalog.Product("flask", "0.12.2", A.Cve("CVE-2018-1000656", 7.5m, Severity.High, CvssVersion.V30));

        var result = await CorrelateAsync(harness, A.SbomPackage("flask", "0.12.2", "python", "wrong-vendor:flask"));

        var vulnerable = Assert.IsType<VulnerableCorrelation>(result);
        Assert.Equal("flask", Assert.IsType<ProductMatch>(vulnerable.Result.Method).Product);
        Assert.Equal("CVE-2018-1000656", Assert.Single(vulnerable.Result.Findings).Cve.Id);
        Assert.Equal(
            [A.CpeValue("wrong-vendor:flask", "0.12.2"), FakeVulnerabilityCatalog.ProductKey("flask", "0.12.2")],
            harness.Catalog.Calls);
    }

    [Fact]
    public async Task Description_only_mentions_never_become_findings()
    {
        using var harness = new ScanHarness();
        harness.Catalog.Product(
            "requests", "2.25.1", A.Cve("CVE-2023-32681", 6.1m, Severity.Medium), A.Cve("CVE-2026-25645", 5.3m, Severity.Medium));
        var package = A.SbomPackage(A.Package("requests", "2.25.1"), A.Cpe("python:requests", "2.25.1", CpeSource.Dictionary));

        var result = await CorrelateAsync(harness, package);

        var clean = Assert.IsType<CleanCorrelation>(result);
        Assert.Equal("requests", Assert.IsType<ProductMatch>(clean.Method).Product);
        Assert.DoesNotContain(harness.Catalog.Calls, call => call.Contains("geoserver", StringComparison.Ordinal));
        Assert.Equal(
            [A.CpeValue("python:requests", "2.25.1"), FakeVulnerabilityCatalog.ProductKey("requests", "2.25.1")],
            harness.Catalog.Calls);
    }

    [Fact]
    public async Task A_package_without_candidates_goes_straight_to_the_product_stage()
    {
        using var harness = new ScanHarness();

        var result = await CorrelateAsync(harness, A.SbomPackage(A.Package("System.Text.Json", "10.0.11", "dotnet")));

        Assert.Equal("system.text.json", Assert.IsType<ProductMatch>(Assert.IsType<CleanCorrelation>(result).Method).Product);
        Assert.Equal([FakeVulnerabilityCatalog.ProductKey("system.text.json", "10.0.11")], harness.Catalog.Calls);
    }

    [Fact]
    public async Task A_name_too_generic_to_product_match_leaves_the_package_clean_without_a_method()
    {
        using var harness = new ScanHarness();

        var result = await CorrelateAsync(
            harness, A.SbomPackage("cloud.google.com/go/auth", "v0.23.3", "go-module", "google:auth"));

        Assert.Null(Assert.IsType<CleanCorrelation>(result).Method);
        Assert.Equal([A.CpeValue("google:auth", "v0.23.3")], harness.Catalog.Calls);
    }

    [Fact]
    public async Task A_package_without_a_version_is_unverified_and_never_looked_up()
    {
        using var harness = new ScanHarness();

        var result = await CorrelateAsync(harness, A.SbomPackage("syft", "UNKNOWN", "binary", "anchore:syft"));

        var unverified = Assert.IsType<UnverifiedCorrelation>(result);
        Assert.Equal(UnverifiedReason.VersionUnknown, unverified.Result.Reason);
        Assert.Empty(harness.Catalog.Calls);
    }

    [Fact]
    public async Task A_CVE_without_a_score_is_listed_not_counted()
    {
        using var harness = new ScanHarness();
        var cpe = A.CpeValue("busybox:busybox", "1.36.1-r31");
        harness.Catalog.Cpe(cpe, A.UnscoredCve("CVE-2025-60876"));

        var result = await CorrelateAsync(harness, A.SbomPackage("busybox", "1.36.1-r31", "apk", "busybox:busybox"));

        var clean = Assert.IsType<CleanCorrelation>(result);
        Assert.IsType<CpeMatch>(clean.Method);
        Assert.Equal(["CVE-2025-60876"], clean.UnscoredCveIds);
    }

    [Fact]
    public async Task The_next_candidate_is_tried_when_one_has_no_records()
    {
        using var harness = new ScanHarness();
        var second = A.CpeValue("python-pyyaml:pyyaml", "5.3");
        harness.Catalog.Cpe(second, A.Cve("CVE-2020-1747", 9.8m, Severity.Critical));

        var result = await CorrelateAsync(harness, A.SbomPackage("pyyaml", "5.3", "python", "pyyaml:pyyaml", "python-pyyaml:pyyaml"));

        Assert.Equal(second, Assert.IsType<CpeMatch>(Assert.IsType<VulnerableCorrelation>(result).Result.Method).Cpe.Value);
        Assert.Equal([A.CpeValue("pyyaml:pyyaml", "5.3"), second], harness.Catalog.Calls);
    }

    [Fact]
    public async Task A_failed_lookup_stops_the_package_as_unverified()
    {
        using var harness = new ScanHarness();
        var first = A.CpeValue("pyyaml:pyyaml", "5.3");
        harness.Catalog
            .FailCpe(first, "NVD returned 503")
            .Cpe(A.CpeValue("python-pyyaml:pyyaml", "5.3"), A.Cve("CVE-2020-1747", 9.8m, Severity.Critical));

        var result = await CorrelateAsync(harness, A.SbomPackage("pyyaml", "5.3", "python", "pyyaml:pyyaml", "python-pyyaml:pyyaml"));

        var unverified = Assert.IsType<UnverifiedCorrelation>(result);
        Assert.Equal(UnverifiedReason.LookupFailed, unverified.Result.Reason);
        Assert.Equal("NVD returned 503", unverified.Result.Detail);
        Assert.Equal([first], harness.Catalog.Calls);
    }

    [Fact]
    public async Task A_failed_product_lookup_makes_the_package_unverified()
    {
        using var harness = new ScanHarness();
        harness.Catalog.FailProduct("minimist", "1.2.5", "timed out");

        var result = await CorrelateAsync(harness, A.SbomPackage("minimist", "1.2.5", "npm", "minimist:minimist"));

        Assert.Equal("timed out", Assert.IsType<UnverifiedCorrelation>(result).Result.Detail);
    }

    [Fact]
    public async Task A_failure_reason_from_NVD_is_cleaned()
    {
        using var harness = new ScanHarness();
        var cpe = A.CpeValue("pyyaml:pyyaml", "5.3");
        harness.Catalog.FailCpe(cpe, $"NVD returned 503{(char)13}{(char)10}{(char)27}[31mretry later{(char)27}[0m");

        var result = await CorrelateAsync(harness, A.SbomPackage("pyyaml", "5.3", "python", "pyyaml:pyyaml"));

        Assert.Equal("NVD returned 503 retry later", Assert.IsType<UnverifiedCorrelation>(result).Result.Detail);
    }

    [Fact]
    public async Task An_open_breaker_makes_the_package_unverified_without_a_lookup()
    {
        using var harness = new ScanHarness();
        var breaker = new NvdOutageBreaker(3);
        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordFailure();

        var result = await harness.Correlator.CorrelateAsync(
            A.SbomPackage("pyyaml", "5.3", "python", "pyyaml:pyyaml"), breaker, harness.Token);

        Assert.Equal(
            "NVD unavailable — not checked after 3 failed lookups in a row",
            Assert.IsType<UnverifiedCorrelation>(result).Result.Detail);
        Assert.Empty(harness.Catalog.Calls);
    }

    [Fact]
    public async Task A_breaker_that_opens_during_the_CPE_stage_stops_the_product_lookup()
    {
        using var harness = new ScanHarness();
        var breaker = new NvdOutageBreaker(3);
        harness.Catalog.Fallback = (_, _) =>
        {
            breaker.RecordFailure();
            breaker.RecordFailure();
            breaker.RecordFailure();
            return Task.FromResult<Airlock.Application.Ports.CatalogLookup>(new Airlock.Application.Ports.CatalogNoRecords());
        };

        var result = await harness.Correlator.CorrelateAsync(
            A.SbomPackage("minimist", "1.2.5", "npm", "minimist:minimist"), breaker, harness.Token);

        Assert.IsType<UnverifiedCorrelation>(result);
        Assert.Single(harness.Catalog.Calls);
    }

    [Fact]
    public async Task Null_arguments_are_rejected()
    {
        using var harness = new ScanHarness();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await harness.Correlator.CorrelateAsync(null!, new NvdOutageBreaker(5), harness.Token));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await harness.Correlator.CorrelateAsync(A.SbomPackage(A.Package()), null!, harness.Token));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NvdOutageBreaker(0));
    }

    private static async Task<PackageCorrelation> CorrelateAsync(ScanHarness harness, Airlock.Application.Ports.SbomPackage package) =>
        await harness.Correlator.CorrelateAsync(package, new NvdOutageBreaker(5), harness.Token);
}
