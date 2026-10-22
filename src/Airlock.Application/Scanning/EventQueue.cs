using System.Runtime.CompilerServices;
using Airlock.Application.Events;

namespace Airlock.Application.Scanning;

/// <summary>
/// One subscriber's buffer (U2-NFR-07). When it is full, the oldest log or progress event is dropped; snapshots, phase
/// changes and the finished event never are. Writing never waits.
/// </summary>
internal sealed class EventQueue(int capacity)
{
    private readonly Lock gate = new();
    private readonly LinkedList<ScanEvent> events = new();
    private TaskCompletionSource? waiter;
    private bool completed;

    public void Write(ScanEvent scanEvent)
    {
        TaskCompletionSource? toWake;
        lock (gate)
        {
            if (completed)
            {
                return;
            }

            if (events.Count >= capacity)
            {
                DropOldestDroppable();
            }

            events.AddLast(scanEvent);
            toWake = TakeWaiter();
        }

        toWake?.TrySetResult();
    }

    public void Complete()
    {
        TaskCompletionSource? toWake;
        lock (gate)
        {
            completed = true;
            toWake = TakeWaiter();
        }

        toWake?.TrySetResult();
    }

    public async IAsyncEnumerable<ScanEvent> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (true)
        {
            ScanEvent? next = null;
            Task? wait = null;
            bool done;
            lock (gate)
            {
                done = events.Count == 0 && completed;
                if (events.First is { } first)
                {
                    next = first.Value;
                    events.RemoveFirst();
                }
                else if (!done)
                {
                    waiter ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    wait = waiter.Task;
                }
            }

            if (done)
            {
                yield break;
            }

            if (next is not null)
            {
                yield return next;
            }
            else if (wait is not null)
            {
                await wait.WaitAsync(cancellationToken);
            }
        }
    }

    private void DropOldestDroppable()
    {
        for (var node = events.First; node is not null; node = node.Next)
        {
            if (node.Value is ScanLogged or PackagesCorrelated)
            {
                events.Remove(node);
                return;
            }
        }
    }

    private TaskCompletionSource? TakeWaiter()
    {
        var taken = waiter;
        waiter = null;
        return taken;
    }
}
