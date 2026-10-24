using System.Diagnostics;
using Airlock.Application.Events;
using Airlock.Application.Logging;
using Airlock.Domain.Scanning;
using Airlock.Infrastructure.Logging;
using Airlock.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Airlock.Infrastructure.Tests.Logging;

// IR-15 to IR-18 — categories and levels, messages only, the scan id, the 200-line replay, many subscribers; defect 4.
public sealed class LiveLogTests : IDisposable
{
    private const string Sentinel = "sentinel";

    private readonly FakeTimeProvider clock = new(Test.StartTime);
    private readonly LiveLogBroadcaster broadcaster = new();
    private readonly ILoggerFactory factory;

    public LiveLogTests() => factory = Factory(new LiveLogLoggerProvider(broadcaster, new ScanLogRouter(), clock), LogLevel.Trace);

    [Fact]
    public async Task Only_Airlocks_own_categories_at_Debug_and_above_are_captured()
    {
        var airlock = factory.CreateLogger("Airlock.Infrastructure.Test");
        Write(airlock, LogLevel.Trace, "trace");
        Write(airlock, LogLevel.Debug, "debug");
        Write(airlock, LogLevel.Information, "information");
        Write(airlock, LogLevel.Critical, "critical");
        Write(factory.CreateLogger("Microsoft.AspNetCore.Test"), LogLevel.Warning, "framework");
        Write(factory.CreateLogger("AirlockLookalike.Test"), LogLevel.Warning, "lookalike");
        Write(factory.CreateLogger("Airlock"), LogLevel.Information, Sentinel);

        var entries = await UntilSentinelAsync();

        Assert.Equal(["debug", "information", "critical"], entries.Select(entry => entry.Message));
        Assert.All(entries, entry => Assert.Equal("Airlock.Infrastructure.Test", entry.Category));
        Assert.Equal(Test.StartTime, entries[0].Timestamp);
    }

    [Fact]
    public async Task An_entry_holds_the_formatted_message_and_never_the_exceptions_details()
    {
        Write(factory.CreateLogger("Airlock.Test"), LogLevel.Error, "Failed: boom", new InvalidOperationException("internal detail"));
        Write(factory.CreateLogger("Airlock"), LogLevel.Information, Sentinel);

        var entry = Assert.Single(await UntilSentinelAsync());

        Assert.Equal("Failed: boom", entry.Message);
        Assert.Equal(LogLevel.Error, entry.Level);
    }

    [Fact]
    public async Task The_scan_id_comes_from_the_innermost_ScanId_scope()
    {
        var logger = factory.CreateLogger("Airlock.Test");
        var outer = ScanId.New();
        var inner = ScanId.New();
        using (logger.BeginScope(ScanLogScope.For(outer)))
        {
            Write(logger, LogLevel.Information, "outer");
            using (logger.BeginScope(ScanLogScope.For(inner)))
            {
                Write(logger, LogLevel.Information, "inner");
            }
        }

        Write(logger, LogLevel.Information, "none");
        Write(factory.CreateLogger("Airlock"), LogLevel.Information, Sentinel);

        Assert.Equal([outer, inner, (ScanId?)null], (await UntilSentinelAsync()).Select(entry => entry.ScanId));
    }

    [Fact]
    public async Task Debug_lines_still_arrive_when_the_console_is_set_to_Warning()
    {
        using var warning = Factory(new LiveLogLoggerProvider(broadcaster, new ScanLogRouter(), clock), LogLevel.Warning);

        Write(warning.CreateLogger("Airlock.Test"), LogLevel.Debug, "still here");
        Write(warning.CreateLogger("Airlock"), LogLevel.Information, Sentinel);

        Assert.Equal("still here", Assert.Single(await UntilSentinelAsync()).Message);
    }

    [Fact]
    public async Task A_new_subscriber_first_receives_the_last_200_entries_oldest_first()
    {
        for (var i = 0; i < 250; i++)
        {
            broadcaster.Publish(Entry($"line {i}"));
        }

        var replay = await TakeAsync(broadcaster.SubscribeAsync(Test.Token), LiveLogBroadcaster.ReplayCount);

        Assert.Equal("line 50", replay[0].Message);
        Assert.Equal("line 249", replay[^1].Message);
    }

