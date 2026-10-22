using Airlock.Application.Correlation;
using Airlock.Application.Ports;
using Airlock.Domain.Scanning;

namespace Airlock.Application.Scanning;

/// <summary>Why a running scan was stopped. The first cause recorded wins.</summary>
internal enum StopCause
{
    None,
    CancelledByRequest,
    EmergencyStop,
    ServerShutdown,
    TimeLimit,
}

/// <summary>
/// The state of one running scan: its latest version (written only by the pipeline), its staging area, its event
/// hub and outage breaker, and one cancellation source fed by stop requests and the scan time limit (U2-NFR-04).
/// </summary>
internal sealed class ScanRun : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private readonly CancellationTokenSource limit;
    private readonly CancellationTokenSource linked;
    private readonly CancellationTokenRegistration limitRegistration;
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int cause;

    public ScanRun(
        Scan scan, IStagingArea? staging, ScanEventHub hub, NvdOutageBreaker breaker, TimeSpan timeLimit, TimeProvider timeProvider)
    {
        Current = scan;
        Staging = staging;
        Hub = hub;
        Breaker = breaker;
        limit = new CancellationTokenSource(timeLimit, timeProvider);
        linked = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, limit.Token);
        limitRegistration = limit.Token.Register(() => Record(StopCause.TimeLimit));
    }

    public ScanId Id => Current.Id;

    public Scan Current { get; set; }

    public IStagingArea? Staging { get; }

    public ScanEventHub Hub { get; }

    public NvdOutageBreaker Breaker { get; }

    public CancellationToken Token => linked.Token;

    public StopCause Cause => (StopCause)Volatile.Read(ref cause);

    /// <summary>Completes when the run has ended and its slot has been released.</summary>
    public Task Completion => completion.Task;

    public void Stop(StopCause stopCause)
    {
        Record(stopCause);
        try
        {
            stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run has already ended; there is nothing left to stop.
        }
    }

    public void MarkCompleted() => completion.TrySetResult();

    public void Dispose()
    {
        limitRegistration.Dispose();
        linked.Dispose();
        limit.Dispose();
        stop.Dispose();
    }

    private void Record(StopCause stopCause) => Interlocked.CompareExchange(ref cause, (int)stopCause, (int)StopCause.None);
}
