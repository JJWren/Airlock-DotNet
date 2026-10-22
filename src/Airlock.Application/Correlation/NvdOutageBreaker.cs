namespace Airlock.Application.Correlation;

/// <summary>
/// Counts one scan's failed lookups in a row and opens once they reach the threshold (AR-18). Any answer resets the
/// count while it is closed; once open, it stays open for the rest of the scan.
/// </summary>
public sealed class NvdOutageBreaker
{
    private int failuresInRow;
    private int open;

    public NvdOutageBreaker(int threshold)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(threshold, 1);
        Threshold = threshold;
    }

    public int Threshold { get; }

    public bool IsOpen => Volatile.Read(ref open) == 1;

    public void RecordAnswer()
    {
        if (!IsOpen)
        {
            Interlocked.Exchange(ref failuresInRow, 0);
        }
    }

    /// <returns>True for the one failure that opened the breaker.</returns>
    public bool RecordFailure() =>
        !IsOpen
        && Interlocked.Increment(ref failuresInRow) >= Threshold
        && Interlocked.CompareExchange(ref open, 1, 0) == 0;
}
