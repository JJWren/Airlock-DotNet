using System.Collections.Immutable;
using System.Globalization;
using Airlock.Domain.Results;
using Airlock.Domain.Targets;
using Airlock.Domain.Text;

namespace Airlock.Domain.Scanning;

/// <summary>
/// One run of Airlock against a scan target. Immutable: every transition returns a new instance, and transitions the
/// state machine does not allow throw (BR-14).
/// </summary>
public sealed record Scan
{
    private Scan(ScanId id, ScanTarget target, DateTimeOffset startedAt)
    {
        Id = id;
        Target = target;
        StartedAt = startedAt;
    }

    public ScanId Id { get; }

    public ScanTarget Target { get; }

    public ScanStatus Status { get; private init; } = ScanStatus.Running;

    public ScanPhase Phase { get; private init; } = ScanPhase.SbomGeneration;

    public ScanProgress? Progress { get; private init; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? FinishedAt { get; private init; }

    /// <summary>Set by <see cref="Finish"/> only.</summary>
    public ScanSummary? Summary { get; private init; }

    /// <summary>Highest severity first, then by package name.</summary>
    public IReadOnlyList<VulnerablePackage> VulnerablePackages { get; private init; } = ImmutableArray<VulnerablePackage>.Empty;

    /// <summary>Ordered by package name.</summary>
    public IReadOnlyList<UnverifiedPackage> UnverifiedPackages { get; private init; } = ImmutableArray<UnverifiedPackage>.Empty;

    public IReadOnlyList<ScanWarning> Warnings { get; private init; } = ImmutableArray<ScanWarning>.Empty;

    /// <summary>Present only when the scan <see cref="ScanStatus.Failed"/>.</summary>
    public ScanFailure? Failure { get; private init; }

    /// <summary>Present only when the scan was <see cref="ScanStatus.Cancelled"/>.</summary>
    public string? CancellationReason { get; private init; }

    public bool IsFinished => Status != ScanStatus.Running;

    // BR-15
    public TimeSpan? Duration => FinishedAt - StartedAt;

    public static Scan Start(ScanId id, ScanTarget target, DateTimeOffset now)
    {
        if (id == default)
        {
            throw new ArgumentException("Must not be empty.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(target);
        return new Scan(id, target, now.ToUniversalTime());
    }

    public Scan EnterPhase(ScanPhase phase)
    {
        EnsureRunning("move");
        if (!Enum.IsDefined(phase))
        {
            throw new ArgumentOutOfRangeException(nameof(phase));
        }

        if (phase <= Phase)
        {
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Cannot move a scan from phase {Phase} to phase {phase}."));
        }

        return this with { Phase = phase };
    }

    public Scan RecordProgress(int correlated, int total)
    {
        EnsureRunning("record progress on");
        if (Phase != ScanPhase.Correlation)
        {
            throw Illegal("record progress on");
        }

        var progress = new ScanProgress(correlated, total);
        if (Progress is { } current && (progress.Total != current.Total || progress.Correlated < current.Correlated))
        {
            throw new ArgumentException("Progress must keep its total and never go backwards.", nameof(correlated));
        }

        return this with { Progress = progress };
    }

    public Scan AddWarning(ScanWarning warning)
    {
        EnsureRunning("add a warning to");
        ArgumentNullException.ThrowIfNull(warning);
        return this with { Warnings = ImmutableArray.CreateRange(Warnings).Add(warning) };
    }

    public Scan Finish(
        int totalPackages, IEnumerable<VulnerablePackage> vulnerable, IEnumerable<UnverifiedPackage> unverified, DateTimeOffset now)
    {
        EnsureRunning("finish");
        ArgumentNullException.ThrowIfNull(vulnerable);
        ArgumentNullException.ThrowIfNull(unverified);
        var finishedAt = EnsureNotBeforeStart(now);

        ImmutableArray<VulnerablePackage> vulnerableList =
        [
            .. vulnerable
                .Select(package => package ?? throw new ArgumentException("Must not contain null.", nameof(vulnerable)))
                .OrderByDescending(package => package.HighestSeverity)
                .ThenBy(package => package.Package.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(package => package.Package.Version, StringComparer.Ordinal),
        ];
        ImmutableArray<UnverifiedPackage> unverifiedList =
        [
            .. unverified
                .Select(package => package ?? throw new ArgumentException("Must not contain null.", nameof(unverified)))
                .OrderBy(package => package.Package.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(package => package.Package.Version, StringComparer.Ordinal),
        ];
        var summary = ScanSummary.From(totalPackages, vulnerableList, unverifiedList);

        return this with
        {
            Status = unverifiedList.IsEmpty ? ScanStatus.Completed : ScanStatus.Incomplete,
            Summary = summary,
            VulnerablePackages = vulnerableList,
            UnverifiedPackages = unverifiedList,
            FinishedAt = finishedAt,
        };
    }

    public Scan Fail(ScanFailure failure, DateTimeOffset now)
    {
        EnsureRunning("fail");
        ArgumentNullException.ThrowIfNull(failure);
        return this with { Status = ScanStatus.Failed, Failure = failure, FinishedAt = EnsureNotBeforeStart(now) };
    }

    public Scan Cancel(string reason, DateTimeOffset now)
    {
        EnsureRunning("cancel");
        var checkedReason = DomainText.Require(reason, DomainLimits.Message, nameof(reason));
        return this with { Status = ScanStatus.Cancelled, CancellationReason = checkedReason, FinishedAt = EnsureNotBeforeStart(now) };
    }

    private void EnsureRunning(string action)
    {
        if (Status != ScanStatus.Running)
        {
            throw Illegal(action);
        }
    }

    private InvalidOperationException Illegal(string action) =>
        new(string.Create(CultureInfo.InvariantCulture, $"Cannot {action} a scan that is {Status} in phase {Phase}."));

    private DateTimeOffset EnsureNotBeforeStart(DateTimeOffset now)
    {
        var utc = now.ToUniversalTime();
        if (utc < StartedAt)
        {
            throw new ArgumentOutOfRangeException(nameof(now), "Must not be earlier than the start of the scan.");
        }

        return utc;
    }
}
