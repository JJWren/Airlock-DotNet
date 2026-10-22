using Airlock.Domain.Scanning;

namespace Airlock.Application.Events;

/// <summary>Who holds the server's single active slot: nobody, a reservation (perhaps receiving an upload) or a scan.</summary>
public sealed record ActiveScanState(ScanId? ScanId, bool ReceivingUpload)
{
    public static ActiveScanState Idle { get; } = new(null, false);

    public bool IsActive => ScanId is not null;
}
