using System.Collections.Concurrent;
using Airlock.Infrastructure.Nvd;
using Airlock.Infrastructure.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Airlock.Infrastructure.Tests.Nvd;

// U3-NFR-03, IR-07 — one request start every 600 ms, application-wide; defect 9 (the rate could be exceeded).
public sealed class NvdRequestPacerTests : IDisposable
{
    private static readonly TimeSpan Spacing = TimeSpan.FromMilliseconds(600);

    private readonly FakeTimeProvider clock = new(Test.StartTime);
    private readonly NvdRequestPacer pacer;

    public NvdRequestPacerTests() => pacer = new NvdRequestPacer(Spacing, clock);

    [Fact]
    public async Task Sixteen_callers_start_at_least_600_ms_apart_and_never_more_than_50_in_30_seconds()
    {
        var starts = new ConcurrentBag<DateTimeOffset>();
        var callers = Enumerable.Range(0, 16).Select(async _ =>
        {
            for (var i = 0; i < 13; i++)
            {
                using var lease = await pacer.AcquireAsync(1, Test.Token);
                starts.Add(clock.GetUtcNow());
            }
        });

        await clock.DriveAsync(Task.WhenAll(callers), TimeSpan.FromMilliseconds(300), 10_000);

        var ordered = starts.Order().ToList();
        Assert.Equal(16 * 13, ordered.Count);
        Assert.All(ordered.Zip(ordered.Skip(1)), pair => Assert.True(pair.Second - pair.First >= Spacing));
        for (var i = 0; i + 50 < ordered.Count; i++)
        {
            Assert.True(ordered[i + 50] - ordered[i] >= TimeSpan.FromSeconds(30));
        }
    }

    [Fact]
    public async Task Waiters_take_their_turns_in_arrival_order()
    {
        using var first = await pacer.AcquireAsync(1, Test.Token);
        var order = new ConcurrentQueue<int>();
        var waiters = Enumerable.Range(0, 5).Select(index => Take(index, order)).ToList();

        await clock.DriveAsync(Task.WhenAll(waiters), Spacing);

        Assert.Equal([0, 1, 2, 3, 4], order);
    }

    [Fact]
    public async Task A_cancelled_waiter_leaves_without_taking_a_turn()
    {
        using var first = await pacer.AcquireAsync(1, Test.Token);
        using var cancel = new CancellationTokenSource();
        var cancelled = pacer.AcquireAsync(1, cancel.Token).AsTask();
        var next = pacer.AcquireAsync(1, Test.Token).AsTask();

        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        clock.Advance(Spacing);
        using var lease = await next.Within();

        Assert.Equal(Test.StartTime + Spacing, clock.GetUtcNow());
        Assert.Equal(2, pacer.GetStatistics()!.TotalSuccessfulLeases);
    }

    [Fact]
    public async Task Every_request_waits_for_its_turn_and_none_is_refused()
    {
        using var lease = pacer.AttemptAcquire(1);
        Assert.False(lease.IsAcquired);
        Assert.False(lease.TryGetMetadata("RETRY_AFTER", out _));
        Assert.Empty(lease.MetadataNames);

        using var check = await pacer.AcquireAsync(0, Test.Token);
        Assert.True(check.IsAcquired);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => pacer.AcquireAsync(2, Test.Token).AsTask());
    }

    [Fact]
    public async Task Statistics_and_idle_time_follow_the_turns()
    {
        Assert.Null(pacer.IdleDuration);
        using (await pacer.AcquireAsync(1, Test.Token))
        {
        }

        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(5), pacer.IdleDuration);
        var statistics = pacer.GetStatistics()!;
        Assert.Equal(1, statistics.TotalSuccessfulLeases);
        Assert.Equal(0, statistics.CurrentQueuedCount);
        Assert.Equal(0, statistics.TotalFailedLeases);
    }

    public void Dispose() => pacer.Dispose();

    private async Task Take(int index, ConcurrentQueue<int> order)
    {
        using var lease = await pacer.AcquireAsync(1, Test.Token);
        order.Enqueue(index);
    }
}
