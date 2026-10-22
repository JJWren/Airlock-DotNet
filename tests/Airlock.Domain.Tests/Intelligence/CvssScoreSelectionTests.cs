using Airlock.Domain.Intelligence;

namespace Airlock.Domain.Tests.Intelligence;

// BR-01, BR-02, BR-03 — rows of the preferred-score table (real NVD data from S0).
public sealed class CvssScoreSelectionTests
{
    public static TheoryData<CvssVersion, decimal, Severity> ScoreBands => new()
    {
        { CvssVersion.V31, 0.0m, Severity.None },
        { CvssVersion.V31, 0.1m, Severity.Low },
        { CvssVersion.V31, 3.9m, Severity.Low },
        { CvssVersion.V31, 4.0m, Severity.Medium },
        { CvssVersion.V31, 6.9m, Severity.Medium },
        { CvssVersion.V31, 7.0m, Severity.High },
        { CvssVersion.V31, 8.9m, Severity.High },
        { CvssVersion.V31, 9.0m, Severity.Critical },
        { CvssVersion.V31, 10.0m, Severity.Critical },
        { CvssVersion.V30, 7.5m, Severity.High },
        { CvssVersion.V40, 9.8m, Severity.Critical },
        { CvssVersion.V20, 0.0m, Severity.Low },
        { CvssVersion.V20, 3.9m, Severity.Low },
        { CvssVersion.V20, 4.0m, Severity.Medium },
        { CvssVersion.V20, 6.9m, Severity.Medium },
        { CvssVersion.V20, 7.0m, Severity.High },
        { CvssVersion.V20, 9.3m, Severity.High },
        { CvssVersion.V20, 10.0m, Severity.High },
    };

    [Fact]
    public void Cve_2023_29530_uses_NVDs_own_medium_score_over_the_CNAs_high_score()
    {
        var nvd = A.Score(6.5m, Severity.Medium, CvssVersion.V31, CvssSource.Primary);
        var cna = A.Score(7.5m, Severity.High, CvssVersion.V31, CvssSource.Secondary);

        Assert.Same(nvd, CvssScore.SelectPreferred([cna, nvd]));
    }

    [Fact]
    public void Cve_2018_1000656_falls_back_to_v30()
    {
        var v30 = A.Score(7.5m, Severity.High, CvssVersion.V30);
        var v20 = A.Score(5.0m, Severity.Medium, CvssVersion.V20);

        Assert.Same(v30, CvssScore.SelectPreferred([v20, v30]));
    }

    [Fact]
    public void Cve_2026_27205_prefers_v31_over_v40()
    {
        var v40 = A.Score(2.3m, Severity.Low, CvssVersion.V40, CvssSource.Secondary);
        var v31 = A.Score(4.3m, Severity.Medium, CvssVersion.V31);

        Assert.Same(v31, CvssScore.SelectPreferred([v40, v31]));
    }

    [Fact]
    public void Cve_2020_1747_takes_NVDs_v31_score()
    {
        var cna = A.Score(9.8m, Severity.Critical, CvssVersion.V31, CvssSource.Secondary);
        var nvd = A.Score(9.8m, Severity.Critical, CvssVersion.V31, CvssSource.Primary);
        var v20 = A.Score(10.0m, Severity.High, CvssVersion.V20);

        Assert.Same(nvd, CvssScore.SelectPreferred([cna, nvd, v20]));
    }

    [Fact]
    public void Several_CNA_scores_without_an_NVD_score_use_the_highest()
    {
        var lower = A.Score(7.5m, Severity.High, CvssVersion.V31, CvssSource.Secondary);
        var higher = A.Score(9.1m, Severity.Critical, CvssVersion.V31, CvssSource.Secondary);

        Assert.Same(higher, CvssScore.SelectPreferred([lower, higher]));
    }

    [Fact]
    public void Equal_CNA_scores_resolve_to_the_first_listed()
    {
        var first = A.Score(7.5m, Severity.High, CvssVersion.V31, CvssSource.Secondary);
        var second = A.Score(7.5m, Severity.High, CvssVersion.V31, CvssSource.Secondary);

        Assert.Same(first, CvssScore.SelectPreferred([first, second]));
    }

    [Fact]
    public void A_v20_only_CVE_uses_its_v20_score()
    {
        var v20 = A.Score(9.3m, Severity.High, CvssVersion.V20);

        Assert.Same(v20, CvssScore.SelectPreferred([v20]));
    }

    [Fact]
    public void No_scores_means_no_preferred_score()
    {
        Assert.Null(CvssScore.SelectPreferred([]));
    }

    [Fact]
    public void Selecting_from_null_throws()
    {
        Assert.Throws<ArgumentNullException>(() => CvssScore.SelectPreferred(null!));
        Assert.Throws<ArgumentNullException>(() => CvssScore.SelectPreferred([null!]));
    }

    [Theory]
    [InlineData(CvssVersion.V20, "CVSS v2.0")]
    [InlineData(CvssVersion.V30, "CVSS v3.0")]
    [InlineData(CvssVersion.V31, "CVSS v3.1")]
    [InlineData(CvssVersion.V40, "CVSS v4.0")]
    public void The_version_label_names_the_version(CvssVersion version, string label)
    {
        Assert.Equal(label, A.Score(7.5m, Severity.High, version).VersionLabel);
    }

    [Theory]
    [MemberData(nameof(ScoreBands))]
    public void A_score_without_a_label_gets_the_severity_of_its_band(CvssVersion version, decimal score, Severity expected)
    {
        Assert.Equal(expected, SeverityScale.FromScore(version, score));
    }

    [Theory]
    [InlineData("CRITICAL", Severity.Critical)]
    [InlineData("high", Severity.High)]
    [InlineData(" Medium ", Severity.Medium)]
    [InlineData("LOW", Severity.Low)]
    [InlineData("NONE", Severity.None)]
    public void NVD_labels_map_to_severities(string label, Severity expected)
    {
        Assert.Equal(expected, SeverityScale.FromLabel(label));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("UNKNOWN")]
    public void Missing_or_unknown_labels_map_to_nothing(string? label)
    {
        Assert.Null(SeverityScale.FromLabel(label));
    }

    [Fact]
    public void Out_of_range_scores_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SeverityScale.FromScore(CvssVersion.V31, -0.1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => SeverityScale.FromScore(CvssVersion.V31, 10.1m));
        Assert.Throws<ArgumentOutOfRangeException>(() => SeverityScale.FromScore((CvssVersion)42, 5.0m));
        Assert.Throws<ArgumentOutOfRangeException>(() => A.Score(-0.1m, Severity.Low));
        Assert.Throws<ArgumentOutOfRangeException>(() => A.Score(10.1m, Severity.Critical));
        Assert.Throws<ArgumentException>(() => A.Score(7.55m, Severity.High));
        Assert.Throws<ArgumentOutOfRangeException>(() => A.Score(7.5m, (Severity)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => A.Score(7.5m, Severity.High, (CvssVersion)42));
        Assert.Throws<ArgumentOutOfRangeException>(() => A.Score(7.5m, Severity.High, CvssVersion.V31, (CvssSource)42));
    }

    [Theory]
    [InlineData(Severity.Critical, true)]
    [InlineData(Severity.High, true)]
    [InlineData(Severity.Medium, false)]
    [InlineData(Severity.Low, false)]
    [InlineData(Severity.None, false)]
    public void The_threshold_admits_HIGH_and_CRITICAL(Severity severity, bool admitted)
    {
        Assert.Equal(Severity.High, SeverityThreshold.Minimum);
        Assert.Equal(admitted, SeverityThreshold.Admits(severity));
    }
}
