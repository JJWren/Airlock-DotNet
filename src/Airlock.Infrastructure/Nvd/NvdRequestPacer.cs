using System.Threading.RateLimiting;

namespace Airlock.Infrastructure.Nvd;

/// <summary>
/// Starts NVD requests at least <c>Window / RequestsPerWindow</c> apart, application-wide (U3-NFR-03): no 30-second span
/// holds more than 50 requests. The built-in sliding window hands a segment's permits back less than a full window
/// after the last of them was taken, and NVD counts a rolling 30 seconds.
/// </summary>
internal sealed class NvdRequestPacer(TimeSpan spacing, TimeProvider timeProvider) : RateLimiter
{
    private const long NoStartYet = -1;

    private readonly SemaphoreSlim turn = new(1, 1);
    private long lastStart = NoStartYet;
    private int waiting;
    private long granted;

    public override TimeSpan? IdleDuration =>
        Volatile.Read(ref waiting) == 0 && Volatile.Read(ref lastStart) is var last and not NoStartYet
            ? timeProvider.GetElapsedTime(last)
            : null;

    public override RateLimiterStatistics? GetStatistics() => new()
    {
        CurrentAvailablePermits = turn.CurrentCount,
        CurrentQueuedCount = Volatile.Read(ref waiting),
        TotalSuccessfulLeases = Interlocked.Read(ref granted),
        TotalFailedLeases = 0,
    };

    /// <summary>Never grants without waiting: every request takes its turn through <c>AcquireAsync</c>.</summary>
    protected override RateLimitLease AttemptAcquireCore(int permitCount) => Lease.Refused;

    // U3-NFR-03: waiters queue in arrival order; one cancelled in the queue or while it waits leaves without a start.
    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(permitCount, 1);
        if (permitCount == 0)
        {
            return Lease.Acquired;
        }

        Interlocked.Increment(ref waiting);
        try
        {
            await turn.WaitAsync(cancellationToken);
            try
            {
                // A timer may end up to a millisecond early, so the wait is checked again until the spacing has passed.
                var last = Volatile.Read(ref lastStart);
                while (last != NoStartYet && spacing - timeProvider.GetElapsedTime(last) is var wait && wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, timeProvider, cancellationToken);
                }

                Volatile.Write(ref lastStart, timeProvider.GetTimestamp());
                Interlocked.Increment(ref granted);
                return Lease.Acquired;
            }
            finally
            {
                turn.Release();
            }
        }
        finally
        {
            Interlocked.Decrement(ref waiting);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            turn.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class Lease(bool acquired) : RateLimitLease
    {
        public static readonly Lease Acquired = new(true);

        public static readonly Lease Refused = new(false);

        public override bool IsAcquired => acquired;

        public override IEnumerable<string> MetadataNames => [];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
    }
}
