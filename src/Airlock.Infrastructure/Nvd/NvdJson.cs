using System.Text.Json.Serialization;

namespace Airlock.Infrastructure.Nvd;

// The fields of the NVD CVE API 2.0 answer that Airlock reads; every other field is skipped (domain-entities.md).

internal sealed record NvdCveResponse(
    int ResultsPerPage, int StartIndex, int TotalResults, IReadOnlyList<NvdVulnerability>? Vulnerabilities);

internal sealed record NvdVulnerability(NvdCve? Cve);

internal sealed record NvdCve(
    string? Id,
    string? VulnStatus,
    IReadOnlyList<NvdDescription>? Descriptions,
    NvdMetrics? Metrics,
    IReadOnlyList<NvdConfiguration>? Configurations);

internal sealed record NvdDescription(string? Lang, string? Value);

internal sealed record NvdMetrics(
    IReadOnlyList<NvdCvssMetric>? CvssMetricV31,
    IReadOnlyList<NvdCvssMetric>? CvssMetricV30,
    IReadOnlyList<NvdCvssMetric>? CvssMetricV40,
    IReadOnlyList<NvdCvssMetric>? CvssMetricV2);

internal sealed record NvdCvssMetric(string? Source, string? Type, NvdCvssData? CvssData, string? BaseSeverity);

internal sealed record NvdCvssData(decimal? BaseScore, string? BaseSeverity);

internal sealed record NvdConfiguration(IReadOnlyList<NvdNode>? Nodes);

internal sealed record NvdNode(IReadOnlyList<NvdCpeMatch>? CpeMatch);

internal sealed record NvdCpeMatch(bool Vulnerable, string? Criteria);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(NvdCveResponse))]
internal sealed partial class NvdJsonContext : JsonSerializerContext;
