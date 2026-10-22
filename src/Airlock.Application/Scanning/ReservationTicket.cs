using Airlock.Domain.Scanning;

namespace Airlock.Application.Scanning;

/// <summary>A claim on the active slot before the scan starts; Emergency Stop ends it through <see cref="Stop"/>.</summary>
internal sealed class ReservationTicket(ScanId id) : IDisposable
{
    private readonly CancellationTokenSource stop = new();

    public ScanId Id { get; } = id;

    public CancellationToken StopToken => stop.Token;

    public bool IsStopped => stop.IsCancellationRequested;

    public void Stop()
    {
        try
        {
            stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The reservation has already ended.
        }
    }

    public void Dispose() => stop.Dispose();
}
