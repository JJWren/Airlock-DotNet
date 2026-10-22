using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Airlock.Application.Correlation;
using Airlock.Application.Events;
using Airlock.Application.Logging;
using Airlock.Application.Options;
using Airlock.Application.Ports;
using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Airlock.Application.Scanning;

/// <summary>
/// Owns the server's single active slot (AR-02) and the running scan. The slot changes inside one small lock that is
/// never held while awaiting or calling a port (U2-NFR-02); watchers hear of every change in order (AR-26).
/// </summary>
public sealed class ScanCoordinator(
    ScanPipeline pipeline,
    IScanRegistry registry,
    ScanLogRouter router,
    IOptions<AirlockOptions> options,
    TimeProvider timeProvider,
    ILogger<ScanCoordinator> logger)
{
    private readonly AirlockOptions settings = options.Value;
    private readonly Lock gate = new();
    private readonly List<Channel<ActiveScanState>> watchers = [];
    private ReservationTicket? reservation;
    private bool receivingUpload;
    private ScanRun? running;

    public ActiveScanState Active
    {
        get
        {
            lock (gate)
            {
                return StateLocked();
            }
        }
    }

    public async IAsyncEnumerable<ActiveScanState> WatchActiveAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<ActiveScanState>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        lock (gate)
        {
            watchers.Add(channel);
            channel.Writer.TryWrite(StateLocked());
        }

        try
        {
            await foreach (var state in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return state;
            }
        }
        finally
        {
            lock (gate)
            {
                watchers.Remove(channel);
            }
        }
    }

    // AR-02: the ticket belongs to its reservation, which disposes it; the coordinator only holds it.
    internal bool TryReserve(ReservationTicket ticket)
    {
        lock (gate)
        {
            if (reservation is not null || running is not null)
            {
                return false;
            }

            reservation = ticket;
            receivingUpload = false;
            NotifyLocked();
            return true;
        }
    }

    internal void MarkReceivingUpload(ReservationTicket ticket)
    {
        lock (gate)
        {
            if (reservation == ticket && !receivingUpload)
            {
                receivingUpload = true;
                NotifyLocked();
            }
        }
    }

    // AR-03
    internal void Release(ReservationTicket ticket)
    {
        lock (gate)
        {
            if (reservation == ticket)
            {
                reservation = null;
                receivingUpload = false;
                NotifyLocked();
            }
        }
    }

    /// <summary>Records the Running scan and starts its pipeline; null when the reservation was stopped meanwhile.</summary>
    internal Scan? Launch(ReservationTicket ticket, ScanTarget target, IStagingArea? staging)
    {
        var scan = Scan.Start(ticket.Id, target, timeProvider.GetUtcNow());
        var hub = new ScanEventHub(scan);
        var breaker = new NvdOutageBreaker(settings.Correlation.OutageThreshold);
        var run = new ScanRun(scan, staging, hub, breaker, settings.Timeouts.Scan, timeProvider);
        lock (gate)
        {
            if (reservation != ticket || ticket.IsStopped)
            {
                run.Dispose();
                return null;
            }

            reservation = null;
            receivingUpload = false;
            running = run;
            NotifyLocked();
        }

        try
        {
            registry.Add(scan);
            router.Register(scan.Id, hub);
        }
        catch
        {
            lock (gate)
            {
                running = null;
                NotifyLocked();
            }

            run.Dispose();
            throw;
        }

        _ = Task.Run(() => RunAndReleaseAsync(run));
        return scan;
    }

    /// <summary>Stops the running scan with that id; returns when its run has ended, or null if it is not running.</summary>
    internal Task? TryStop(ScanId id, StopCause cause)
    {
        ScanRun? run;
        lock (gate)
        {
            run = running is { } current && current.Id == id ? current : null;
        }

        run?.Stop(cause);
        return run?.Completion;
    }

    /// <summary>Emergency Stop (AR-28): ends whatever holds the slot and returns its id, or null when nothing did.</summary>
    internal ScanId? StopActive(StopCause cause)
    {
        ScanRun? run;
        ReservationTicket? ticket = null;
        lock (gate)
        {
            run = running;
            if (run is null && reservation is not null)
            {
                ticket = reservation;
                reservation = null;
                receivingUpload = false;
                NotifyLocked();
            }
        }

        if (run is not null)
        {
            using (logger.BeginScope(ScanLogScope.For(run.Id)))
            {
                logger.EmergencyStop(run.Id);
            }

            run.Stop(cause);
            return run.Id;
        }

        ticket?.Stop();
        return ticket?.Id;
    }

    /// <summary>Server shutdown (AR-30): ends the reservation and the running scan and waits for its cleanup.</summary>
    internal async Task StopAllAsync()
    {
        ScanRun? run;
        ReservationTicket? ticket;
        lock (gate)
        {
            run = running;
            ticket = reservation;
            if (ticket is not null)
            {
                reservation = null;
                receivingUpload = false;
                NotifyLocked();
            }
        }

        ticket?.Stop();
        if (run is not null)
        {
            run.Stop(StopCause.ServerShutdown);
            await run.Completion;
        }
    }

    internal ScanEventHub? HubFor(ScanId id)
    {
        lock (gate)
        {
            return running is { } run && run.Id == id ? run.Hub : null;
        }
    }

    // AR-33
    internal void Prune()
    {
        foreach (var id in RetentionPolicy.ScansToRemove(registry.List(), timeProvider.GetUtcNow(), settings.Retention))
        {
            registry.Remove(id);
        }
    }

    // AR-32: after the pipeline's own cleanup, release the slot whatever happened, then prune.
    private async Task RunAndReleaseAsync(ScanRun run)
    {
        try
        {
            await pipeline.RunAsync(run);
        }
        catch (Exception exception)
        {
            logger.CleanupFailed("run the scan", ExternalText.Clean(exception.Message));
        }
        finally
        {
            router.Unregister(run.Id);
            lock (gate)
            {
                if (running == run)
                {
                    running = null;
                    NotifyLocked();
                }
            }

            try
            {
                Prune();
            }
            catch (Exception exception)
            {
                logger.CleanupFailed("prune finished scans", ExternalText.Clean(exception.Message));
            }

            run.MarkCompleted();
            run.Dispose();
        }
    }

    private ActiveScanState StateLocked() =>
        running is not null ? new ActiveScanState(running.Id, false)
        : reservation is not null ? new ActiveScanState(reservation.Id, receivingUpload)
        : ActiveScanState.Idle;

    private void NotifyLocked()
    {
        var state = StateLocked();
        foreach (var watcher in watchers)
        {
            watcher.Writer.TryWrite(state);
        }
    }
}
