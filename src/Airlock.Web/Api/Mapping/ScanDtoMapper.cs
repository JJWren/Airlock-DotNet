using Airlock.Domain.Intelligence;
using Airlock.Domain.Results;
using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;
using Airlock.Web.Api.Contracts;

namespace Airlock.Web.Api.Mapping;

/// <summary>Maps a scan to the API's resource (domain-entities.md), value by value.</summary>
internal static class ScanDtoMapper
{
    public static ScanDto ToDto(Scan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        var startedAt = ApiTime.Truncate(scan.StartedAt);
        var finishedAt = scan.FinishedAt is { } finished ? ApiTime.Truncate(finished) : (DateTimeOffset?)null;
        return new ScanDto(
            scan.Id.ToString(),
            Status(scan.Status),
            Phase(scan.Phase),
            scan.Progress is { } progress ? new ScanProgressDto(progress.Correlated, progress.Total) : null,
            Target(scan.Target),
            ApiTime.Format(startedAt),
            finishedAt is { } end ? ApiTime.Format(end) : null,
            finishedAt is { } stop ? Seconds(stop - startedAt) : null,
            scan.Summary is { } summary ? Summary(summary) : null,
            [.. scan.VulnerablePackages.Select(Vulnerable)],
            [.. scan.UnverifiedPackages.Select(Unverified)],
            [.. scan.Warnings.Select(warning => new ScanWarningDto(WarningCode(warning.Code), warning.Message, warning.Input))],
            scan.Failure is { } failure ? new ScanFailureDto(FailureCode(failure.Code), failure.Reason, failure.Input) : null,
            scan.CancellationReason);
    }

    public static ScanStatusValue Status(ScanStatus status) => status switch
    {
        ScanStatus.Running => ScanStatusValue.Running,
        ScanStatus.Completed => ScanStatusValue.Completed,
        ScanStatus.Incomplete => ScanStatusValue.Incomplete,
        ScanStatus.Failed => ScanStatusValue.Failed,
        ScanStatus.Cancelled => ScanStatusValue.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static ScanPhaseValue Phase(ScanPhase phase) => phase switch
    {
        ScanPhase.SbomGeneration => ScanPhaseValue.SbomGeneration,
        ScanPhase.Correlation => ScanPhaseValue.Correlation,
        ScanPhase.Summary => ScanPhaseValue.Summary,
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null),
    };

    // WR-02: three decimal places, from the whole milliseconds the times show.
    private static decimal Seconds(TimeSpan duration) =>
        (duration.Ticks / TimeSpan.TicksPerMillisecond / 1000m) + 0.000m;

    private static ScanTargetDto Target(ScanTarget target) => target switch
    {
        DirectoryTarget directory => new ScanTargetDto(TargetKindValue.Directory, directory.Path, []),
        ArtifactsTarget artifacts => new ScanTargetDto(
            TargetKindValue.Artifacts,
            null,
            [.. artifacts.Artifacts.Select(artifact => new ArtifactDto(artifact.FileName, artifact.SizeBytes, ArtifactKind(artifact.Kind)))]),
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
    };

    private static ArtifactKindValue ArtifactKind(ArtifactKind kind) => kind switch
    {
        Domain.Targets.ArtifactKind.Manifest => ArtifactKindValue.Manifest,
        Domain.Targets.ArtifactKind.Other => ArtifactKindValue.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static ScanSummaryDto Summary(ScanSummary summary) => new(
        summary.TotalPackages,
        summary.VulnerablePackages,
        summary.CleanPackages,
        summary.UnverifiedPackages,
        summary.CriticalFindings,
        summary.HighFindings);

    private static VulnerablePackageDto Vulnerable(VulnerablePackage vulnerable) => new(
        vulnerable.Package.Type,
        vulnerable.Package.Name,
        vulnerable.Package.Version,
        Severity(vulnerable.HighestSeverity),
        Method(vulnerable.Method),
        [.. vulnerable.Findings.Select(Finding)]);

    private static MatchMethodDto Method(MatchMethod method) => method switch
    {
        CpeMatch cpe => new MatchMethodDto(MatchKindValue.Cpe, cpe.Cpe.Value, CpeSource(cpe.Cpe.Source), null),
        ProductMatch product => new MatchMethodDto(MatchKindValue.Product, null, null, product.Product),
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
    };

    private static CpeSourceValue CpeSource(CpeSource source) => source switch
    {
        Domain.Intelligence.CpeSource.Dictionary => CpeSourceValue.Dictionary,
        Domain.Intelligence.CpeSource.Generated => CpeSourceValue.Generated,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };

    // A finding carries the score Airlock uses (U1 BR-01, BR-02), not every score NVD lists.
    private static FindingDto Finding(Finding finding) => new(
        finding.Cve.Id,
        Severity(finding.Severity),
        finding.Score.BaseScore,
        CvssVersion(finding.Score.Version),
        finding.Score.Source == CvssSource.Primary ? ScoreSourceValue.Primary : ScoreSourceValue.Secondary,
        finding.Cve.NvdUrl.AbsoluteUri,
        finding.Cve.Description);

    // Findings are HIGH or CRITICAL by the threshold (U1 BR-04).
    private static SeverityValue Severity(Severity severity) => severity switch
    {
        Domain.Intelligence.Severity.High => SeverityValue.High,
        Domain.Intelligence.Severity.Critical => SeverityValue.Critical,
        _ => throw new ArgumentOutOfRangeException(nameof(severity), severity, "Findings are HIGH or CRITICAL."),
    };

    private static string CvssVersion(CvssVersion version) => version switch
    {
        Domain.Intelligence.CvssVersion.V20 => "2.0",
        Domain.Intelligence.CvssVersion.V30 => "3.0",
        Domain.Intelligence.CvssVersion.V31 => "3.1",
        Domain.Intelligence.CvssVersion.V40 => "4.0",
        _ => throw new ArgumentOutOfRangeException(nameof(version), version, null),
    };

    private static UnverifiedPackageDto Unverified(UnverifiedPackage unverified) => new(
        unverified.Package.Type,
        unverified.Package.Name,
        unverified.Package.Version,
        unverified.Reason == UnverifiedReason.LookupFailed ? UnverifiedReasonValue.LookupFailed : UnverifiedReasonValue.VersionUnknown,
        unverified.Detail);

    private static WarningCodeValue WarningCode(ScanWarningCode code) => code switch
    {
        ScanWarningCode.NoPackagesIdentified => WarningCodeValue.NoPackagesIdentified,
        ScanWarningCode.DistributionBackportsNotConsidered => WarningCodeValue.DistributionBackportsNotConsidered,
        ScanWarningCode.CvesWithoutSeverity => WarningCodeValue.CvesWithoutSeverity,
        ScanWarningCode.NotProductMatched => WarningCodeValue.NotProductMatched,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };

    private static FailureCodeValue FailureCode(ScanFailureCode code) => code switch
    {
        ScanFailureCode.SyftFailed => FailureCodeValue.SyftFailed,
        ScanFailureCode.SyftTimedOut => FailureCodeValue.SyftTimedOut,
        ScanFailureCode.SyftOutputUnreadable => FailureCodeValue.SyftOutputUnreadable,
        ScanFailureCode.ScanTimedOut => FailureCodeValue.ScanTimedOut,
        ScanFailureCode.Unexpected => FailureCodeValue.Unexpected,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };
}
