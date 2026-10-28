using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Airlock.Application.Events;
using Airlock.Application.Ports;
using Airlock.Domain.Scanning;
using Microsoft.Extensions.Logging;

namespace Airlock.Web.Tests.Support;

/// <summary>
/// The Live Log, written by the test: a new subscriber gets <see cref="Replay"/> first, as the server's last lines, then
/// every entry written; it counts its subscribers.
/// </summary>
internal sealed class FakeLiveLog : ILiveLog
{
    private readonly Lock gate = new();
    private readonly List<Channel<LiveLogEntry>> subscribers = [];

    public List<LiveLogEntry> Replay { get; } = [];

    public int SubscriberCount
    {
        get
        {
            lock (gate)
            {
                return subscribers.Count;
            }
        }
    }

    public void Write(LiveLogEntry entry)
    {
        lock (gate)
        {
            foreach (var subscriber in subscribers)
            {
                subscriber.Writer.TryWrite(entry);
            }
        }
    }

    public void Write(string message, LogLevel level = LogLevel.Information, ScanId? scanId = null, DateTimeOffset? at = null) =>
        Write(new LiveLogEntry(at ?? Test.StartTime, level, "Airlock.Test", message, scanId));

    public Task UntilSubscribedAsync(int count = 1) => Test.EventuallyAsync(() => SubscriberCount >= count, "the Live Log's subscriber");

    public async IAsyncEnumerable<LiveLogEntry> SubscribeAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<LiveLogEntry>();
        lock (gate)
        {
            foreach (var entry in Replay)
            {
                channel.Writer.TryWrite(entry);
            }

            subscribers.Add(channel);
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
                subscribers.Remove(channel);
            }
        }
    }
}