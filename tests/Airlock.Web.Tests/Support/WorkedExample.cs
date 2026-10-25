using System.Text.Json;
using Airlock.Domain.Intelligence;
using Airlock.Domain.Results;
using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;

namespace Airlock.Web.Tests.Support;

/// <summary>
/// The worked example of the U4 logic model: the S0 requirements.txt, with the descriptions NVD recorded for it in U3's
/// fixtures. NVD answered 503 for jinja2 2.10, so the scan is Incomplete.
/// </summary>
internal static class WorkedExample
{
    public static readonly ScanId Id = new(Guid.Parse("01a1637b-7e00-7c3a-9f12-6b84d0e5a1c7"));
    public static readonly DateTimeOffset StartedAt = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset FinishedAt = StartedAt.AddMilliseconds(92_418);

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Descriptions = new(ReadDescriptions);

    public static ArtifactsTarget Target => new([new Artifact("requirements.txt", 56)]);

    /// <summary>The scan while it correlates, as "Poll while it runs" shows it.</summary>
    public static Scan Running() =>
        Scan.Start(Id, Target, StartedAt).EnterPhase(ScanPhase.Correlation).RecordProgress(0, 4).RecordProgress(2, 4);

    /// <summary>The finished scan, which is also the JSON export.</summary>
    public static Scan Finished()
    {
        var pyyaml = new VulnerablePackage(
            new Package("python", "pyyaml", "5.3"),
            new CpeMatch(new CandidateCpe("cpe:2.3:a:pyyaml:pyyaml:5.3:*:*:*:*:*:*:*", CpeSource.Generated)),
            Finding.Select([Record("CVE-2020-14343", 9.8m, Severity.Critical, CvssVersion.V31), Record("CVE-2020-1747", 9.8m, Severity.Critical, CvssVersion.V31)]).Findings);
        var flask = new VulnerablePackage(
            new Package("python", "flask", "0.12.2"),
            new ProductMatch("flask"),
            Finding.Select(
            [
                Record("CVE-2018-1000656", 7.5m, Severity.High, CvssVersion.V30),
                Record("CVE-2019-1010083", 7.5m, Severity.High, CvssVersion.V30),
                Record("CVE-2023-30861", 7.5m, Severity.High, CvssVersion.V31),
            ]).Findings);
        var jinja2 = UnverifiedPackage.LookupFailed(new Package("python", "jinja2", "2.10"), "NVD returned 503");

        return Scan.Start(Id, Target, StartedAt)
            .EnterPhase(ScanPhase.Correlation)
            .RecordProgress(0, 4)
            .RecordProgress(4, 4)
            .EnterPhase(ScanPhase.Summary)
            .Finish(4, [pyyaml, flask], [jinja2], FinishedAt);
    }

    public static string Description(string cveId) => Descriptions.Value[cveId];

    private static Cve Record(string id, decimal score, Severity severity, CvssVersion version) =>
        new(id, Description(id), [new CvssScore(score, severity, version, CvssSource.Primary)], []);

    private static Dictionary<string, string> ReadDescriptions()
    {
        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in new[] { "cpe-pyyaml-5.3.json", "cpe-flask-0.12.2.json" })
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Nvd", file)));
            foreach (var vulnerability in document.RootElement.GetProperty("vulnerabilities").EnumerateArray())
            {
                var cve = vulnerability.GetProperty("cve");
                var english = cve.GetProperty("descriptions").EnumerateArray().First(entry => entry.GetProperty("lang").GetString() == "en");
                descriptions[cve.GetProperty("id").GetString()!] = english.GetProperty("value").GetString()!;
            }
        }

        return descriptions;
    }
}
