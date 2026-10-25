namespace Airlock.Web.Api.Contracts;

// functional-design/domain-entities.md. Every property is always present: null when it does not apply, [] for an empty
// list (WR-02). Times are UTC strings with milliseconds and Z (ApiTime).

/// <summary>The answer to a submission (202).</summary>
internal sealed record ScanAcceptedDto(string Id, ScanStatusValue Status);

/// <summary>The scan resource: <c>GET /api/v2/scans/{id}</c>, the <c>snapshot</c> event and the JSON export.</summary>
internal sealed record ScanDto(
    string Id,
    ScanStatusValue Status,
    ScanPhaseValue Phase,
    ScanProgressDto? Progress,
    ScanTargetDto Target,
    string StartedAt,
    string? FinishedAt,
    decimal? DurationSeconds,
    ScanSummaryDto? Summary,
    IReadOnlyList<VulnerablePackageDto> VulnerablePackages,
    IReadOnlyList<UnverifiedPackageDto> UnverifiedPackages,
    IReadOnlyList<ScanWarningDto> Warnings,
    ScanFailureDto? Failure,
    string? CancellationReason);

internal sealed record ScanProgressDto(int Correlated, int Total);

internal sealed record ScanTargetDto(TargetKindValue Kind, string? Path, IReadOnlyList<ArtifactDto> Artifacts);

internal sealed record ArtifactDto(string FileName, long SizeBytes, ArtifactKindValue Kind);

internal sealed record ScanSummaryDto(
    int TotalPackages, int VulnerablePackages, int CleanPackages, int UnverifiedPackages, int CriticalFindings, int HighFindings);

internal sealed record VulnerablePackageDto(
    string Type, string Name, string Version, SeverityValue HighestSeverity, MatchMethodDto MatchMethod, IReadOnlyList<FindingDto> Findings);

internal sealed record MatchMethodDto(MatchKindValue Kind, string? Cpe, CpeSourceValue? CpeSource, string? Product);

internal sealed record FindingDto(
    string CveId, SeverityValue Severity, decimal BaseScore, string CvssVersion, ScoreSourceValue ScoreSource, string NvdUrl, string Description);

internal sealed record UnverifiedPackageDto(string Type, string Name, string Version, UnverifiedReasonValue Reason, string Detail);

internal sealed record ScanWarningDto(WarningCodeValue Code, string Message, string? Input);

internal sealed record ScanFailureDto(FailureCodeValue Code, string Reason, string? Input);

/// <summary>Emergency Stop's answer (WR-16).</summary>
internal sealed record EmergencyStopDto(StopStatusValue Status, string? ScanId);

/// <summary>The health answer (WR-17).</summary>
internal sealed record SystemHealthDto(HealthStatusValue Status, string Version, IReadOnlyList<HealthCheckDto> Checks);

internal sealed record HealthCheckDto(string Name, bool Passing, string Detail);

/// <summary>The <c>phase</c> event.</summary>
internal sealed record PhaseEventDto(ScanPhaseValue Phase);

/// <summary>The <c>finished</c> event.</summary>
internal sealed record FinishedEventDto(ScanStatusValue Status);

/// <summary>A <c>log</c> event on a scan's stream, which leaves out the scan id.</summary>
internal sealed record ScanLogEntryDto(string Timestamp, LogLevelValue Level, string Category, string Message);

/// <summary>A <c>log</c> event on the Live Log stream, with its scan id or null.</summary>
internal sealed record LogEntryDto(string Timestamp, LogLevelValue Level, string Category, string Message, string? ScanId);
