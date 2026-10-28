namespace Airlock.Web.State;

/// <summary>
/// U5-NFR-02: at most one redraw of a component per 250 ms. The first request redraws at once; requests within the next
/// 250 ms bring one more redraw when the interval ends. It runs on the Application's <see cref="TimeProvider"/>, so
/// tests drive it with a fake clock.
/// </summary>
internal sealed class RenderCoalescer : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    private readonly TimeProvider clock;
    private readonly Func<Task> redraw;
    private readonly Lock gate = new();
    private ITimer? timer;
    private long lastRedraw;
    private bool redrawn;
    private bool pending;
    private bool disposed;

    public RenderCoalescer(TimeProvider clock, Func<Task> redraw)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(redraw);
        this.clock = clock;
        this.redraw = redraw;
    }

    public void Request()
    {
        lock (gate)
        {
            if (disposed || pending)
            {
                return;
            }

            var now = clock.GetTimestamp();
            var since = redrawn ? clock.GetElapsedTime(lastRedraw, now) : Interval;
            if (since < Interval)
            {
                pending = true;
                timer ??= clock.CreateTimer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                timer.Change(Interval - since, Timeout.InfiniteTimeSpan);
                return;
            }

            redrawn = true;
            lastRedraw = now;
        }

        _ = RedrawAsync();
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            pending = false;
        }

        timer?.Dispose();
    }

    private void Flush()
    {
        lock (gate)
        {
            if (disposed || !pending)
            {
                return;
            }

            pending = false;
            redrawn = true;
            lastRedraw = clock.GetTimestamp();
        }

        _ = RedrawAsync();
    }

    private async Task RedrawAsync()
    {
        try
        {
            await redraw();
        }
        catch (ObjectDisposedException)
        {
            // The circuit has ended; nothing is left to draw.
        }
    }
}