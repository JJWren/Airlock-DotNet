using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Airlock.Application.Events;
using Airlock.Application.Ports;

namespace Airlock.Infrastructure.Logging;

/// <summary>The Live Log for every subscriber: the last 200 entries first, then each new one (IR-17).</summary>
public sealed class LiveLogBroadcaster : ILiveLog
{
    public const int ReplayCount = 200;

    public const int SubscriberCapacity = 1_000;

    private readonly Lock gate = new();
    private readonly LiveLogEntry[] ring = new LiveLogEntry[ReplayCount];
    private int next;
    private int count;
    private Channel<LiveLogEntry>[] subscribers = [];

    // IR-17: publishing never waits; every subscriber receives every entry its buffer can hold.
    public void Publish(LiveLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (gate)
        {
            ring[next] = entry;
            next = (next + 1) % ReplayCount;
            count = Math.Min(count + 1, ReplayCount);
            foreach (var subscriber in subscribers)
            {
                subscriber.Writer.TryWrite(entry);
            }
        }
    }

    public async IAsyncEnumerable<LiveLogEntry> SubscribeAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // IR-17: each subscriber has its own buffer, which drops its oldest entry when full.
        var channel = Channel.CreateBounded<LiveLogEntry>(new BoundedChannelOptions(SubscriberCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        // The replay and the subscription happen under the publishing lock, so no entry is missed or repeated.
        lock (gate)
        {
            for (var i = 0; i < count; i++)
            {
                channel.Writer.TryWrite(ring[(next - count + i + ReplayCount) % ReplayCount]);
            }

            subscribers = [.. subscribers, channel];
        }

        try
        {
            await foreach (var entry in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return entry;
            }
        }
        finally
        {
            lock (gate)
            {
                subscribers = [.. subscribers.Where(subscriber => subscriber != channel)];
            }
        }
    }
}
