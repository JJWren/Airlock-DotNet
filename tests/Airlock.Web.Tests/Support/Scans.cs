using Airlock.Domain.Intelligence;
using Airlock.Domain.Results;
using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;

namespace Airlock.Web.Tests.Support;

/// <summary>Finished scans for the Reports tests, besides the worked example.</summary>
internal static class Scans
{
    public static readonly DateTimeOffset StartedAt = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset FinishedAt = StartedAt.AddMilliseconds(92_418);

    public static DirectoryTarget Directory => new("/uploads/acme-app");

    public static ArtifactsTarget Artifacts(params string[] names) => new(names.Select(name => new Artifact(name, 56)));

    public static Scan Running(ScanTarget? target = null) => Scan.Start(ScanId.New(), target ?? Directory, StartedAt);

    public static Scan Failed(string reason = "Syft failed for /uploads/acme-app") =>
        Running().Fail(new ScanFailure(ScanFailureCode.SyftFailed, reason, "/uploads/acme-app"), FinishedAt);

    public static Scan Cancelled(string reason = "Emergency Stop") => Running().Cancel(reason, FinishedAt);

    /// <summary>A finished scan of the packages given, in any order; the domain orders them.</summary>
    public static Scan Finished(
        int totalPackages,
        IEnumerable<VulnerablePackage> vulnerable,
        IEnumerable<UnverifiedPackage>? unverified = null,
        IEnumerable<ScanWarning>? warnings = null,
        ScanTarget? target = null)
    {
        var scan = Running(target);
        foreach (var warning in warnings ?? [])
        {
            scan = scan.AddWarning(warning);
        }

        return scan.Finish(totalPackages, vulnerable, unverified ?? [], FinishedAt);
    }

    public static VulnerablePackage Vulnerable(string name, string version, params Finding[] findings) =>
        new(new Package("python", name, version), new CpeMatch(new CandidateCpe(A.CpeFor(name, version), CpeSource.Generated)), findings);

    public static UnverifiedPackage Unverified(string name, string version = "1.0", string detail = "NVD returned 503") =>
        UnverifiedPackage.LookupFailed(new Package("python", name, version), detail);

    public static Finding FindingOf(string id, decimal score, Severity severity, string description = "A test vulnerability.", CvssSource source = CvssSource.Primary) =>
        Finding.Select([new Cve(id, description, [new CvssScore(score, severity, CvssVersion.V31, source)], [])]).Findings[0];

    /// <summary>U5-NFR-04: a scan of <paramref name="rows"/> rows, three in four vulnerable, the rest unverified.</summary>
    public static Scan Large(int rows)
    {
        var vulnerable = new List<VulnerablePackage>();
        var unverified = new List<UnverifiedPackage>();
        for (var i = 0; i < rows; i++)
        {
            var name = $"package-{i:0000}";
            if (i % 4 == 3)
            {
                unverified.Add(Unverified(name));
            }
            else
            {
                vulnerable.Add(Vulnerable(name, "1.0", FindingOf($"CVE-2026-{10000 + i}", i % 2 == 0 ? 9.8m : 7.5m, i % 2 == 0 ? Severity.Critical : Severity.High)));
            }
        }

        return Finished(rows + 100, vulnerable, unverified);
    }
}