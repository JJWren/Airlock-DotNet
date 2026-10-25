using System.Text.Json.Serialization;

namespace Airlock.Web.Api.Contracts;

// WR-02, Q-U4-1: every value in capitals with underscores. The API names its own values rather than applying a naming
// policy to the Domain's enumerations, so renaming a Domain value cannot change the contract.

[JsonConverter(typeof(JsonStringEnumConverter<ScanStatusValue>))]
internal enum ScanStatusValue
{
    [JsonStringEnumMemberName("RUNNING")] Running,
    [JsonStringEnumMemberName("COMPLETED")] Completed,
    [JsonStringEnumMemberName("INCOMPLETE")] Incomplete,
    [JsonStringEnumMemberName("FAILED")] Failed,
    [JsonStringEnumMemberName("CANCELLED")] Cancelled,
}

[JsonConverter(typeof(JsonStringEnumConverter<ScanPhaseValue>))]
internal enum ScanPhaseValue
{
    [JsonStringEnumMemberName("SBOM_GENERATION")] SbomGeneration,
    [JsonStringEnumMemberName("CORRELATION")] Correlation,
    [JsonStringEnumMemberName("SUMMARY")] Summary,
}

[JsonConverter(typeof(JsonStringEnumConverter<TargetKindValue>))]
internal enum TargetKindValue
{
    [JsonStringEnumMemberName("DIRECTORY")] Directory,
    [JsonStringEnumMemberName("ARTIFACTS")] Artifacts,
}

[JsonConverter(typeof(JsonStringEnumConverter<ArtifactKindValue>))]
internal enum ArtifactKindValue
{
    [JsonStringEnumMemberName("MANIFEST")] Manifest,
    [JsonStringEnumMemberName("OTHER")] Other,
}

[JsonConverter(typeof(JsonStringEnumConverter<SeverityValue>))]
internal enum SeverityValue
{
    [JsonStringEnumMemberName("HIGH")] High,
    [JsonStringEnumMemberName("CRITICAL")] Critical,
}

[JsonConverter(typeof(JsonStringEnumConverter<MatchKindValue>))]
internal enum MatchKindValue
{
    [JsonStringEnumMemberName("CPE")] Cpe,
    [JsonStringEnumMemberName("PRODUCT")] Product,
}

[JsonConverter(typeof(JsonStringEnumConverter<CpeSourceValue>))]
internal enum CpeSourceValue
{
    [JsonStringEnumMemberName("DICTIONARY")] Dictionary,
    [JsonStringEnumMemberName("GENERATED")] Generated,
}

[JsonConverter(typeof(JsonStringEnumConverter<ScoreSourceValue>))]
internal enum ScoreSourceValue
{
    [JsonStringEnumMemberName("PRIMARY")] Primary,
    [JsonStringEnumMemberName("SECONDARY")] Secondary,
}

[JsonConverter(typeof(JsonStringEnumConverter<UnverifiedReasonValue>))]
internal enum UnverifiedReasonValue
{
    [JsonStringEnumMemberName("LOOKUP_FAILED")] LookupFailed,
    [JsonStringEnumMemberName("VERSION_UNKNOWN")] VersionUnknown,
}

[JsonConverter(typeof(JsonStringEnumConverter<WarningCodeValue>))]
internal enum WarningCodeValue
{
    [JsonStringEnumMemberName("NO_PACKAGES_IDENTIFIED")] NoPackagesIdentified,
    [JsonStringEnumMemberName("DISTRIBUTION_BACKPORTS_NOT_CONSIDERED")] DistributionBackportsNotConsidered,
    [JsonStringEnumMemberName("CVES_WITHOUT_SEVERITY")] CvesWithoutSeverity,
    [JsonStringEnumMemberName("NOT_PRODUCT_MATCHED")] NotProductMatched,
}

[JsonConverter(typeof(JsonStringEnumConverter<FailureCodeValue>))]
internal enum FailureCodeValue
{
    [JsonStringEnumMemberName("SYFT_FAILED")] SyftFailed,
    [JsonStringEnumMemberName("SYFT_TIMED_OUT")] SyftTimedOut,
    [JsonStringEnumMemberName("SYFT_OUTPUT_UNREADABLE")] SyftOutputUnreadable,
    [JsonStringEnumMemberName("SCAN_TIMED_OUT")] ScanTimedOut,
    [JsonStringEnumMemberName("UNEXPECTED")] Unexpected,
}

[JsonConverter(typeof(JsonStringEnumConverter<HealthStatusValue>))]
internal enum HealthStatusValue
{
    [JsonStringEnumMemberName("ONLINE")] Online,
    [JsonStringEnumMemberName("DEGRADED")] Degraded,
}

[JsonConverter(typeof(JsonStringEnumConverter<StopStatusValue>))]
internal enum StopStatusValue
{
    [JsonStringEnumMemberName("TERMINATED")] Terminated,
    [JsonStringEnumMemberName("IDLE")] Idle,
}

/// <summary>The names <c>LOG_LEVEL</c> accepts (U3 IR-23).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LogLevelValue>))]
internal enum LogLevelValue
{
    [JsonStringEnumMemberName("TRACE")] Trace,
    [JsonStringEnumMemberName("DEBUG")] Debug,
    [JsonStringEnumMemberName("INFO")] Info,
    [JsonStringEnumMemberName("WARNING")] Warning,
    [JsonStringEnumMemberName("ERROR")] Error,
    [JsonStringEnumMemberName("CRITICAL")] Critical,
}
