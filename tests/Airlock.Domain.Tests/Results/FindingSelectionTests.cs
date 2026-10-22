using Airlock.Domain.Intelligence;
using Airlock.Domain.Results;

namespace Airlock.Domain.Tests.Results;

// BR-04, BR-05, BR-07
public sealed class FindingSelectionTests
{
    [Fact]
    public void HIGH_and_CRITICAL_CVEs_are_findings_and_lower_ones_are_dropped()
    {
        var selection = Finding.Select(
        [
            A.Cve("CVE-2020-0001", A.Score(9.8m, Severity.Critical)),
            A.Cve("CVE-2020-0002", A.Score(7.5m, Severity.High)),
            A.Cve("CVE-2020-0003", A.Score(6.5m, Severity.Medium)),
            A.Cve("CVE-2020-0004", A.Score(2.0m, Severity.Low)),
        ]);

        Assert.Equal(["CVE-2020-0001", "CVE-2020-0002"], selection.Findings.Select(finding => finding.Cve.Id));
        Assert.Empty(selection.UnscoredCveIds);
    }

    [Fact]
    public void A_CVE_without_any_score_is_listed_not_counted()
    {
        var selection = Finding.Select([A.Cve("CVE-2025-60876")]);

        Assert.Empty(selection.Findings);
        Assert.Equal(["CVE-2025-60876"], selection.UnscoredCveIds);
    }

    [Fact]
    public void The_same_CVE_appears_once()
    {
        var selection = Finding.Select(
        [
            A.Cve("CVE-2020-0001", A.Score(9.8m, Severity.Critical)),
            A.Cve("CVE-2020-0001", A.Score(9.8m, Severity.Critical)),
            A.Cve("CVE-2025-60876"),
            A.Cve("CVE-2025-60876"),
        ]);

        Assert.Single(selection.Findings);
        Assert.Single(selection.UnscoredCveIds);
    }

    [Fact]
    public void Findings_are_ordered_by_severity_then_score_then_id()
    {
        var selection = Finding.Select(
        [
            A.Cve("CVE-2020-0002", A.Score(7.5m, Severity.High)),
            A.Cve("CVE-2020-0003", A.Score(9.1m, Severity.Critical)),
            A.Cve("CVE-2020-0001", A.Score(7.5m, Severity.High)),
            A.Cve("CVE-2020-0004", A.Score(9.8m, Severity.Critical)),
        ]);

        Assert.Equal(
            ["CVE-2020-0004", "CVE-2020-0003", "CVE-2020-0001", "CVE-2020-0002"],
            selection.Findings.Select(finding => finding.Cve.Id));
    }

    // The "Finding?" column of the preferred-score table (real NVD data from S0, plus its illustrative row).
    [Fact]
    public void The_S0_CVEs_are_findings_only_when_their_preferred_score_is_HIGH_or_above()
    {
        const CvssSource Cna = CvssSource.Secondary;
        var selection = Finding.Select(
        [
            A.Cve("CVE-2023-29530", A.Score(6.5m, Severity.Medium), A.Score(7.5m, Severity.High, CvssVersion.V31, Cna)),
            A.Cve("CVE-2018-1000656", A.Score(7.5m, Severity.High, CvssVersion.V30), A.Score(5.0m, Severity.Medium, CvssVersion.V20)),
            A.Cve("CVE-2026-27205", A.Score(2.3m, Severity.Low, CvssVersion.V40, Cna), A.Score(4.3m, Severity.Medium)),
            A.Cve("CVE-2020-1747",
                A.Score(9.8m, Severity.Critical), A.Score(9.8m, Severity.Critical, CvssVersion.V31, Cna),
                A.Score(10.0m, Severity.High, CvssVersion.V20)),
            A.Cve("CVE-2025-60876"),
            A.Cve("CVE-2099-0001", A.Score(7.5m, Severity.High, CvssVersion.V31, Cna), A.Score(9.1m, Severity.Critical, CvssVersion.V31, Cna)),
        ]);

        Assert.Equal(["CVE-2020-1747", "CVE-2099-0001", "CVE-2018-1000656"], selection.Findings.Select(finding => finding.Cve.Id));
        Assert.Equal(["CVE-2025-60876"], selection.UnscoredCveIds);
    }

    [Fact]
    public void A_finding_carries_its_preferred_score_and_severity()
    {
        var finding = A.Finding("CVE-2020-1747", 9.8m, Severity.Critical);

        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Equal(9.8m, finding.Score.BaseScore);
        Assert.Equal(CvssVersion.V31, finding.Score.Version);
    }

    [Fact]
    public void Null_input_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => Finding.Select(null!));
        Assert.Throws<ArgumentNullException>(() => Finding.Select([null!]));
    }
}
