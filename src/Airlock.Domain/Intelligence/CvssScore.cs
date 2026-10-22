namespace Airlock.Domain.Intelligence;

public enum CvssVersion
{
    V20,
    V30,
    V31,
    V40,
}

/// <summary>Who published a score: NVD itself (Primary) or a CNA or other provider (Secondary).</summary>
public enum CvssSource
{
    Primary,
    Secondary,
}

/// <summary>One CVSS base score as NVD published it.</summary>
public sealed record CvssScore
{
    private static readonly CvssVersion[] Precedence =
        [CvssVersion.V31, CvssVersion.V30, CvssVersion.V40, CvssVersion.V20];

    public CvssScore(decimal baseScore, Severity severity, CvssVersion version, CvssSource source)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseScore);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(baseScore, 10.0m);
        if (decimal.Round(baseScore, 1) != baseScore)
        {
            throw new ArgumentException("Must have at most one decimal place.", nameof(baseScore));
        }

        if (!Enum.IsDefined(severity))
        {
            throw new ArgumentOutOfRangeException(nameof(severity));
        }

        if (!Enum.IsDefined(version))
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(nameof(source));
        }

        BaseScore = baseScore;
        Severity = severity;
        Version = version;
        Source = source;
    }

    public decimal BaseScore { get; }

    public Severity Severity { get; }

    public CvssVersion Version { get; }

    public CvssSource Source { get; }

    /// <summary>The label shown next to the score, for example "CVSS v3.1".</summary>
    public string VersionLabel => Version switch
    {
        CvssVersion.V20 => "CVSS v2.0",
        CvssVersion.V30 => "CVSS v3.0",
        CvssVersion.V31 => "CVSS v3.1",
        _ => "CVSS v4.0",
    };

    // BR-01: v3.1, else v3.0, else v4.0, else v2.0.
    // BR-02: within that version NVD's own score; otherwise the highest CNA score, the first listed on ties.
    public static CvssScore? SelectPreferred(IEnumerable<CvssScore> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);
        var all = scores.ToArray();
        foreach (var version in Precedence)
        {
            CvssScore? highestSecondary = null;
            foreach (var score in all)
            {
                ArgumentNullException.ThrowIfNull(score, nameof(scores));
                if (score.Version != version)
                {
                    continue;
                }

                if (score.Source == CvssSource.Primary)
                {
                    return score;
                }

                if (highestSecondary is null || score.BaseScore > highestSecondary.BaseScore)
                {
                    highestSecondary = score;
                }
            }

            if (highestSecondary is not null)
            {
                return highestSecondary;
            }
        }

        return null;
    }
}
