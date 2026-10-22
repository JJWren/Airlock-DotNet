using System.Collections.Concurrent;
using Airlock.Application.Events;
using Airlock.Application.Scanning;
using Airlock.Domain.Scanning;

namespace Airlock.Application.Logging;

/// <summary>
/// Publishes each Live Log entry that carries a scan id on that scan's event hub as it is written (AR-24). U3's Live Log
/// provider calls <see cref="Route"/>; it has no dependencies, so the logging pipeline can hold it without a cycle.
/// </summary>
public sealed class ScanLogRouter
{
    private readonly ConcurrentDictionary<ScanId, ScanEventHub> hubs = new();

    public void Route(LiveLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.ScanId is { } id && hubs.TryGetValue(id, out var hub))
        {
            hub.PublishLog(entry);
        }
    }

    internal void Register(ScanId id, ScanEventHub hub) => hubs[id] = hub;

    internal void Unregister(ScanId id) => hubs.TryRemove(id, out _);
}
