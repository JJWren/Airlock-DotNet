using Airlock.Application.Events;
using Airlock.Application.UseCases;
using Airlock.Domain.Scanning;
using Airlock.Web.Components.Layout;
using Airlock.Web.Components.Pages;
using Airlock.Web.Components.Parts;
using Airlock.Web.State;
using Airlock.Web.Tests.Support;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Airlock.Web.Tests.Ui;

// UR-03, UR-13 to UR-16, UR-18: the tab's session follows the active slot and its scan, whoever started it.
public sealed class ScanSessionTests
{
    [Fact]
    public async Task Moving_between_pages_keeps_the_path_the_staged_files_and_the_lines()
    {
        await using var ui = new UiTestContext();
        await ui.StartSessionAsync();
        ui.PlanPage();
        var first = ui.Render<ControlCenter>();
        first.Find("#target-path").Input("/uploads/acme-app");
        await ui.StageAsync(("1", "requirements.txt", 56));
        ui.LiveLog.Write("📥 Processing 1 uploaded artifact(s)");
        await ui.UntilAsync(() => Assert.Single(ui.Session.State.Lines));

        await ui.DisposeComponentsAsync();
        var logs = ui.Render<ActiveLogs>();
        Assert.Contains("📥 Processing 1 uploaded artifact(s)", logs.Markup, StringComparison.Ordinal);
        await ui.DisposeComponentsAsync();
        var again = ui.Render<ControlCenter>();

        Assert.Equal("/uploads/acme-app", again.Find("#target-path").GetAttribute("value"));
        Assert.Contains("requirements.txt", again.Find(".airlock-staged").TextContent, StringComparison.Ordinal);
        Assert.Contains("📥 Processing 1 uploaded artifact(s)", again.Find(".airlock-terminal").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reservation_shows_the_preparing_view_and_its_upload()
    {
        await using var ui = new UiTestContext();
        await ui.StartSessionAsync();
        ui.PlanPage();
        var page = ui.Render<ControlCenter>();

        var reserved = await ui.Services.GetRequiredService<StartScan>().ReserveAsync(Test.Token);
        await using var reservation = reserved.Value!;
        await ui.UntilAsync(() =>
        {
            Assert.True(ui.Session.State.IsPreparing);
            var execute = page.Find(".airlock-execute button");
            Assert.Equal("Vetting Sequence Active...", execute.TextContent.Trim());
            Assert.True(execute.HasAttribute("disabled"));
        });
        using var body = new MemoryStream(A.Bytes(A.RequirementsText));
        await reservation.AddArtifactAsync("requirements.txt", body, Test.Token);

        await ui.UntilAsync(() => Assert.Contains("Receiving artifacts…", page.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_scan_started_elsewhere_is_followed_to_its_end_with_a_notice()
    {
        await using var ui = new UiTestContext();
        var release = new TaskCompletionSource();
        var directory = ui.Directory("acme-app");
        ui.Sbom.ProducesAfter(directory, release.Task, A.Sbom("flask", "0.12.2"));
        await ui.StartSessionAsync();
        var endings = new List<ScanEnding>();
        ui.Session.ScanEnded += endings.Add;

        var scan = await ui.StartScanAsync(directory);
        await ui.UntilAsync(() => Assert.Equal(scan.Id, ui.Session.State.Current?.Id));
        Assert.False(ui.Session.State.IsPreparing);
        release.SetResult();

        await ui.UntilAsync(() => Assert.Equal(ScanStatus.Completed, ui.Session.State.Current!.Status));
        var ending = Assert.Single(endings);
        Assert.False(ending.SubmittedHere);
        Assert.Equal(new InlineNotice(NoticeKind.Finished, "Scan COMPLETED", scan.Id), ui.Session.State.Notice);
    }

    [Fact]
    public async Task A_new_session_picks_up_a_running_scan_and_keeps_the_replayed_lines()
    {
        await using var ui = new UiTestContext();
        var directory = ui.Directory("acme-app");
        ui.Sbom.BlocksUntilCancelled(directory);
        var scan = await ui.StartScanAsync(directory);
        await ui.Sbom.RunStarted.Task.Within();
        ui.LiveLog.Replay.Add(new LiveLogEntry(Test.StartTime, LogLevel.Information, "Airlock", "🔧 Syft 1.55.0 at /opt/airlock/bin/syft", null));

        await ui.StartSessionAsync();

        await ui.UntilAsync(() =>
        {
            Assert.Equal(scan.Id, ui.Session.State.Current?.Id);
            Assert.Single(ui.Session.State.Lines);
        });
    }

    [Fact]
    public async Task The_tab_that_submitted_a_scan_opens_its_report()
    {
        await using var ui = new UiTestContext();
        var directory = ui.Directory("acme-app");
        var release = new TaskCompletionSource();
        ui.Sbom.ProducesAfter(directory, release.Task, A.Sbom("flask", "0.12.2"));
        await ui.StartSessionAsync();
        ui.PlanSubmissions();
        var navigation = ui.Services.GetRequiredService<BunitNavigationManager>();
        ui.Render<MainLayout>(parameters => parameters.Add(page => page.Body, (RenderFragment)(body => body.AddContent(0, "The page"))));
        ui.Session.SetDirectoryPath(directory);

        var submitted = ui.Session.ExecuteAsync();
        var scan = await ui.StartScanAsync(directory);
        await ui.AnswerAsync(scan);
        Assert.Equal(new SubmissionOutcome(SubmissionKind.Accepted, scan.Id), await submitted.Within());
        release.SetResult();

        await ui.UntilAsync(() => Assert.EndsWith("/reports/" + scan.Id, navigation.Uri, StringComparison.Ordinal));
        Assert.Null(ui.Session.State.Notice);
    }

    [Theory]
    [InlineData(false, "COMPLETED")]
    [InlineData(true, "INCOMPLETE")]
    public async Task Every_other_tab_gets_the_snackbar_and_the_notice(bool lookupFails, string status)
    {
        await using var ui = new UiTestContext();
        var directory = ui.Directory("acme-app");
        var release = new TaskCompletionSource();
        ui.Sbom.ProducesAfter(directory, release.Task, A.Sbom("jinja2", "2.10"));
        if (lookupFails)
        {
            ui.Catalog.FailsFor(A.CpeFor("jinja2", "2.10"), "NVD returned 503");
        }

        await ui.StartSessionAsync();
        var navigation = ui.Services.GetRequiredService<BunitNavigationManager>();
        var layout = ui.Render<MainLayout>(parameters => parameters.Add(page => page.Body, (RenderFragment)(body => body.AddContent(0, "The page"))));

        var scan = await ui.StartScanAsync(directory);
        release.SetResult();

        await ui.UntilAsync(() =>
        {
            var snackbar = layout.Find(".mud-snackbar");
            Assert.Contains($"Scan {status} — View report", snackbar.TextContent, StringComparison.Ordinal);
            Assert.Equal("/reports/" + scan.Id, snackbar.QuerySelector("a")!.GetAttribute("href"));
        });
        Assert.Equal(new InlineNotice(NoticeKind.Finished, "Scan " + status, scan.Id), ui.Session.State.Notice);
        Assert.Empty(navigation.History);
    }

    [Fact]
    public async Task A_failed_scan_brings_its_reason()
    {
        await using var ui = new UiTestContext();
        var directory = ui.Directory("acme-app");
        ui.Sbom.Fails(directory, new ScanFailure(ScanFailureCode.SyftFailed, "Syft failed for acme-app", directory));
        await ui.StartSessionAsync();

        var scan = await ui.StartScanAsync(directory);

        await ui.UntilAsync(() => Assert.Equal(new InlineNotice(NoticeKind.Failed, "Scan failed: Syft failed for acme-app", scan.Id), ui.Session.State.Notice));
    }

    [Fact]
    public async Task A_cancelled_scan_brings_its_reason()
    {
        await using var ui = new UiTestContext();
        var directory = ui.Directory("acme-app");
        ui.Sbom.BlocksUntilCancelled(directory);
        await ui.StartSessionAsync();
        var scan = await ui.StartScanAsync(directory);
        await ui.UntilAsync(() => Assert.Equal(scan.Id, ui.Session.State.Current?.Id));

        var stopped = await ui.Session.StopAsync(Test.Token);

        Assert.Equal(new EmergencyStopResult(true, scan.Id), stopped);
        await ui.UntilAsync(() => Assert.Equal(new InlineNotice(NoticeKind.Cancelled, "Scan cancelled: Emergency Stop", scan.Id), ui.Session.State.Notice));
    }

    [Fact]
    public async Task The_terminal_starts_afresh_when_another_scan_takes_the_slot()
    {
        await using var ui = new UiTestContext();
        await ui.StartSessionAsync();
        ui.LiveLog.Write("Before the scan");
        await ui.UntilAsync(() => Assert.Single(ui.Session.State.Lines));

        var reserved = await ui.Services.GetRequiredService<StartScan>().ReserveAsync(Test.Token);
        await using var reservation = reserved.Value!;
        await ui.UntilAsync(() => Assert.Equal(reservation.Id, ui.Session.State.Active.ScanId));
        ui.LiveLog.Write("The scan's own line", scanId: reservation.Id);
        ui.LiveLog.Write("Any line afterwards");

        await ui.UntilAsync(() => Assert.Equal(
            ["The scan's own line", "Any line afterwards"],
            ui.Session.State.Lines.Select(line => line.Message)));
    }

    [Fact]
    public async Task The_new_scans_lines_survive_whichever_arrives_first()
    {
        await using var ui = new UiTestContext();
        await ui.StartSessionAsync();
        ui.LiveLog.Write("Before the scan");
        await ui.UntilAsync(() => Assert.Single(ui.Session.State.Lines));

        // The scan's first line races the slot's change through the session's two feeds.
        var reserved = await ui.Services.GetRequiredService<StartScan>().ReserveAsync(Test.Token);
        await using var reservation = reserved.Value!;
        ui.LiveLog.Write("The scan's own line", scanId: reservation.Id);

        await ui.UntilAsync(() =>
        {
            Assert.Equal(reservation.Id, ui.Session.State.Active.ScanId);
            Assert.Equal(["The scan's own line"], ui.Session.State.Lines.Select(line => line.Message));
        });
    }

    [Fact]
    public async Task The_timer_reads_zero_without_a_scan()
    {
        await using var ui = new UiTestContext();

        var timer = ui.Render<ElapsedTimer>();

        Assert.Equal("00:00", timer.Find(".airlock-timer__value").TextContent);
    }

    [Fact]
    public async Task The_timer_counts_whole_seconds_from_the_start_while_the_scan_runs()
    {
        await using var ui = new UiTestContext();
        var scan = Scans.Running();
        ui.Clock.Advance(TimeSpan.FromSeconds(47.6));

        var timer = ui.Render<ElapsedTimer>(parameters => parameters.Add(component => component.Scan, scan));
        Assert.Equal("00:47", timer.Find(".airlock-timer__value").TextContent);
        ui.Clock.Advance(TimeSpan.FromSeconds(1));

        timer.WaitForAssertion(() => Assert.Equal("00:48", timer.Find(".airlock-timer__value").TextContent));
    }

    [Fact]
    public async Task The_timer_shows_hours_from_one_hour()
    {
        await using var ui = new UiTestContext();
        ui.Clock.Advance(new TimeSpan(1, 2, 3));

        var timer = ui.Render<ElapsedTimer>(parameters => parameters.Add(component => component.Scan, Scans.Running()));

        Assert.Equal("1:02:03", timer.Find(".airlock-timer__value").TextContent);
    }

    [Fact]
    public async Task The_timer_stops_at_the_finish()
    {
        await using var ui = new UiTestContext();
        ui.Clock.Advance(TimeSpan.FromMinutes(10));

        var timer = ui.Render<ElapsedTimer>(parameters => parameters.Add(component => component.Scan, WorkedExample.Finished()));
        ui.Clock.Advance(TimeSpan.FromMinutes(1));

        Assert.Equal("01:32", timer.Find(".airlock-timer__value").TextContent);
    }
}