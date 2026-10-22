namespace Airlock.Domain.Scanning;

/// <summary>How many of a scan's packages have been correlated so far.</summary>
public sealed record ScanProgress
{
    public ScanProgress(int correlated, int total)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(correlated);
        ArgumentOutOfRangeException.ThrowIfNegative(total);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(correlated, total);
        Correlated = correlated;
        Total = total;
    }

    public int Correlated { get; }

    public int Total { get; }
}
