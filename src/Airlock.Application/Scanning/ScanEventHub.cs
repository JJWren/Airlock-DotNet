using System.Runtime.CompilerServices;
using Airlock.Application.Events;
using Airlock.Domain.Scanning;

namespace Airlock.Application.Scanning;

/// <summary>
/// The events of one running scan, for any number of subscribers (AR-21, AR-23). It owns the scan's latest version, so
/// a new subscriber's snapshot and the events after it never overlap or leave a gap; publishing never waits.
/// </summary>
public sealed class ScanEventHub
{
    internal const int SubscriberCapacity = 1_000;

    private readonly Lock gate = new();
    private readonly List<EventQueue> subscribers = [];
    private Scan latest;
    private bool completed;

    internal ScanEventHub(Scan initial) => latest = initial;

    public Scan Latest
    {
        get
        {
            lock (gate)
            {
                return latest;
            }
        }
    }

    /// <summary>A snapshot first, then the later events in order, ending with the finished event (AR-23).</summary>
    public async IAsyncEnumerable<ScanEvent> SubscribeAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var queue = new EventQueue(SubscriberCapacity);
        Scan snapshot;
        bool alreadyCompleted;
        lock (gate)
        {
            snapshot = latest;
            alreadyCompleted = completed;
            if (!completed)
            {
                subscribers.Add(queue);
            }
        }

        yield return new ScanSnapshot(snapshot.Id, snapshot);
        if (alreadyCompleted)
        {
            yield return new ScanFinished(snapshot.Id, snapshot.Status);
            yield break;
        }

        try
        {
            await foreach (var scanEvent in queue.ReadAllAsync(cancellationToken))
            {
                yield return scanEvent;
            }
        }
        finally
        {
            lock (gate)
            {
                subscribers.Remove(queue);
            }
        }
    }

    internal void Publish(Scan updated, ScanEvent? scanEvent)
    {
        lock (gate)
        {
            if (completed)
            {
                return;
            }

            latest = updated;
            if (scanEvent is not null)
            {
                WriteToAll(scanEvent);
            }
        }
    }

    internal void PublishLog(LiveLogEntry entry)
    {
        lock (gate)
        {
            if (!completed)
            {
                WriteToAll(new ScanLogged(latest.Id, entry));
            }
        }
    }

    /// <summary>Publishes the finished event with the final version and ends every subscription (AR-32).</summary>
    internal void Complete(Scan final)
    {
        lock (gate)
        {
            if (completed)
            {
                return;
            }

            latest = final;
            completed = true;
            WriteToAll(new ScanFinished(final.Id, final.Status));
            foreach (var subscriber in subscribers)
            {
                subscriber.Complete();
            }

            subscribers.Clear();
        }
    }

    private void WriteToAll(ScanEvent scanEvent)
    {
        foreach (var subscriber in subscribers)
        {
            subscriber.Write(scanEvent);
        }
    }
}
