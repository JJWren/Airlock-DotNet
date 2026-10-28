using Airlock.Domain.Intelligence;
using Airlock.Domain.Results;
using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;

namespace Airlock.Web.State;

/// <summary>
/// One row of the inventory grid: a vulnerable or an unverified package, in the scan's own order (UR-27). A row's
/// identity is its package's — type, name and version (UR-29).
/// </summary>
internal abstract record InventoryRow(Package Package)
{
    /// <summary>The highest severity, or UNVERIFIED (defect 2).</summary>
    public abstract string Risk { get; }

    /// <summary>The badge's look: critical, high or unverified.</summary>
    public abstract string RiskClass { get; }

    public abstract string Method { get; }

    public abstract string Findings { get; }

    /// <summary>UR-28: the name, or one of the CVE ids, contains the text, ignoring case.</summary>
    public abstract bool Matches(string text);

    public static IReadOnlyList<InventoryRow> For(Scan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        return
        [
            .. scan.VulnerablePackages.Select(package => new VulnerableRow(package)),
            .. scan.UnverifiedPackages.Select(package => new UnverifiedRow(package)),
        ];
    }

    /// <summary>UR-28: the rows the filter keeps; an empty filter keeps every row.</summary>
    public static IReadOnlyList<InventoryRow> Filter(IReadOnlyList<InventoryRow> rows, string? text)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? rows : [.. rows.Where(row => row.Matches(trimmed))];
    }

    protected bool NameMatches(string text) => Package.Name.Contains(text, StringComparison.OrdinalIgnoreCase);
}

internal sealed record VulnerableRow(VulnerablePackage Result) : InventoryRow(Result.Package)
{
    public override string Risk => UiFormat.Severity(Result.HighestSeverity);

    public override string RiskClass => Result.HighestSeverity == Severity.Critical ? "critical" : "high";

    public override string Method => UiFormat.Method(Result.Method);

    public override string Findings => UiFormat.Count(Result.Findings.Count);

    public override bool Matches(string text) =>
        NameMatches(text) || Result.Findings.Any(finding => finding.Cve.Id.Contains(text, StringComparison.OrdinalIgnoreCase));
}

internal sealed record UnverifiedRow(UnverifiedPackage Result) : InventoryRow(Result.Package)
{
    public override string Risk => "UNVERIFIED";

    public override string RiskClass => "unverified";

    public override string Method => string.Empty;

    public override string Findings => string.Empty;

    public override bool Matches(string text) => NameMatches(text);
}