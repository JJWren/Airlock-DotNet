using Airlock.Web.Components.Parts;
using Airlock.Web.State;
using Airlock.Web.Tests.Support;
using Bunit;
using Microsoft.Extensions.Time.Testing;

namespace Airlock.Web.Tests.Quality;

// U5-NFR-02, U5-NFR-03: redraws coalesced to four a second; a tab's lines and subscriptions bounded, and ended with it.
public sealed class UiResponsivenessTests
{
    [Fact]
    public void A_burst_of_requests_draws_at_once_and_once_more_when_the_interval_ends()
    {
        var clock = new FakeTimeProvider(Test.StartTime);
        var redraws = 0;
        using var coalescer = new RenderCoalescer(clock, () =>
        {
            Interlocked.Increment(ref redraws);
            return Task.CompletedTask;
        });

        for (var i = 0; i < 100; i++)
        {
            coalescer.Request();
            clock.Advance(TimeSpan.FromMilliseconds(2));
        }

        Assert.Equal(1, redraws);
        clock.Advance(RenderCoalescer.Interval);
        Assert.Equal(2, redraws);
        clock.Advance(TimeSpan.FromSeconds(1));
        coalescer.Request();
        Assert.Equal(3, redraws);
    }

    [Fact]
    public void A_disposed_coalescer_draws_nothing()
    {
        var clock = new FakeTimeProvider(Test.StartTime);
        var redraws = 0;
        var coalescer = new RenderCoalescer(clock, () =>
        {
            redraws++;
            return Task.CompletedTask;
        });
        coalescer.Request();
        coalescer.Request();

        coalescer.Dispose();
        clock.Advance(TimeSpan.FromSeconds(1));
        coalescer.Request();

        Assert.Equal(1, redraws);
    }

    [Fact]
    public async Task A_burst_of_a_hundred_lines_redraws_the_terminal_at_most_twice()
    {
        await using var ui = new UiTestContext();
        ui.PlanPage();
        await ui.StartSessionAsync();
        var terminal = ui.Render<Terminal>();
        ui.Clock.Advance(TimeSpan.FromSeconds(1));
        var before = terminal.RenderCount;

        for (var i = 0; i < 100; i++)
        {
            ui.LiveLog.Write($"line {i}");
        }

        await Test.EventuallyAsync(() => ui.Session.State.Lines.Count == 100, "the burst reaching the session");
        await Task.Delay(100, Test.Token);
        Assert.True(terminal.RenderCount - before <= 1, $"{terminal.RenderCount - before} redraws within the burst's interval.");
        ui.Clock.Advance(RenderCoalescer.Interval);
        terminal.WaitForAssertion(() => Assert.Equal(100, terminal.FindAll(".airlock-terminal__line").Count));
        Assert.True(terminal.RenderCount - before <= 2, $"{terminal.RenderCount - before} redraws for 100 lines.");
    }

    [Fact]
    public async Task The_terminal_keeps_the_last_thousand_lines()
    {
        await using var ui = new UiTestContext();
        await ui.StartSessionAsync();

        for (var i = 0; i < 1_500; i++)
        {
            ui.LiveLog.Write($"line {i}");
        }

        await Test.EventuallyAsync(() => ui.Session.State.Lines.LastOrDefault()?.Message == "line 1499", "the last line");
        Assert.Equal(ScanSession.MaxLines, ui.Session.State.Lines.Count);
        Assert.Equal("line 500", ui.Session.State.Lines[0].Message);
    }

    [Fact]
    public async Task Disposing_the_session_ends_its_subscriptions()
    {
        await using var ui = new UiTestContext();
        await ui.StartSessionAsync();
        Assert.Equal(1, ui.LiveLog.SubscriberCount);

        await ui.Session.DisposeAsync().AsTask().Within();

        await Test.EventuallyAsync(() => ui.LiveLog.SubscriberCount == 0, "the Live Log's subscriber leaving");
    }
}