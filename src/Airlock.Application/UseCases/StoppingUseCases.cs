using Airlock.Application.Options;
using Airlock.Application.Outcomes;
using Airlock.Application.Ports;
using Airlock.Application.Scanning;
using Airlock.Domain.Scanning;
using Microsoft.Extensions.Options;

namespace Airlock.Application.UseCases;

/// <summary>Cancels one running scan and waits briefly for it to record Cancelled (AR-27).</summary>
public sealed class CancelScan(
    ScanCoordinator coordinator, IScanRegistry registry, IOptions<AirlockOptions> options, TimeProvider timeProvider)
{
    internal const string NotFoundMessage = "Scan not found or no longer retained";
    internal const string AlreadyFinishedMessage = "The scan has already finished";

    public async ValueTask<Outcome<Scan>> ExecuteAsync(ScanId id, CancellationToken cancellationToken)
    {
        coordinator.Prune();
        var scan = registry.Find(id);
        if (scan is null)
        {
            return Outcome.Refused<Scan>(RefusalCode.ScanNotFound, NotFoundMessage);
        }

        if (scan.IsFinished)
        {
            return Outcome.Refused<Scan>(RefusalCode.ScanAlreadyFinished, AlreadyFinishedMessage);
        }

        if (coordinator.TryStop(id, StopCause.CancelledByRequest) is { } ended)
        {
            try
            {
                await ended.WaitAsync(options.Value.Timeouts.CancelGracePeriod, timeProvider, cancellationToken);
            }
            catch (TimeoutException)
            {
                // AR-27: the scan is returned as it stands; it records Cancelled on its own shortly after.
            }
        }

        return Outcome.Success(registry.Find(id) ?? scan);
    }
}

/// <summary>What Emergency Stop did: TERMINATED with the id of what held the slot, or IDLE.</summary>
public sealed record EmergencyStopResult(bool Stopped, ScanId? ScanId);

/// <summary>Cancels whatever holds the active slot, whoever started it (AR-28).</summary>
public sealed class EmergencyStop(ScanCoordinator coordinator)
{
    public ValueTask<EmergencyStopResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopped = coordinator.StopActive(StopCause.EmergencyStop);
        return ValueTask.FromResult(new EmergencyStopResult(stopped is not null, stopped));
    }
}
