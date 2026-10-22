using Airlock.Application.Logging;
using Airlock.Application.Options;
using Airlock.Application.Outcomes;
using Airlock.Application.Ports;
using Airlock.Application.Scanning;
using Airlock.Domain.Scanning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Airlock.Application.UseCases;

/// <summary>Reserves the active slot for a new scan, before any upload is read (AR-01, AR-02).</summary>
public sealed class StartScan(
    ScanCoordinator coordinator,
    ISbomGenerator sbomGenerator,
    IVulnerabilityCatalog catalog,
    IArtifactStaging staging,
    IHostFileSystem fileSystem,
    IOptions<AirlockOptions> options,
    ILogger<StartScan> logger)
{
    internal const string ScanInProgressMessage = "A scan is already in progress";
    internal const string MissingKeyMessage = "NVD API key not configured";

    public async ValueTask<Outcome<ScanReservation>> ReserveAsync(CancellationToken cancellationToken)
    {
        // AR-01: readiness first, with every reason that applies.
        List<string> reasons = [];
        if (!catalog.IsConfigured)
        {
            reasons.Add(MissingKeyMessage);
        }

        var tool = await sbomGenerator.GetStatusAsync(cancellationToken);
        if (!tool.Available)
        {
            reasons.Add(tool.Detail);
        }

        if (reasons.Count > 0)
        {
            return Refuse(RefusalCode.NotReady, string.Join("; ", reasons));
        }

        // AR-02
        var ticket = new ReservationTicket(ScanId.New());
        if (!coordinator.TryReserve(ticket))
        {
            ticket.Dispose();
            return Refuse(RefusalCode.ScanInProgress, ScanInProgressMessage);
        }

        return Outcome.Success(
            new ScanReservation(ticket, coordinator, staging, fileSystem, options.Value, logger));
    }

    private Outcome<ScanReservation> Refuse(RefusalCode code, string message)
    {
        logger.ScanRefused(message);
        return Outcome.Refused<ScanReservation>(code, message);
    }
}
