using System.Collections.Immutable;
using Airlock.Domain.Intelligence;

namespace Airlock.Domain.Results;

/// <summary>One CVE, at or above the severity threshold, matched to one package.</summary>
public sealed record Finding
{
    private Finding(Cve cve, CvssScore score)
    {
        Cve = cve;
        Score = score;
    }

    public Cve Cve { get; }

    /// <summary>The CVE's preferred score (BR-01, BR-02).</summary>
    public CvssScore Score { get; }

    public Severity Severity => Score.Severity;

    // BR-04: only CVEs at or above the threshold; BR-05: unscored CVEs are listed apart; BR-07: distinct and ordered.
    public static FindingSelection Select(IEnumerable<Cve> matched)
    {
        ArgumentNullException.ThrowIfNull(matched);
        var findings = new List<Finding>();
        var findingIds = new HashSet<string>(StringComparer.Ordinal);
        var unscored = new List<string>();
        var unscoredIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cve in matched)
        {
            ArgumentNullException.ThrowIfNull(cve, nameof(matched));
            if (cve.PreferredScore is not { } score)
            {
                if (unscoredIds.Add(cve.Id))
                {
                    unscored.Add(cve.Id);
                }
            }
            else if (SeverityThreshold.Admits(score.Severity) && findingIds.Add(cve.Id))
            {
                findings.Add(new Finding(cve, score));
            }
        }

        return new FindingSelection(FindingOrder.Apply(findings), [.. unscored]);
    }
}

/// <summary>The findings selected from a package's matched CVEs, and the CVEs NVD has not scored yet.</summary>
public sealed record FindingSelection
{
    internal FindingSelection(ImmutableArray<Finding> findings, ImmutableArray<string> unscoredCveIds)
    {
        Findings = findings;
        UnscoredCveIds = unscoredCveIds;
    }

    public IReadOnlyList<Finding> Findings { get; }

    public IReadOnlyList<string> UnscoredCveIds { get; }
}

internal static class FindingOrder
{
    // BR-07: distinct by CVE id; highest severity first, then highest score, then CVE id.
    public static ImmutableArray<Finding> Apply(IEnumerable<Finding> findings) =>
        [
            .. findings
                .DistinctBy(finding => finding.Cve.Id, StringComparer.Ordinal)
                .OrderByDescending(finding => finding.Severity)
                .ThenByDescending(finding => finding.Score.BaseScore)
                .ThenBy(finding => finding.Cve.Id, StringComparer.Ordinal),
        ];
}
