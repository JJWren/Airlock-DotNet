using Airlock.Web.Components.Pages;
using Airlock.Web.State;
using Airlock.Web.Tests.Support;
using Bunit;

namespace Airlock.Web.Tests.Quality;

// U5-NFR-05: no call from the server waits for an upload; its progress and answer reach the session, whichever page is
// open.
public sealed class UiUploadTests
{
    [Fact]
    public async Task Submitting_returns_at_once_and_the_upload_reports_through_the_session()
    {
        await using var ui = new UiTestContext();
        ui.PlanPage();
        ui.PlanSubmissions();
        await ui.StartSessionAsync();
        ui.Render<ControlCenter>();
        await ui.StageAsync(("1", "app-image.tar", 1L << 30));
        await ui.UntilAsync(() => Assert.Single(ui.Session.State.Staged));

        var submitted = ui.Session.ExecuteAsync();

        Assert.Single(ui.Module.Invocations["submitScan"]);
        Assert.False(submitted.IsCompleted);
        Assert.True(ui.Session.State.Submitting);

        // The analyst opens Active Logs while the body is sent.
        await ui.DisposeComponentsAsync();
        var logs = ui.Render<ActiveLogs>();
        await ui.Renderer.Dispatcher.InvokeAsync(() => ui.Session.Sink.Value.Progress(512L << 20, 1L << 30));
        Assert.Equal(new UploadProgress(512L << 20, 1L << 30), ui.Session.State.Upload);
        Assert.Equal(50, ui.Session.State.Upload!.Percent);

        var scan = await ui.StartScanAsync(null, ("app-image.tar", "not really an image"));
        await ui.AnswerAsync(scan);

        Assert.Equal(new SubmissionOutcome(SubmissionKind.Accepted, scan.Id), await submitted.Within());
        Assert.Null(ui.Session.State.Upload);
        Assert.Contains(scan.Id, ui.Session.State.Submitted);
        Assert.False(logs.IsDisposed);
    }

    [Fact]
    public async Task A_session_that_ends_during_an_upload_answers_its_own_wait()
    {
        await using var ui = new UiTestContext();
        ui.PlanSubmissions();
        await ui.StartSessionAsync();
        var submitted = ui.Session.ExecuteAsync();

        await ui.Session.DisposeAsync();

        Assert.Equal(SubmissionKind.Interrupted, (await submitted.Within()).Kind);
    }
}