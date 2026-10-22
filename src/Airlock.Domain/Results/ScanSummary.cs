using Airlock.Domain.Intelligence;
using Airlock.Domain.Targets;

namespace Airlock.Domain.Results;

/// <summary>The counts reported for a finished scan; built only through <see cref="From"/> (BR-13).</summary>
public sealed record ScanSummary
{
    private ScanSummary(
        int totalPackages, int vulnerablePackages, int cleanPackages, int unverifiedPackages, int criticalFindings, int highFindings)
    {
        TotalPackages = totalPackages;
        VulnerablePackages = vulnerablePackages;
        CleanPackages = cleanPackages;
        UnverifiedPackages = unverifiedPackages;
        CriticalFindings = criticalFindings;
        HighFindings = highFindings;
    }

    public int TotalPackages { get; }

    public int VulnerablePackages { get; }

    public int CleanPackages { get; }

    public int UnverifiedPackages { get; }

    /// <summary>Findings (CVEs) rated CRITICAL — not packages.</summary>
    public int CriticalFindings { get; }

    /// <summary>Findings (CVEs) rated HIGH — not packages.</summary>
    public int HighFindings { get; }

    // BR-13: every package is clean, vulnerable or unverified — exactly one of them.
    public static ScanSummary From(
        int totalPackages, IEnumerable<VulnerablePackage> vulnerable, IEnumerable<UnverifiedPackage> unverified)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalPackages);
        ArgumentNullException.ThrowIfNull(vulnerable);
        ArgumentNullException.ThrowIfNull(unverified);

        var vulnerableList = vulnerable.ToArray();
        var unverifiedList = unverified.ToArray();
        var seen = new HashSet<Package>();
        foreach (var package in vulnerableList)
        {
            ArgumentNullException.ThrowIfNull(package, nameof(vulnerable));
            if (!seen.Add(package.Package))
            {
                throw new ArgumentException($"The package {package.Package} is listed more than once.", nameof(vulnerable));
            }
        }

        foreach (var package in unverifiedList)
        {
            ArgumentNullException.ThrowIfNull(package, nameof(unverified));
            if (!seen.Add(package.Package))
            {
                throw new ArgumentException($"The package {package.Package} is listed more than once.", nameof(unverified));
            }
        }

        var clean = totalPackages - seen.Count;
        if (clean < 0)
        {
            throw new ArgumentException(
                "There are more vulnerable and unverified packages than packages in total.", nameof(totalPackages));
        }

        return new ScanSummary(
            totalPackages,
            vulnerableList.Length,
            clean,
            unverifiedList.Length,
            vulnerableList.Sum(package => package.Findings.Count(finding => finding.Severity == Severity.Critical)),
            vulnerableList.Sum(package => package.Findings.Count(finding => finding.Severity == Severity.High)));
    }
}
