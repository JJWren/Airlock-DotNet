using System.Threading.Channels;
using Airlock.Application.Events;
using Airlock.Application.Ports;
using Airlock.Domain.Scanning;

namespace Airlock.Application.Tests.Fakes;

/// <summary>The registry in memory, able to fail chosen updates (fault injection).</summary>
internal sealed class FakeScanRegistry : IScanRegistry
{
    private readonly Lock gate = new();
    private readonly Dictionary<ScanId, Scan> scans = [];

    /// <summary>When it returns true for a new version, the update throws instead of storing it.</summary>
    public Func<Scan, bool>? FailUpdateWhen { get; set; }

    public int UpdateCount { get; private set; }

    /// <summary>When set, adding a scan throws (fault injection).</summary>
    public bool FailAdd { get; set; }

    public void Add(Scan scan)
    {
        lock (gate)
        {
            if (FailAdd)
            {
                throw new InvalidOperationException("Registry unavailable");
            }

            scans.Add(scan.Id, scan);
        }
    }

    public Scan? Find(ScanId id)
    {
        lock (gate)
        {
            return scans.GetValueOrDefault(id);
        }
    }

    public Scan Update(ScanId id, Func<Scan, Scan> transition)
    {
        lock (gate)
        {
            var next = transition(scans[id]);
            if (FailUpdateWhen?.Invoke(next) == true)
            {
                throw new InvalidOperationException("Registry unavailable");
            }

            UpdateCount++;
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

/// <summary>A renderer that returns three bytes and records what it rendered.</summary>
internal sealed class FakeScanReportRenderer : IScanReportRenderer
{
    public List<Scan> Rendered { get; } = [];

    public ValueTask<byte[]> RenderPdfAsync(Scan scan, CancellationToken cancellationToken)
    {
        Rendered.Add(scan);
        return ValueTask.FromResult<byte[]>([0x25, 0x50, 0x44]);
    }
}

/// <summary>A Live Log that hands out what the test writes.</summary>
internal sealed class FakeLiveLog : ILiveLog
{
    private readonly Channel<LiveLogEntry> entries = Channel.CreateUnbounded<LiveLogEntry>();

    public void Write(LiveLogEntry entry) => entries.Writer.TryWrite(entry);

    public IAsyncEnumerable<LiveLogEntry> SubscribeAsync(CancellationToken cancellationToken) =>
        entries.Reader.ReadAllAsync(cancellationToken);
}
