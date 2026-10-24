using System.Globalization;
using System.IO.Compression;
using System.Text;
using Airlock.Domain.Intelligence;
using Airlock.Domain.Results;
using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;

namespace Airlock.Infrastructure.Tests.Support;

/// <summary>Test-data builders, read as "A.Package(...)"; the defaults are the S0 examples.</summary>
internal static class A
{
    public static Package Package(string name = "pyyaml", string version = "5.3", string type = "python") => new(type, name, version);

    public static Cve Cve(string id, decimal score, Severity severity, string description = "A test description.") =>
        new(id, description, [new CvssScore(score, severity, CvssVersion.V31, CvssSource.Primary)], ["vendor:product"]);

    public static VulnerablePackage Vulnerable(Package package, MatchMethod method, params Cve[] cves) =>
        new(package, method, Finding.Select(cves).Findings);

    public static CpeMatch CpeMatch(string cpe) => new(new CandidateCpe(cpe, CpeSource.Dictionary));

    /// <summary>A scan of /uploads/acme-app that finished Completed, or Incomplete when it has unverified packages.</summary>
    public static Scan FinishedScan(
        int total, IEnumerable<VulnerablePackage> vulnerable, IEnumerable<UnverifiedPackage> unverified, params ScanWarning[] warnings)
    {
        var scan = Scan.Start(ScanId.New(), new DirectoryTarget("/uploads/acme-app"), Test.StartTime).EnterPhase(ScanPhase.Correlation);
        foreach (var warning in warnings)
        {
            scan = scan.AddWarning(warning);
        }

        return scan.Finish(total, vulnerable, unverified, Test.StartTime.AddMinutes(11).AddSeconds(42));
    }

    /// <summary>The worked example of the PDF layout: pyyaml's two CRITICAL findings, one unverified package, one warning.</summary>
    public static Scan IncompleteScan() => FinishedScan(
        87,
        [
            Vulnerable(
                Package(), CpeMatch("cpe:2.3:a:pyyaml:pyyaml:5.3:*:*:*:*:*:*:*"),
                Cve("CVE-2020-14343", 9.8m, Severity.Critical, "A vulnerability was discovered in the PyYAML library."),
                Cve("CVE-2020-1747", 9.8m, Severity.Critical, "A vulnerability was discovered in the PyYAML library in versions before 5.3.1.")),
            Vulnerable(Package("minimist", "1.2.5", "npm"), new ProductMatch("minimist"), Cve("CVE-2021-44906", 9.8m, Severity.Critical)),
        ],
        [UnverifiedPackage.LookupFailed(Package("failing-2", "1.0"), "NVD returned 503")],
        new ScanWarning(ScanWarningCode.CvesWithoutSeverity, "busybox 1.36.1-r31: CVE-2025-60876 not yet scored by NVD"));

    public static Scan CompletedScan() => FinishedScan(
        4,
        [Vulnerable(Package("flask", "0.12.2"), CpeMatch("cpe:2.3:a:palletsprojects:flask:0.12.2:*:*:*:*:*:*:*"), Cve("CVE-2018-1000656", 7.5m, Severity.High))],
        []);

    /// <summary>A Completed scan with <paramref name="findings"/> HIGH findings over packages of five findings each.</summary>
    public static Scan LargeScan(int findings)
    {
        var vulnerable = Enumerable.Range(0, findings / 5)
            .Select(index => Vulnerable(
                Package(string.Create(CultureInfo.InvariantCulture, $"package-{index:D4}"), "1.0"),
                new ProductMatch(string.Create(CultureInfo.InvariantCulture, $"package-{index:D4}")),
                [.. Enumerable.Range(0, 5).Select(cve => Cve(
                    string.Create(CultureInfo.InvariantCulture, $"CVE-2026-{10_000 + (index * 5) + cve}"),
                    7.5m,
                    Severity.High,
                    "A long description that wraps over several lines of the findings table, as real NVD descriptions do. "
                    + "It names the affected versions, the attack vector and what an attacker gains."))]))
            .ToList();
        return FinishedScan(vulnerable.Count + 10, vulnerable, []);
    }

    /// <summary>A ZIP archive holding the entries given, as a NuGet package or a wheel is.</summary>
    public static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var stream = archive.CreateEntry(name).Open();
                stream.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        return buffer.ToArray();
    }

    public static string Nuspec(string id = "Newtonsoft.Json", string version = "12.0.1") =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
        + "<package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\"><metadata>"
        + $"<id>{id}</id><version>{version}</version><authors>James Newton-King</authors>"
        + "</metadata><files><file src=\"lib/x.dll\" /></files></package>";
}
