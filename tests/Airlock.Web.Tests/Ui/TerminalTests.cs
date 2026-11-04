using System.Text.RegularExpressions;
using Airlock.Application.UseCases;
using Airlock.Web.Components.Pages;
using Airlock.Web.Components.Parts;
using Airlock.Web.Tests.Support;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Airlock.Web.Tests.Ui;

// UR-17, UR-19, UR-20: the Live Log's lines, Export Logs, and Emergency Stop behind its question.
public sealed partial class TerminalTests
{
    [Fact]
    public async Task A_line_reads_the_time_in_the_browsers_zone_the_level_and_the_message()
    {
        await using var ui = await ReadyAsync();
        var terminal = ui.Render<Terminal>();

        ui.LiveLog.Write("📥 Processing 1 uploaded artifact(s)", at: Test.StartTime.AddSeconds(92));

        await ui.UntilAsync(() => Assert.Equal(["14:01:32 INFO 📥 Processing 1 uploaded artifact(s)"], Lines(terminal)));
    }

    [Theory]
    [InlineData(LogLevel.Trace, "TRACE", "info")]
    [InlineData(LogLevel.Debug, "DEBUG", "info")]
    [InlineData(LogLevel.Information, "INFO", "info")]
    [InlineData(LogLevel.Warning, "WARNING", "warning")]
    [InlineData(LogLevel.Error, "ERROR", "error")]
    [InlineData(LogLevel.Critical, "CRITICAL", "error")]
    public async Task Each_level_has_its_name_and_its_colour(LogLevel level, string name, string look)
    {
        await using var ui = await ReadyAsync();
        var terminal = ui.Render<Terminal>();

        ui.LiveLog.Write("A line", level);

        await ui.UntilAsync(() =>
        {
            var line = terminal.Find(".airlock-terminal__line");
            Assert.Equal(name, line.QuerySelector(".airlock-terminal__level")!.TextContent);
            Assert.Contains("airlock-terminal__line--" + look, line.ClassName, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task An_empty_terminal_stands_by()
    {
        await using var ui = await ReadyAsync();

        var terminal = ui.Render<Terminal>();

        Assert.Equal("📡 Standing by for telemetry...", terminal.Find(".airlock-terminal__empty").TextContent);
        Assert.Equal("log", terminal.Find(".airlock-terminal__lines").GetAttribute("role"));
        Assert.Equal("off", terminal.Find(".airlock-terminal__lines").GetAttribute("aria-live"));
    }

    [Fact]
    public async Task The_header_minimizes_and_restores_the_terminal()
    {
        await using var ui = await ReadyAsync();
        var terminal = ui.Render<Terminal>();

        terminal.Find("button[aria-label='Minimize terminal']").Click();
        Assert.Empty(terminal.FindAll(".airlock-terminal__lines"));
        Assert.Equal("false", terminal.Find("button[aria-label='Restore terminal']").GetAttribute("aria-expanded"));
        terminal.Find("button[aria-label='Restore terminal']").Click();

        Assert.Single(terminal.FindAll(".airlock-terminal__lines"));
        Assert.Equal(2, ui.Module.Invocations["followTerminal"].Count);
    }

    [Fact]
    public async Task Export_logs_saves_the_lines_as_shown_under_a_first_line_naming_version_time_and_zone()
    {
        await using var ui = await ReadyAsync();
        ui.Module.SetupVoid("downloadText", _ => true).SetVoidResult();
        var terminal = ui.Render<Terminal>();
        ui.LiveLog.Write("🚀 Initializing vetting sequence for: requirements.txt", at: Test.StartTime);
        ui.LiveLog.Write("⚠️ NVD CPE Search failed: NVD returned 503", LogLevel.Warning, at: Test.StartTime.AddSeconds(31));
        await ui.UntilAsync(() => Assert.Equal(2, Lines(terminal).Count));
        ui.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 22, 12, 5, 0, TimeSpan.Zero));

        terminal.FindAll("button").Single(button => button.TextContent.Trim() == "Export Logs").Click();

        var saved = Assert.Single(ui.Module.Invocations["downloadText"]);
        Assert.Equal("airlock_audit_2026-10-22T12-05-00-000Z.log", saved.Arguments[0]);
        Assert.Equal(
            "# Airlock 2.0.0 Live Log, exported 2026-10-22 14:05:00 UTC+02:00 (Europe/Berlin)\n"
            + "14:00:00 INFO 🚀 Initializing vetting sequence for: requirements.txt\n"
            + "14:00:31 WARNING ⚠️ NVD CPE Search failed: NVD returned 503\n",
            saved.Arguments[1]);
    }

    [Fact]
    public async Task Emergency_stop_is_enabled_only_while_something_holds_the_slot()
    {
        await using var ui = await ReadyAsync();
        var terminal = ui.Render<Terminal>();
        Assert.True(StopButton(terminal).HasAttribute("disabled"));

        var reserved = await ui.Services.GetRequiredService<StartScan>().ReserveAsync(Test.Token);
        await using var reservation = reserved.Value!;

        await ui.UntilAsync(() => Assert.False(StopButton(terminal).HasAttribute("disabled")));
    }

    [Fact]
    public async Task Dismissing_the_question_changes_nothing()
    {
        await using var ui = await ReadyAsync();
        var (_, dialogs, _) = ui.RenderProviders();
        var terminal = ui.Render<Terminal>();
        var reserved = await ui.Services.GetRequiredService<StartScan>().ReserveAsync(Test.Token);
        await using var reservation = reserved.Value!;
        await ui.UntilAsync(() => Assert.False(StopButton(terminal).HasAttribute("disabled")));

        StopButton(terminal).Click();
        await ui.UntilAsync(() => Assert.Contains("CRITICAL: Terminate the active scan?", dialogs.Markup, StringComparison.Ordinal));
        var buttons = dialogs.FindAll(".mud-dialog button");
        Assert.Equal(["Cancel", "Terminate"], buttons.Select(button => button.TextContent.Trim()));
        buttons[0].Click();

        await ui.UntilAsync(() => Assert.DoesNotContain("CRITICAL: Terminate the active scan?", dialogs.Markup, StringComparison.Ordinal));
        Assert.True(ui.Session.State.Active.IsActive);
        Assert.Equal(reservation.Id, ui.Session.State.Active.ScanId);
    }

    [Fact]
    public async Task Terminate_stops_the_scan_holding_the_slot()
    {
        await using var ui = await ReadyAsync();
        var (_, dialogs, _) = ui.RenderProviders();
        var directory = ui.Directory("acme-app");
        ui.Sbom.BlocksUntilCancelled(directory);
        var terminal = ui.Render<Terminal>();
        var scan = await ui.StartScanAsync(directory);
        await ui.UntilAsync(() => Assert.False(StopButton(terminal).HasAttribute("disabled")));

        StopButton(terminal).Click();
        await ui.UntilAsync(() => Assert.NotEmpty(dialogs.FindAll(".mud-dialog button")));
        dialogs.FindAll(".mud-dialog button").Single(button => button.TextContent.Trim() == "Terminate").Click();

        await ui.UntilAsync(() => Assert.Equal("Scan cancelled: Emergency Stop", ui.Session.State.Notice?.Text));
        Assert.Equal(scan.Id, ui.Session.State.Current!.Id);
    }

    [Fact]
    public async Task Terminate_when_nothing_is_active_any_more_says_so()
    {
        await using var ui = await ReadyAsync();
        var (_, dialogs, snackbars) = ui.RenderProviders();
        var terminal = ui.Render<Terminal>();
        var reserved = await ui.Services.GetRequiredService<StartScan>().ReserveAsync(Test.Token);
        var reservation = reserved.Value!;
        await ui.UntilAsync(() => Assert.False(StopButton(terminal).HasAttribute("disabled")));
        StopButton(terminal).Click();
        await ui.UntilAsync(() => Assert.NotEmpty(dialogs.FindAll(".mud-dialog button")));

        await reservation.DisposeAsync();
        dialogs.FindAll(".mud-dialog button").Single(button => button.TextContent.Trim() == "Terminate").Click();

        await ui.UntilAsync(() => Assert.Contains("No active scan", snackbars.Markup, StringComparison.Ordinal));
    }

    // Build and Test (E7): the scan ended while the question was open and took the tab to its report, so Emergency Stop had
    // left the page; returning the focus to it threw, which ended the circuit.
    [Fact]
    public async Task The_question_closing_once_emergency_stop_has_left_the_page_ends_nothing()
    {
        await using var ui = await ReadyAsync();
        var (_, dialogs, _) = ui.RenderProviders();
        var terminal = ui.Render<Terminal>();
        var reserved = await ui.Services.GetRequiredService<StartScan>().ReserveAsync(Test.Token);
        await using var reservation = reserved.Value!;
        await ui.UntilAsync(() => Assert.False(StopButton(terminal).HasAttribute("disabled")));
        StopButton(terminal).Click();
        await ui.UntilAsync(() => Assert.NotEmpty(dialogs.FindAll(".mud-dialog button")));

        // What the browser answers when the element is no longer on the page.
        ui.JSInterop.SetupVoid("Blazor._internal.domWrapper.focus", _ => true).SetException(new JSException("Unable to focus an invalid element."));
        dialogs.FindAll(".mud-dialog button").Single(button => button.TextContent.Trim() == "Cancel").Click();

        await ui.UntilAsync(() => Assert.Empty(dialogs.FindAll(".mud-dialog")));
        Assert.False(ui.Renderer.UnhandledException.IsCompleted);
    }

    [Fact]
    public async Task Active_logs_names_the_machine_and_shows_the_terminal_at_full_height()
    {
        await using var ui = await ReadyAsync();

        var page = ui.Render<ActiveLogs>();

        Assert.Equal("System Vulnerability Scan", page.Find("h1").TextContent);
        Assert.Equal($"Node: {Environment.MachineName} // Mode: Tactical Analysis", page.Find(".airlock-subtitle").TextContent);
        Assert.Single(page.FindAll(".airlock-terminal--full"));
        Assert.Equal("00:00", page.Find(".airlock-timer__value").TextContent);
    }

    private static async Task<UiTestContext> ReadyAsync()
    {
        var ui = new UiTestContext();
        ui.PlanPage();
        await ui.StartSessionAsync();
        return ui;
    }

    private static AngleSharp.Dom.IElement StopButton(IRenderedComponent<Terminal> terminal) =>
        terminal.FindAll("button").Single(button => button.TextContent.Trim() == "Emergency Stop");

    private static List<string> Lines(IRenderedComponent<Terminal> terminal) =>
        [.. terminal.FindAll(".airlock-terminal__line").Select(line => Spaces().Replace(line.TextContent.Trim(), " "))];

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}