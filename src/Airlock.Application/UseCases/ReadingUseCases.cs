using System.Globalization;
using System.Runtime.CompilerServices;
using Airlock.Application.Events;
using Airlock.Application.Outcomes;
using Airlock.Application.Ports;
using Airlock.Application.Scanning;
using Airlock.Domain.Scanning;

namespace Airlock.Application.UseCases;

/// <summary>The latest version of a retained scan, or null (AR-33 prunes first).</summary>
public sealed class GetScan(ScanCoordinator coordinator, IScanRegistry registry)
{
    public Scan? Execute(ScanId id)
    {
        coordinator.Prune();
        return registry.Find(id);
    }
}

/// <summary>A scan's events: its snapshot first and its finished event last; nothing for an unknown scan (AR-23).</summary>
public sealed class WatchScan(ScanCoordinator coordinator, IScanRegistry registry)
{
    public async IAsyncEnumerable<ScanEvent> SubscribeAsync(ScanId id, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        coordinator.Prune();
        if (coordinator.HubFor(id) is { } hub)
        {
            await foreach (var scanEvent in hub.SubscribeAsync(cancellationToken))
            {
                yield return scanEvent;
            }

            yield break;
        }

        if (registry.Find(id) is { IsFinished: true } scan)
        {
            yield return new ScanSnapshot(id, scan);
            yield return new ScanFinished(id, scan.Status);
        }
    }
}

/// <summary>Who holds the active slot, now and on every change (AR-26).</summary>
public sealed class WatchActiveScan(ScanCoordinator coordinator)
{
    public ActiveScanState Current => coordinator.Active;

    public IAsyncEnumerable<ActiveScanState> SubscribeAsync(CancellationToken cancellationToken) =>
        coordinator.WatchActiveAsync(cancellationToken);
}

/// <summary>The Live Log for one subscriber (FR-18).</summary>
public sealed class WatchLiveLog(ILiveLog liveLog)
{
    public IAsyncEnumerable<LiveLogEntry> SubscribeAsync(CancellationToken cancellationToken) => liveLog.SubscribeAsync(cancellationToken);
}

/// <summary>A rendered Scan Report.</summary>
public sealed record ScanReportDocument(string FileName, string ContentType, ReadOnlyMemory<byte> Content);

/// <summary>The PDF Scan Report of a Completed or Incomplete scan (AR-34).</summary>
public sealed class RenderScanReport(ScanCoordinator coordinator, IScanRegistry registry, IScanReportRenderer renderer)
{
    internal const string NotFinishedMessage = "The scan is still running";
    internal const string NoResultsMessage = "The scan ended without results";

    public async ValueTask<Outcome<ScanReportDocument>> ExecuteAsync(ScanId id, CancellationToken cancellationToken)
    {
        coordinator.Prune();
        var scan = registry.Find(id);
        if (scan is null)
        {
            return Outcome.Refused<ScanReportDocument>(RefusalCode.ScanNotFound, CancelScan.NotFoundMessage);
        }

        if (scan.FinishedAt is not { } finishedAt)
        {
            return Outcome.Refused<ScanReportDocument>(RefusalCode.ScanNotFinished, NotFinishedMessage);
        }

        if (scan.Status is ScanStatus.Failed or ScanStatus.Cancelled)
        {
            return Outcome.Refused<ScanReportDocument>(RefusalCode.ScanHasNoResults, NoResultsMessage);
        }

        var pdf = await renderer.RenderPdfAsync(scan, cancellationToken);
        var fileName = string.Create(CultureInfo.InvariantCulture, $"airlock_audit_{finishedAt.UtcDateTime:yyyy-MM-ddTHH-mm-ss-fff}Z.pdf");
        return Outcome.Success(new ScanReportDocument(fileName, "application/pdf", pdf));
    }
}

/// <summary>ONLINE or DEGRADED.</summary>
public enum HealthStatus
{
    Online,
    Degraded,
}

/// <summary>One readiness check.</summary>
public sealed record HealthCheck(string Name, bool Passing, string Detail);

/// <summary>Airlock's readiness and its checks.</summary>
public sealed record SystemHealth(HealthStatus Status, IReadOnlyList<HealthCheck> Checks);

/// <summary>Whether Syft is available and an NVD key is configured; it never calls NVD (AR-35).</summary>
public sealed class GetSystemHealth(ISbomGenerator sbomGenerator, IVulnerabilityCatalog catalog)
{
    public async ValueTask<SystemHealth> ExecuteAsync(CancellationToken cancellationToken)
    {
        var tool = await sbomGenerator.GetStatusAsync(cancellationToken);
        HealthCheck[] checks =
        [
            new("Syft", tool.Available, tool.Available ? tool.Version ?? tool.Detail : tool.Detail),
            new("NVD API key", catalog.IsConfigured, catalog.IsConfigured ? "configured" : StartScan.MissingKeyMessage),
        ];
        var status = Array.TrueForAll(checks, check => check.Passing) ? HealthStatus.Online : HealthStatus.Degraded;
        return new SystemHealth(status, checks);
    }
}
