using Airlock.Application.Ports;
using Airlock.Domain.Scanning;

namespace Airlock.Infrastructure.Scans;

/// <summary>The latest version of every retained scan, in memory; nothing survives a restart (IR-14).</summary>
public sealed class InMemoryScanRegistry : IScanRegistry
{
    private readonly Lock gate = new();
    private readonly Dictionary<ScanId, Scan> scans = [];

    public void Add(Scan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        lock (gate)
        {
            if (!scans.TryAdd(scan.Id, scan))
            {
                throw new InvalidOperationException($"Scan {scan.Id} is already registered.");
            }
        }
    }

    public Scan? Find(ScanId id)
    {
        lock (gate)
        {
            return scans.GetValueOrDefault(id);
        }
    }

    // IR-14: the transition runs under the lock, so concurrent updates of one scan apply one after another.
    public Scan Update(ScanId id, Func<Scan, Scan> transition)
    {
        ArgumentNullException.ThrowIfNull(transition);
        lock (gate)
        {
            if (!scans.TryGetValue(id, out var current))
            {
                throw new InvalidOperationException($"Scan {id} is not registered.");
            }

            var next = transition(current) ?? throw new InvalidOperationException("A transition must return a scan.");
            if (next.Id != id)
            {
                throw new InvalidOperationException("A transition must keep the scan's id.");
            }

            scans[id] = next;
            return next;
        }
    }

    public IReadOnlyList<Scan> List()
    {
        lock (gate)
        {
            return [.. scans.Values];
        }
    }

    public void Remove(ScanId id)
    {
        lock (gate)
        {
            scans.Remove(id);
        }
    }
}
