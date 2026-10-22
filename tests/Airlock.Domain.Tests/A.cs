using Airlock.Domain.Intelligence;
using Airlock.Domain.Results;
using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;

namespace Airlock.Domain.Tests;

/// <summary>Test-data builders, read as "A.Package(...)"; the defaults are the S0 examples.</summary>
internal static class A
{
    /// <summary>The moment the test scans start.</summary>
    public static readonly DateTimeOffset ScanStart = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    public static Package Package(string name = "pyyaml", string version = "5.3", string type = "python") =>
        new(type, name, version);

    public static CvssScore Score(
        decimal baseScore, Severity severity, CvssVersion version = CvssVersion.V31, CvssSource source = CvssSource.Primary) =>
        new(baseScore, severity, version, source);

    public static Cve Cve(string id, params CvssScore[] scores) => new(id, "A test description.", scores, ["vendor:product"]);

    /// <summary>A finding for a CVE with one NVD score, which must be HIGH or CRITICAL to pass the threshold.</summary>
    public static Finding Finding(string id, decimal baseScore, Severity severity) =>
        Domain.Results.Finding.Select([Cve(id, Score(baseScore, severity))]).Findings.Single();

    public static VulnerablePackage Vulnerable(Package package, params Finding[] findings) =>
        new(package, new ProductMatch(ProductName.Normalize(package.Name)), findings);

    public static UnverifiedPackage Unverified(Package package) =>
        UnverifiedPackage.LookupFailed(package, "NVD returned 503.");

    public static CandidateCpe GeneratedCpe(string vendor, string product, string version = "1.0") =>
        new($"cpe:2.3:a:{vendor}:{product}:{version}:*:*:*:*:*:*:*", CpeSource.Generated);

    public static CandidateCpe DictionaryCpe(string vendor, string product, string version = "1.0") =>
        new($"cpe:2.3:a:{vendor}:{product}:{version}:*:*:*:*:*:*:*", CpeSource.Dictionary);

    public static Scan RunningScan() => Scan.Start(ScanId.New(), new DirectoryTarget("/uploads"), ScanStart);

    public static Scan ScanInCorrelation() => RunningScan().EnterPhase(ScanPhase.Correlation);
}
