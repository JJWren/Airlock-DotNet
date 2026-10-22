namespace Airlock.Domain.Intelligence;

public enum Severity
{
    None,
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>Severity labels and the CVSS score bands behind them (BR-03).</summary>
public static class SeverityScale
{
    // BR-03: the severity a base score falls in, used when NVD gives no label.
    public static Severity FromScore(CvssVersion version, decimal baseScore)
    {
        if (!Enum.IsDefined(version))
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(baseScore);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(baseScore, 10.0m);

        if (version == CvssVersion.V20)
        {
            return baseScore < 4.0m ? Severity.Low : baseScore < 7.0m ? Severity.Medium : Severity.High;
        }

        return baseScore switch
        {
            0.0m => Severity.None,
            < 4.0m => Severity.Low,
            < 7.0m => Severity.Medium,
            < 9.0m => Severity.High,
            _ => Severity.Critical,
        };
    }

    /// <summary>NVD's severity label, or null when the label is missing or not recognized.</summary>
    public static Severity? FromLabel(string? label) => label?.Trim().ToUpperInvariant() switch
    {
        "NONE" => Severity.None,
        "LOW" => Severity.Low,
        "MEDIUM" => Severity.Medium,
        "HIGH" => Severity.High,
        "CRITICAL" => Severity.Critical,
        _ => null,
    };
}

/// <summary>The lowest severity Airlock reports (GLOSSARY: Severity Threshold).</summary>
public static class SeverityThreshold
{
    public static Severity Minimum => Severity.High;

    // BR-04
    public static bool Admits(Severity severity) => severity >= Minimum;
}