    [Fact]
    public async Task Two_subscribers_both_receive_every_line()
    {
        await using var first = broadcaster.SubscribeAsync(Test.Token).GetAsyncEnumerator(Test.Token);
        await using var second = broadcaster.SubscribeAsync(Test.Token).GetAsyncEnumerator(Test.Token);
        var firstLines = CollectAsync(first, 500);
        var secondLines = CollectAsync(second, 500);

        for (var i = 0; i < 500; i++)
        {
            broadcaster.Publish(Entry($"line {i}"));
        }

        Assert.Equal(500, (await firstLines.Within()).Count);
        Assert.Equal(500, (await secondLines.Within()).Count);
    }

    [Fact]
    public async Task A_subscriber_that_never_reads_slows_no_one_and_keeps_only_its_last_1000_entries()
    {
        await using var idle = broadcaster.SubscribeAsync(Test.Token).GetAsyncEnumerator(Test.Token);
        var pending = idle.MoveNextAsync().AsTask();
        var stopwatch = Stopwatch.StartNew();

        for (var i = 0; i < 5_000; i++)
        {
            broadcaster.Publish(Entry($"line {i}"));
        }

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
        Assert.True(await pending.Within());
        var kept = new List<LiveLogEntry> { idle.Current };
        while (kept[^1].Message != "line 4999" && await idle.MoveNextAsync().AsTask().Within())
        {
            kept.Add(idle.Current);
        }

        // The buffer held the newest 1,000 entries; the reader may have taken one more while publishing went on.
        Assert.InRange(kept.Count, 2, LiveLogBroadcaster.SubscriberCapacity + 1);
        var numbers = kept.Select(entry => int.Parse(entry.Message["line ".Length..], System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(numbers.Order(), numbers);
        Assert.True(numbers[^2] >= 5_000 - LiveLogBroadcaster.SubscriberCapacity);
    }

    [Fact]
    public async Task An_entry_logged_while_publishing_is_dropped_rather_than_recursing()
    {
        var talkative = new TalkativeClock();
        using var looping = Factory(new LiveLogLoggerProvider(broadcaster, new ScanLogRouter(), talkative), LogLevel.Trace);
        talkative.Logger = looping.CreateLogger("Airlock.Clock");

        Write(looping.CreateLogger("Airlock.Test"), LogLevel.Information, "outer");
        talkative.Logger = null;
        Write(looping.CreateLogger("Airlock"), LogLevel.Information, Sentinel);

        Assert.Equal("outer", Assert.Single(await UntilSentinelAsync()).Message);
    }

    [Fact]
    public void Publishing_a_null_entry_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => broadcaster.Publish(null!));
    }

    public void Dispose() => factory.Dispose();

    private static void Write(ILogger logger, LogLevel level, string message, Exception? exception = null) =>
        logger.Log(level, default, message, exception, static (state, _) => state);

    private static ILoggerFactory Factory(LiveLogLoggerProvider provider, LogLevel minimum) =>
        LoggerFactory.Create(builder => builder
            .SetMinimumLevel(minimum)
            .AddFilter<LiveLogLoggerProvider>(null, LogLevel.None)
            .AddFilter<LiveLogLoggerProvider>(LiveLogLoggerProvider.Category, LogLevel.Debug)
            .AddProvider(provider));

    private static LiveLogEntry Entry(string message) =>
        new(Test.StartTime, LogLevel.Information, "Airlock.Test", message, null);

    private static async Task<List<LiveLogEntry>> TakeAsync(IAsyncEnumerable<LiveLogEntry> entries, int count)
    {
        var taken = new List<LiveLogEntry>();
        await foreach (var entry in entries)
        {
            taken.Add(entry);
            if (taken.Count == count)
            {
                break;
            }
        }

        return taken;
    }

    private static async Task<List<LiveLogEntry>> CollectAsync(IAsyncEnumerator<LiveLogEntry> entries, int count)
    {
        var collected = new List<LiveLogEntry>();
        while (collected.Count < count && await entries.MoveNextAsync())
        {
            collected.Add(entries.Current);
        }

        return collected;
    }

    private async Task<List<LiveLogEntry>> UntilSentinelAsync()
    {
        var entries = new List<LiveLogEntry>();
        using var deadline = Test.Deadline(10);
        await foreach (var entry in broadcaster.SubscribeAsync(deadline.Token))
        {
            if (entry.Message == Sentinel)
            {
                return entries;
            }

            entries.Add(entry);
        }

        return entries;
    }

    /// <summary>A clock that logs whenever it is read, as a careless future change on the publishing path might.</summary>
    private sealed class TalkativeClock : TimeProvider
    {
        public ILogger? Logger { get; set; }

        public override DateTimeOffset GetUtcNow()
        {
            if (Logger is not null)
            {
                Write(Logger, LogLevel.Information, "logged while publishing");
            }

            return Test.StartTime;
        }
    }
}
