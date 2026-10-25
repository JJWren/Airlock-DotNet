using System.Text;
using System.Text.Json;
using Airlock.Application.Events;
using Airlock.Domain.Intelligence;
using Airlock.Domain.Results;
using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;
using Airlock.Web.Api.Contracts;
using Airlock.Web.Api.Mapping;
using Airlock.Web.Tests.Support;
using Microsoft.Extensions.Logging;

namespace Airlock.Web.Tests.Contract;

// domain-entities.md — every Domain value a scan can carry has its value in the contract.
public sealed class MappingTests
{
    [Fact]
    public void Every_kind_of_warning_and_unverified_package_has_its_value()
    {
        var scan = Scan.Start(WorkedExample.Id, WorkedExample.Target, Test.StartTime)
            .AddWarning(new ScanWarning(ScanWarningCode.NoPackagesIdentified, "No packages identified in notes.docx", "notes.docx"))
            .AddWarning(new ScanWarning(ScanWarningCode.DistributionBackportsNotConsidered, "Distribution backports are not considered for alpine-3.20.tar", "alpine-3.20.tar"))
            .AddWarning(new ScanWarning(ScanWarningCode.CvesWithoutSeverity, "CVE-2026-1 has no severity yet", "pyyaml 5.3"))
            .AddWarning(new ScanWarning(ScanWarningCode.NotProductMatched, "Names too generic to product-match: core"))
            .EnterPhase(ScanPhase.Correlation)
            .RecordProgress(1, 1)
            .EnterPhase(ScanPhase.Summary)
            .Finish(1, [], [UnverifiedPackage.VersionUnknown(new Package("go-module", "syft", "UNKNOWN"))], Test.StartTime);

        var dto = ScanDtoMapper.ToDto(scan);

        Assert.Equal(
            [WarningCodeValue.NoPackagesIdentified, WarningCodeValue.DistributionBackportsNotConsidered, WarningCodeValue.CvesWithoutSeverity, WarningCodeValue.NotProductMatched],
            dto.Warnings.Select(warning => warning.Code));
        Assert.Null(dto.Warnings[3].Input);
        var unverified = Assert.Single(dto.UnverifiedPackages);
        Assert.Equal(UnverifiedReasonValue.VersionUnknown, unverified.Reason);
        Assert.Equal("version unknown", unverified.Detail);
        Assert.Equal(ScanStatusValue.Incomplete, dto.Status);
    }

    [Theory]
    [InlineData(CvssVersion.V40, CvssSource.Primary, "4.0", "PRIMARY")]
    [InlineData(CvssVersion.V20, CvssSource.Secondary, "2.0", "SECONDARY")]
    [InlineData(CvssVersion.V30, CvssSource.Primary, "3.0", "PRIMARY")]
    public void A_findings_score_carries_its_cvss_version_and_source(CvssVersion version, CvssSource source, string written, string scoreSource)
    {
        var cve = new Cve("CVE-2026-0001", "A record.", [new CvssScore(9.1m, Severity.Critical, version, source)], []);
        var vulnerable = new VulnerablePackage(
            new Package("npm", "lodash", "4.17.20"),
            new CpeMatch(new CandidateCpe("cpe:2.3:a:lodash:lodash:4.17.20:*:*:*:*:node.js:*:*", CpeSource.Dictionary)),
            Finding.Select([cve]).Findings);
        var scan = Scan.Start(WorkedExample.Id, WorkedExample.Target, Test.StartTime).EnterPhase(ScanPhase.Correlation)
            .RecordProgress(1, 1).EnterPhase(ScanPhase.Summary).Finish(1, [vulnerable], [], Test.StartTime);

        var package = Assert.Single(ScanDtoMapper.ToDto(scan).VulnerablePackages);

        Assert.Equal(CpeSourceValue.Dictionary, package.MatchMethod.CpeSource);
        using var finding = JsonDocument.Parse(ApiJson.SerializeCompact(Assert.Single(package.Findings)));
        Assert.Equal(written, finding.RootElement.GetProperty("cvssVersion").GetString());
        Assert.Equal(scoreSource, finding.RootElement.GetProperty("scoreSource").GetString());
    }

    [Fact]
    public void A_cancelled_scan_carries_its_reason_and_no_results()
    {
        var scan = Scan.Start(WorkedExample.Id, new DirectoryTarget("/uploads/acme-app"), Test.StartTime).Cancel("Emergency Stop", Test.StartTime.AddSeconds(3));

        var dto = ScanDtoMapper.ToDto(scan);

        Assert.Equal(ScanStatusValue.Cancelled, dto.Status);
        Assert.Equal("Emergency Stop", dto.CancellationReason);
        Assert.Equal(3.000m, dto.DurationSeconds);
        Assert.Null(dto.Summary);
    }

    [Theory]
    [InlineData(LogLevel.Trace, "TRACE")]
    [InlineData(LogLevel.Debug, "DEBUG")]
    [InlineData(LogLevel.Information, "INFO")]
    [InlineData(LogLevel.Warning, "WARNING")]
    [InlineData(LogLevel.Error, "ERROR")]
    [InlineData(LogLevel.Critical, "CRITICAL")]
    public void Live_log_levels_are_the_names_log_level_accepts(LogLevel level, string written)
    {
        var item = EventMapper.ForLiveLog(new LiveLogEntry(Test.StartTime, level, "Airlock.Test", "A line", WorkedExample.Id));

        using var data = JsonDocument.Parse(item.Data);
        Assert.Equal("log", item.EventType);
        Assert.Equal(written, data.RootElement.GetProperty("level").GetString());
        Assert.Equal(WorkedExample.Id.ToString(), data.RootElement.GetProperty("scanId").GetString());
    }

    [Fact]
    public void A_line_at_no_level_is_never_mapped()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EventMapper.ForLiveLog(new LiveLogEntry(Test.StartTime, LogLevel.None, "Airlock.Test", "A line", null)));
    }

    [Fact]
    public void Each_scan_event_has_its_name()
    {
        ScanEvent[] events =
        [
            new ScanSnapshot(WorkedExample.Id, WorkedExample.Running()),
            new PhaseChanged(WorkedExample.Id, ScanPhase.Summary),
            new PackagesCorrelated(WorkedExample.Id, new ScanProgress(3, 4)),
            new ScanLogged(WorkedExample.Id, new LiveLogEntry(Test.StartTime, LogLevel.Information, "Airlock.Test", "A line", WorkedExample.Id)),
            new ScanFinished(WorkedExample.Id, ScanStatus.Failed),
        ];

        var items = events.Select(EventMapper.ForScan).ToList();

        Assert.Equal(["snapshot", "phase", "progress", "log", "finished"], items.Select(item => item.EventType));
        Assert.Equal("{\"status\":\"FAILED\"}", Encoding.UTF8.GetString(items[^1].Data));
        Assert.Equal("{\"correlated\":3,\"total\":4}", Encoding.UTF8.GetString(items[2].Data));
    }
}
