using Airlock.Domain.Intelligence;
using Airlock.Web.Components.Layout;
using Airlock.Web.Components.Pages;
using Airlock.Web.Tests.Support;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace Airlock.Web.Tests.Quality;

// U5-NFR-07: every button has a name, the grid's rows are focusable and say whether they are selected, the announcer
// repeats notices for screen readers, and external text is shown as text (U5-NFR-08).
public sealed class UiAccessibilityTests
{
    [Fact]
    public async Task Every_button_of_control_center_has_a_name()
    {
        await using var ui = new UiTestContext();
        ui.PlanPage();
        ui.PlanStart();
        var layout = ui.Render<MainLayout>(parameters => parameters.Add(page => page.Body, Page<ControlCenter>()));
        await ui.StageAsync(("1", "requirements.txt", 56));

        await ui.UntilAsync(() =>
        {
            Assert.Single(layout.FindAll(".airlock-staged__item"));
            Assert.All(layout.FindAll("button"), AssertNamed);
            Assert.Equal("button", layout.Find(".airlock-dropzone").GetAttribute("role"));
            Assert.Equal("0", layout.Find(".airlock-dropzone").GetAttribute("tabindex"));
        });
    }

    // Build and Test (axe-core): without a main landmark, each page's header was a second banner and its content sat
    // outside every landmark.
    [Fact]
    public async Task Each_page_sits_in_the_one_main_landmark()
    {
        await using var ui = new UiTestContext();
        ui.PlanPage();
        ui.PlanStart();
        var layout = ui.Render<MainLayout>(parameters => parameters.Add(page => page.Body, Page<ControlCenter>()));

        var main = Assert.Single(layout.FindAll("main"));
        Assert.NotNull(main.QuerySelector("h1"));
        Assert.Null(main.QuerySelector(".mud-appbar"));
    }

    [Fact]
    public async Task Every_button_of_reports_has_a_name_and_rows_say_whether_they_are_selected()
    {
        await using var ui = new UiTestContext();
        var scan = ui.Registered(Scans.Finished(
            12,
            [Scans.Vulnerable("pyyaml", "5.3", Scans.FindingOf("CVE-2020-1747", 9.8m, Severity.Critical))],
            [.. Enumerable.Range(1, 6).Select(i => Scans.Unverified($"package-{i}"))]));
        var page = ui.Render<Reports>(parameters => parameters.Add(reports => reports.Id, scan.Id.Value));

        page.FindAll(".airlock-grid__row")[0].Click();

        Assert.All(page.FindAll("button"), AssertNamed);
        var selected = page.FindAll(".airlock-grid__row").Select(row => row.GetAttribute("aria-selected")).ToList();
        Assert.Equal(7, selected.Count);
        Assert.Equal("true", selected[0]);
        Assert.All(selected.Skip(1), value => Assert.Equal("false", value));
        Assert.All(page.FindAll(".airlock-grid__row"), row => Assert.Equal("0", row.GetAttribute("tabindex")));
        Assert.Equal("Filter by package or CVE", page.Find(".airlock-grid__filter").GetAttribute("aria-label"));
    }

    [Fact]
    public async Task The_announcer_repeats_each_notice()
    {
        await using var ui = new UiTestContext();
        ui.PlanSubmissions();
        await ui.StartSessionAsync();
        var layout = ui.Render<MainLayout>(parameters => parameters.Add(page => page.Body, (RenderFragment)(body => body.AddContent(0, "The page"))));

        var submitted = ui.Session.ExecuteAsync();
        await ui.AnswerAsync(409, detail: "A scan is already in progress");
        await submitted.Within();

        await ui.UntilAsync(() =>
        {
            var announcer = layout.Find("[aria-live='polite']");
            Assert.Equal("status", announcer.GetAttribute("role"));
            Assert.Equal("A scan is already in progress", announcer.TextContent.Trim());
        });
    }

    [Fact]
    public async Task External_text_is_shown_as_text()
    {
        const string Markup = "<script>alert('airlock')</script><img src=x onerror=alert(1)>";
        await using var ui = new UiTestContext();
        ui.PlanPage();
        await ui.StartSessionAsync();
        var scan = ui.Registered(Scans.Finished(1, [Scans.Vulnerable("left-pad", "1.0.0", Scans.FindingOf("CVE-2026-0001", 9.8m, Severity.Critical, Markup))]));
        var page = ui.Render<Reports>(parameters => parameters.Add(reports => reports.Id, scan.Id.Value));
        page.FindAll(".airlock-grid__row")[0].Click();
        var terminal = ui.Render<ActiveLogs>();
        ui.LiveLog.Write(Markup);
        await ui.StageAsync(("1", Markup, 1));

        await ui.UntilAsync(() => Assert.Single(terminal.FindAll(".airlock-terminal__line")));
        var controlCenter = ui.Render<ControlCenter>();
        Assert.Empty(page.FindAll("script, img"));
        Assert.Empty(terminal.FindAll("script, img"));
        Assert.Empty(controlCenter.FindAll("script, img"));
        Assert.Equal("Remove " + Markup, controlCenter.Find(".airlock-staged__item button").GetAttribute("aria-label"));
        Assert.Equal($"\"{Markup}\"", page.Find(".airlock-finding__description").TextContent);
        Assert.Equal(Markup, terminal.Find(".airlock-terminal__message").TextContent);
        Assert.Equal(Markup, controlCenter.Find(".airlock-staged__name").TextContent);
    }

    private static RenderFragment Page<TPage>()
        where TPage : IComponent => body =>
        {
            body.OpenComponent<TPage>(0);
            body.CloseComponent();
        };

    private static void AssertNamed(IElement button)
    {
        var name = button.GetAttribute("aria-label") ?? button.TextContent.Trim();
        Assert.False(string.IsNullOrWhiteSpace(name), $"A button without a name: {button.OuterHtml}");
    }
}