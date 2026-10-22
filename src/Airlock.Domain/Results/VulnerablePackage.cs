using Airlock.Domain.Intelligence;
using Airlock.Domain.Targets;

namespace Airlock.Domain.Results;

/// <summary>A package with at least one finding.</summary>
public sealed record VulnerablePackage
{
    public VulnerablePackage(Package package, MatchMethod method, IEnumerable<Finding> findings)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(findings);

        var ordered = FindingOrder.Apply(
            findings.Select(finding => finding ?? throw new ArgumentException("Must not contain null.", nameof(findings))));
        if (ordered.IsEmpty)
        {
            throw new ArgumentException("A vulnerable package needs at least one finding.", nameof(findings));
        }

        Package = package;
        Method = method;
        Findings = ordered;
        HighestSeverity = ordered[0].Severity; // BR-06: findings are ordered highest severity first (BR-07).
    }

    public Package Package { get; }

    public MatchMethod Method { get; }

    public IReadOnlyList<Finding> Findings { get; }

    /// <summary>The package's Risk: the highest severity among its findings (BR-06).</summary>
    public Severity HighestSeverity { get; }
}
