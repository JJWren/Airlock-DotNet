using Airlock.Application.Ports;
using Airlock.Application.UseCases;
using Airlock.Web.Components.Pages;
using Airlock.Web.Interop;
using Airlock.Web.State;
using Airlock.Web.Tests.Support;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Airlock.Web.Tests.Ui;

// UR-06 to UR-12: the target, staging, the checks before uploading, Execute and its reasons, the answer, the notice.
public sealed class ControlCenterTests
{
    [Fact]
    public async Task The_path_starts_as_uploads_and_is_sent_as_typed()
    {
        await using var ui = await ReadyAsync();
        var page = ui.Render<ControlCenter>();
        Assert.Equal("/uploads", page.Find("#target-path").GetAttribute("value"));

        page.Find("#target-path").Input("/uploads/acme-app");
        page.Find(".airlock-execute button").Click();

        var target = Submitted(ui);
        Assert.Equal(ScanSubmissionTarget.ForPath("/uploads/acme-app"), target);
    }

    [Fact]
    public async Task Staged_artifacts_dim_the_path_and_are_sent_alone()
    {
        await using var ui = await ReadyAsync();
        var page = ui.Render<ControlCenter>();

        await ui.StageAsync(("1", "requirements.txt", 56), ("2", "app-image.tar", 1024));
        await ui.UntilAsync(() =>
        {
            Assert.True(page.Find("#target-path").HasAttribute("disabled"));
            Assert.Equal("/uploads", page.Find("#target-path").GetAttribute("value"));
            Assert.Contains("Staged artifacts are scanned instead of this path", page.Markup, StringComparison.Ordinal);
            Assert.Equal("2 File(s) Staged", page.Find(".airlock-staged .airlock-label").TextContent.Trim());
        });
        page.Find(".airlock-execute button").Click();

        var target = Submitted(ui);
        Assert.Null(target.TargetPath);
        Assert.Equal(["1", "2"], target.StagedIds);
    }

    [Fact]
    public async Task A_file_staged_twice_is_listed_once_with_its_size()
    {
        await using var ui = await ReadyAsync();
        var page = ui.Render<ControlCenter>();

        await ui.StageAsync(("1", "requirements.txt", 56));
        await ui.StageAsync(("2", "requirements.txt", 56), ("3", "app.jar", 12_698));

        await ui.UntilAsync(() => Assert.Equal(
            ["requirements.txt 56 B", "app.jar 12.4 KB"],
            page.FindAll(".airlock-staged__item").Select(item => item.QuerySelector(".airlock-staged__name")!.TextContent + " " + item.QuerySelector(".airlock-staged__size")!.TextContent)));
    }

    [Fact]
    public async Task Removing_the_last_file_brings_the_path_back()
    {
        await using var ui = await ReadyAsync();
        ui.Module.SetupVoid("unstage", "1").SetVoidResult();
        var page = ui.Render<ControlCenter>();
        await ui.StageAsync(("1", "requirements.txt", 56));
        await ui.UntilAsync(() => Assert.True(page.Find("#target-path").HasAttribute("disabled")));

        page.Find("button[aria-label='Remove requirements.txt']").Click();

        await ui.UntilAsync(() =>
        {
            Assert.False(page.Find("#target-path").HasAttribute("disabled"));
            Assert.Empty(page.FindAll(".airlock-staged"));
        });
        Assert.Single(ui.Module.Invocations["unstage"]);
    }

    [Fact]
    public async Task Too_many_files_are_refused_in_the_applications_words_and_nothing_is_sent()
    {
        await using var ui = await ReadyAsync();
        var page = ui.Render<ControlCenter>();
        await ui.StageAsync([.. Enumerable.Range(1, 26).Select(i => (i.ToString(System.Globalization.CultureInfo.InvariantCulture), $"file-{i}.bin", 1L))]);
        await ui.UntilAsync(() => Assert.Equal(26, ui.Session.State.Staged.Count));

        page.Find(".airlock-execute button").Click();

        await ui.UntilAsync(() => Assert.Equal(UploadLimits.Check(26, 26, ui.Session.Limits)!.Message, page.Find(".airlock-notice__text").TextContent.Trim()));
        Assert.Equal("At most 25 files per scan", ui.Session.State.Notice!.Text);
        Assert.Empty(ui.Module.Invocations["submitScan"]);
    }

    [Fact]
    public async Task Too_many_bytes_are_refused_in_the_applications_words()
    {
        await using var ui = await ReadyAsync(settings => settings["Airlock:Limits:MaxBytesPerScan"] = "1048576");
        var page = ui.Render<ControlCenter>();
        Assert.Equal("Up to 25 files, 1 MB in total", page.Find("#upload-limits").TextContent);
        await ui.StageAsync(("1", "a.bin", 600_000), ("2", "b.bin", 600_000));
        await ui.UntilAsync(() => Assert.Equal(2, ui.Session.State.Staged.Count));

        page.Find(".airlock-execute button").Click();

        await ui.UntilAsync(() => Assert.Equal("Uploads are limited to 1 MB per scan", page.Find(".airlock-notice__text").TextContent.Trim()));
    }

