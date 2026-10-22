using Airlock.Application.Logging;
using Airlock.Application.Tests.Fakes;
using Airlock.Domain.Intelligence;
using Airlock.Domain.Scanning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Airlock.Application.Tests.Quality;

// U2-NFR-09: the catalogue's messages, levels and event ids, written in the scan's scope.
public sealed class LogCatalogueTests
{
    [Fact]
    public async Task A_CPE_match_writes_the_lifecycle_and_correlation_lines()
    {
        using var harness = new ScanHarness();
        var cpe = A.CpeValue("pyyaml:pyyaml", "5.3");
        harness.Sbom.Produces(FakeSbomGenerator.Manifests, A.SbomPackage("pyyaml", "5.3", "python", "pyyaml:pyyaml"));
        harness.Catalog.Cpe(cpe, A.Cve("CVE-2020-1747", 9.8m, Severity.Critical), A.Cve("CVE-2020-14343", 9.8m, Severity.Critical));

        var scan = await harness.RunUploadScanAsync("requirements.txt");

        var records = harness.LogRecords;
        AssertLogged(records, 1101, LogLevel.Information, "🚀 Initializing vetting sequence for: uploaded artifacts", scan.Id);
        AssertLogged(records, 1102, LogLevel.Information, "📥 Processing 1 uploaded artifact(s)", scan.Id);
        AssertLogged(records, 1201, LogLevel.Debug, "📑 Generating SBOM for: requirements.txt", scan.Id);
        AssertLogged(records, 1202, LogLevel.Information, "📦 1 package(s) identified in requirements.txt", scan.Id);
        AssertLogged(records, 1206, LogLevel.Information, "⚙️ 1 unique package(s) to vet", scan.Id);
        AssertLogged(records, 1301, LogLevel.Debug, "🔍 CPE search: " + cpe, scan.Id);
        AssertLogged(records, 1302, LogLevel.Information, "🚩 Found 2 vulnerabilities via CPE for pyyaml 5.3", scan.Id);
        AssertLogged(
            records, 1104, LogLevel.Information, "✅ Vetting sequence complete: 1 vulnerable, 0 unverified, 0 clean of 1 package(s)", scan.Id);
    }

    [Fact]
    public async Task Product_matches_generic_names_unknown_versions_and_failures_have_their_own_lines()
    {
        using var harness = new ScanHarness(configure: options => options.Correlation.MaxParallelism = 1);
        harness.Sbom.Produces(
            ScanHarness.Directory,
            A.SbomPackage("minimist", "1.2.5", "npm", "minimist:minimist"),
            A.SbomPackage(A.Package("lodash", "4.17.21", "npm")),
            A.SbomPackage("flask", "2.0.0", "python", "flask:flask"),
            A.SbomPackage(A.Package("cloud.google.com/go/auth", "v0.23.3", "go-module")),
            A.SbomPackage(A.Package("syft", "UNKNOWN", "binary")),
            A.SbomPackage("pyyaml", "5.3", "python", "pyyaml:pyyaml"),
            A.SbomPackage(A.Package("jinja2", "2.10")));
        harness.Catalog
            .Product("minimist", "1.2.5", A.Cve("CVE-2021-44906", 9.8m, Severity.Critical))
            .Cpe(A.CpeValue("flask:flask", "2.0.0"), A.Cve("CVE-2099-1001", 5.3m, Severity.Medium))
            .FailCpe(A.CpeValue("pyyaml:pyyaml", "5.3"), "NVD returned 503")
            .FailProduct("jinja2", "2.10", "timed out");

        var scan = await harness.RunDirectoryScanAsync();

        var records = harness.LogRecords;
        AssertLogged(records, 1304, LogLevel.Debug, "🔍 Product search: 'minimist' 1.2.5", scan.Id);
        AssertLogged(records, 1305, LogLevel.Information, "🚩 Found 1 vulnerabilities via product match for minimist 1.2.5", scan.Id);
        AssertLogged(records, 1306, LogLevel.Debug, "✅ No vulnerabilities found via product match for lodash 4.17.21", scan.Id);
        AssertLogged(records, 1303, LogLevel.Debug, "✅ No vulnerabilities found for this CPE: " + A.CpeValue("flask:flask", "2.0.0"), scan.Id);
        AssertLogged(
            records, 1307, LogLevel.Debug, "⏭ cloud.google.com/go/auth v0.23.3: name too generic for a product match", scan.Id);
        AssertLogged(records, 1310, LogLevel.Warning, "⚠️ syft UNKNOWN: version unknown, not checked", scan.Id);
        AssertLogged(
            records, 1308, LogLevel.Warning, $"⚠️ NVD CPE Search failed for {A.CpeValue("pyyaml:pyyaml", "5.3")}: NVD returned 503", scan.Id);
        AssertLogged(records, 1309, LogLevel.Warning, "⚠️ NVD product search failed for jinja2 2.10: timed out", scan.Id);
    }

    private static void AssertLogged(IReadOnlyList<FakeLogRecord> records, int eventId, LogLevel level, string message, ScanId scanId)
    {
        var record = Assert.Single(records, record => record.Id.Id == eventId && record.Message == message);
        Assert.Equal(level, record.Level);
        Assert.Contains(
            record.Scopes,
            scope => scope is IEnumerable<KeyValuePair<string, object?>> pairs
                && pairs.Any(pair => pair.Key == ScanLogScope.Key && Equals(pair.Value, scanId)));
    }
}