    [Fact]
    public async Task Execute_is_enabled_when_online_with_the_slot_free()
    {
        await using var ui = await ReadyAsync();

        var execute = ui.Render<ControlCenter>().Find(".airlock-execute button");

        Assert.Equal("Execute Security Audit", execute.TextContent.Trim());
        Assert.False(execute.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Execute_is_disabled_while_another_tab_holds_the_slot()
    {
        await using var ui = await ReadyAsync();
        var page = ui.Render<ControlCenter>();

        var reserved = await ui.Services.GetRequiredService<StartScan>().ReserveAsync(Test.Token);
        await using var reservation = reserved.Value!;

        await ui.UntilAsync(() =>
        {
            var execute = page.Find(".airlock-execute button");
            Assert.Equal("Vetting Sequence Active...", execute.TextContent.Trim());
            Assert.True(execute.HasAttribute("disabled"));
        });
    }

    [Fact]
    public async Task Execute_is_disabled_when_degraded_with_the_reasons()
    {
        await using var ui = new UiTestContext();
        ui.Catalog.IsConfigured = false;
        ui.PlanPage();
        await ui.StartSessionAsync();

        var page = ui.Render<ControlCenter>();

        Assert.True(page.Find(".airlock-execute button").HasAttribute("disabled"));
        Assert.Equal("Airlock is not ready: NVD API key not configured", page.Find(".airlock-execute__reason").TextContent);
    }

    [Fact]
    public async Task Execute_is_disabled_while_loading()
    {
        await using var ui = new UiTestContext();
        ui.PlanPage();

        var page = ui.Render<ControlCenter>();

        Assert.True(page.Find(".airlock-execute button").HasAttribute("disabled"));
        Assert.Equal("Checking system status...", page.Find(".airlock-execute__reason").TextContent);
    }

    [Fact]
    public async Task A_problems_detail_becomes_the_notice_and_the_staged_list_stays()
    {
        await using var ui = await ReadyAsync();
        var page = ui.Render<ControlCenter>();
        await ui.StageAsync(("1", "requirements.txt", 56));
        await ui.UntilAsync(() => Assert.Single(ui.Session.State.Staged));

        page.Find(".airlock-execute button").Click();
        Submitted(ui);
        await ui.AnswerAsync(409, detail: "A scan is already in progress");

        await ui.UntilAsync(() =>
        {
            Assert.Equal("A scan is already in progress", page.Find(".airlock-notice__text").TextContent.Trim());
            Assert.Contains("airlock-notice--error", page.Find(".airlock-notice").ClassName, StringComparison.Ordinal);
            Assert.Single(page.FindAll(".airlock-staged__item"));
            Assert.Equal("Execute Security Audit", page.Find(".airlock-execute button").TextContent.Trim());
        });
    }

    [Fact]
    public async Task No_answer_is_an_interrupted_upload()
    {
        await using var ui = await ReadyAsync();
        var page = ui.Render<ControlCenter>();

        page.Find(".airlock-execute button").Click();
        Submitted(ui);
        await ui.AnswerAsync(0);

        await ui.UntilAsync(() => Assert.Equal(
            "Upload interrupted — check the connection and try again", page.Find(".airlock-notice__text").TextContent.Trim()));
    }

    [Fact]
    public async Task The_transfer_shows_its_progress_and_the_button_its_activity()
    {
        await using var ui = await ReadyAsync();
        var page = ui.Render<ControlCenter>();
        await ui.StageAsync(("1", "app-image.tar", 1L << 30));
        await ui.UntilAsync(() => Assert.Single(ui.Session.State.Staged));

        page.Find(".airlock-execute button").Click();
        Submitted(ui);
        await ui.Renderer.Dispatcher.InvokeAsync(() => ui.Session.Sink.Value.Progress(450L << 20, 1L << 30));

        await ui.UntilAsync(() =>
        {
            Assert.Equal("Transferring artifacts: 43% (450.0 MB of 1.0 GB)", page.Find(".airlock-progress__line").TextContent);
            var execute = page.Find(".airlock-execute button");
            Assert.Equal("Vetting Sequence Active...", execute.TextContent.Trim());
            Assert.True(execute.HasAttribute("disabled"));
        });
    }

    [Fact]
    public async Task One_notice_at_a_time_dismissed_by_its_button_and_cleared_by_execute()
    {
        await using var ui = await ReadyAsync();
        var page = ui.Render<ControlCenter>();
        page.Find(".airlock-execute button").Click();
        Submitted(ui);
        await ui.AnswerAsync(503, detail: "NVD API key not configured");
        await ui.UntilAsync(() => Assert.Single(page.FindAll(".airlock-notice")));

        page.Find("button[aria-label='Dismiss notice']").Click();
        await ui.UntilAsync(() => Assert.Empty(page.FindAll(".airlock-notice")));
        page.Find(".airlock-execute button").Click();
        await ui.AnswerAsync(400, detail: "Target path does not exist");
        await ui.UntilAsync(() => Assert.Equal("Target path does not exist", page.Find(".airlock-notice__text").TextContent.Trim()));
        page.Find(".airlock-execute button").Click();

        await ui.UntilAsync(() => Assert.Empty(page.FindAll(".airlock-notice")));
        Assert.Equal(3, ui.Module.Invocations["submitScan"].Count);
    }

    private static async Task<UiTestContext> ReadyAsync(Action<Dictionary<string, string?>>? configure = null)
    {
        var ui = new UiTestContext(configure);
        ui.PlanPage();
        ui.PlanSubmissions();
        await ui.StartSessionAsync();
        return ui;
    }

    private static ScanSubmissionTarget Submitted(UiTestContext ui)
    {
        var invocation = Assert.Single(ui.Module.Invocations["submitScan"]);
        return Assert.IsType<ScanSubmissionTarget>(invocation.Arguments[0]);
    }
}