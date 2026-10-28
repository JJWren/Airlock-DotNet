using System.Text.RegularExpressions;
using Airlock.Application.UseCases;
using Airlock.Domain.Intelligence;
using Airlock.Domain.Results;
using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;
using Airlock.Web.Components.Pages;
using Airlock.Web.Interop;
using Airlock.Web.State;
using Airlock.Web.Tests.Support;
using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;

namespace Airlock.Web.Tests.Ui;

// UR-22 to UR-33: which scan Reports shows and how, the analysis of the worked example, the grid, the detail pane,
// copying, the exports and Rescan; with the regressions for defects 1 and 2.
public sealed partial class ReportsTests
{
    private static readonly string[] ArtifactNames = ["requirements.txt", "app-image.tar", "notes.docx"];

    [Fact]
    public async Task Without_a_current_scan_reports_has_no_audit_data()
    {
        await using var ui = new UiTestContext();

        var page = ui.Render<Reports>();

        Assert.Contains("No audit data available. Execute a scan to begin.", page.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("01a1637b-7e00-7c3a-9f12-000000000000")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task An_id_no_scan_answers_to_is_not_found(string id)
    {
        await using var ui = new UiTestContext();

        var page = ui.Render<Reports>(parameters => parameters.Add(reports => reports.Id, Guid.Parse(id)));

        Assert.Equal("Scan not found or no longer retained", page.Find("h1").TextContent);
        Assert.Equal("/", page.FindAll("a").Single(link => link.TextContent.Trim() == "Back to Control Center").GetAttribute("href"));
    }

    [Fact]
    public async Task A_running_scan_shows_the_spinner_and_the_progress()
    {
        await using var ui = await ReadyAsync();
        var directory = ui.Directory("acme-app");
        ui.Sbom.BlocksUntilCancelled(directory);
        var scan = await ui.StartScanAsync(directory);

        var page = ui.Render<Reports>(parameters => parameters.Add(reports => reports.Id, scan.Id.Value));

        await ui.UntilAsync(() =>
        {
            Assert.Equal("Vetting In Progress", page.Find("h1").TextContent);
            Assert.Contains("Building Security Matrix...", page.Markup, StringComparison.Ordinal);
            Assert.Equal("SBOM Generation", page.Find(".airlock-progress__line").TextContent);
        });
    }

    [Fact]
    public async Task A_reservation_shows_the_spinner_at_reports()
    {
        await using var ui = await ReadyAsync();
        var page = ui.Render<Reports>();

        var reserved = await ui.Services.GetRequiredService<StartScan>().ReserveAsync(Test.Token);
        await using var reservation = reserved.Value!;
        using var body = new MemoryStream(A.Bytes(A.RequirementsText));
        await reservation.AddArtifactAsync("requirements.txt", body, Test.Token);

        await ui.UntilAsync(() =>
        {
            Assert.Equal("Vetting In Progress", page.Find("h1").TextContent);
            Assert.Equal("Receiving artifacts…", page.Find(".airlock-progress__line").TextContent);
        });
    }

    [Fact]
    public async Task The_worked_example_header_cards_banner_and_grid()
    {
        await using var ui = await ReadyAsync();
        var scan = ui.Registered(WorkedExample.Finished());

        var page = Show(ui, scan);

        Assert.Equal("Post-Scan Analysis", page.Find("h1").TextContent);
        Assert.Equal("INCOMPLETE", page.Find(".airlock-report__title-row .airlock-badge").TextContent);
        Assert.Equal("Target: requirements.txt", page.Find(".airlock-report__heading .airlock-subtitle").TextContent);
        Assert.Equal("2026-10-22 14:01:32 UTC+02:00 // SCAN TIME: 92.42S", page.Find(".airlock-report__time").TextContent);
        Assert.Equal(
            ["Total Packages 4", "Critical Findings 2", "High Findings 3", "Clean Packages 1"],
            page.FindAll(".airlock-stat").Select(card => Normalized(card.TextContent)));
        Assert.Single(page.FindAll(".airlock-stat--critical .airlock-pulse"));
        Assert.Equal("INCOMPLETE — 1 package could not be verified and is not counted as clean", page.Find(".airlock-banner__summary").TextContent);
        Assert.Equal(["jinja2 2.10 — lookup failed: NVD returned 503"], page.FindAll(".airlock-banner__list li").Select(item => item.TextContent));
        Assert.Empty(page.FindAll(".airlock-warnings"));
        Assert.Equal(
            ["CRITICAL pyyaml 5.3 CPE match 02", "HIGH flask 0.12.2 Product match 03", "UNVERIFIED jinja2 2.10"],
            Rows(page));
    }

    [Fact]
    public async Task The_cards_show_no_pulse_without_critical_findings()
    {
        await using var ui = new UiTestContext();
        var scan = ui.Registered(Scans.Finished(5, [Scans.Vulnerable("flask", "0.12.2", Scans.FindingOf("CVE-2018-1000656", 7.5m, Severity.High))]));

        var page = Show(ui, scan);

        Assert.Empty(page.FindAll(".airlock-pulse"));
        Assert.Equal("COMPLETED", page.Find(".airlock-report__title-row .airlock-badge").TextContent);
    }

    [Theory]
    [InlineData(1, "Target: requirements.txt")]
    [InlineData(2, "Target: requirements.txt and 1 more artifact")]
    [InlineData(3, "Target: requirements.txt and 2 more artifacts")]
    public async Task The_target_names_the_first_artifact_and_how_many_more(int count, string expected)
    {
        await using var ui = new UiTestContext();
        var scan = ui.Registered(Scans.Finished(0, [], target: Scans.Artifacts([.. ArtifactNames.Take(count)])));

        var page = Show(ui, scan);

        Assert.Equal(expected, page.Find(".airlock-report__heading .airlock-subtitle").TextContent);
    }

    [Fact]
    public async Task Times_wait_for_the_browsers_zone()
    {
        await using var ui = new UiTestContext();
        var scan = ui.Registered(WorkedExample.Finished());

        var page = Show(ui, scan);

        Assert.Equal("— // SCAN TIME: 92.42S", page.Find(".airlock-report__time").TextContent);
    }

    [Fact]
    public async Task A_failed_scan_shows_its_reason_without_cards_or_grid()
    {
        await using var ui = new UiTestContext();
        var scan = ui.Registered(Scans.Failed());

        var page = Show(ui, scan);

        Assert.Equal("FAILED", page.Find(".airlock-report__title-row .airlock-badge").TextContent);
        Assert.Equal("Scan failed: Syft failed for /uploads/acme-app", page.Find(".airlock-report__reason").TextContent);
        Assert.Empty(page.FindAll(".airlock-cards"));
        Assert.Empty(page.FindAll(".airlock-grid"));
        Assert.Equal("true", Export(page, "Export PDF").GetAttribute("aria-disabled"));
        Assert.Null(Export(page, "Export PDF").GetAttribute("href"));
        Assert.Equal("/api/v2/scans/" + scan.Id, Export(page, "Export JSON").GetAttribute("href"));
    }

    [Fact]
    public async Task A_cancelled_scan_shows_its_reason()
    {
        await using var ui = new UiTestContext();
        var scan = ui.Registered(Scans.Cancelled());

        var page = Show(ui, scan);

        Assert.Equal("CANCELLED", page.Find(".airlock-report__title-row .airlock-badge").TextContent);
        Assert.Equal("Scan cancelled: Emergency Stop", page.Find(".airlock-report__reason").TextContent);
    }

    [Fact]
    public async Task The_exports_are_the_apis_answers_saved_under_the_pdfs_name()
    {
        await using var ui = new UiTestContext();
        var scan = ui.Registered(WorkedExample.Finished());

        var page = Show(ui, scan);

        var json = Export(page, "Export JSON");
        Assert.Equal("/api/v2/scans/" + scan.Id, json.GetAttribute("href"));
        Assert.Equal("airlock_audit_2026-10-22T12-01-32-418Z.json", json.GetAttribute("download"));
        var pdf = Export(page, "Export PDF");
        Assert.Equal("/api/v2/scans/" + scan.Id + "/report.pdf", pdf.GetAttribute("href"));
        Assert.Equal("airlock_audit_2026-10-22T12-01-32-418Z.pdf", pdf.GetAttribute("download"));
    }

    [Fact]
    public async Task The_banner_lists_five_then_all()
    {
        await using var ui = new UiTestContext();
        var scan = ui.Registered(Scans.Finished(12, [], [.. Enumerable.Range(1, 12).Select(i => Scans.Unverified($"package-{i:00}"))]));
        var page = Show(ui, scan);
        Assert.Equal("INCOMPLETE — 12 packages could not be verified and are not counted as clean", page.Find(".airlock-banner__summary").TextContent);
        Assert.Equal(5, page.FindAll(".airlock-banner__list li").Count);

        page.FindAll(".airlock-banner button").Single(button => button.TextContent.Trim() == "Show all (12)").Click();
        Assert.Equal(12, page.FindAll(".airlock-banner__list li").Count);
        page.FindAll(".airlock-banner button").Single(button => button.TextContent.Trim() == "Show fewer").Click();

        Assert.Equal(5, page.FindAll(".airlock-banner__list li").Count);
    }

    [Fact]
    public async Task Warnings_are_counted_in_a_collapsed_panel()
    {
        await using var ui = new UiTestContext();
        ScanWarning[] warnings =
        [
            new(ScanWarningCode.NoPackagesIdentified, "No packages identified in notes.docx", "notes.docx"),
            new(ScanWarningCode.DistributionBackportsNotConsidered, "Distribution backports are not considered for alpine-3.20.tar", "alpine-3.20.tar"),
            new(ScanWarningCode.CvesWithoutSeverity, "2 CVEs have no severity yet"),
        ];
        var scan = ui.Registered(Scans.Finished(3, [], warnings: warnings));

        var page = Show(ui, scan);

        var panel = page.Find("details.airlock-warnings");
        Assert.False(panel.HasAttribute("open"));
        Assert.Equal("Warnings (3)", panel.QuerySelector("summary")!.TextContent);
        Assert.Equal(warnings.Select(warning => warning.Message), panel.QuerySelectorAll("li").Select(item => item.TextContent));
    }

    [Theory]
    [InlineData(4, "No HIGH or CRITICAL findings: every package is clean.")]
    [InlineData(0, "No packages identified in the scan target.")]
    public async Task A_scan_without_rows_says_why(int total, string expected)
    {
        await using var ui = new UiTestContext();
        var scan = ui.Registered(Scans.Finished(total, []));

        var page = Show(ui, scan);

        Assert.Equal(expected, page.Find(".airlock-grid .airlock-empty").TextContent.Trim());
    }

    [Fact]
    public async Task Risk_is_the_highest_severity_even_when_a_high_finding_comes_first()
    {
        // Defect 2: the original showed the first CVE's severity.
        await using var ui = new UiTestContext();
        var package = Scans.Vulnerable(
            "django", "2.2.0", Scans.FindingOf("CVE-2019-14234", 7.5m, Severity.High), Scans.FindingOf("CVE-2019-14235", 9.8m, Severity.Critical));
        var scan = ui.Registered(Scans.Finished(1, [package]));

        var page = Show(ui, scan);

        Assert.Equal(["CRITICAL django 2.2.0 CPE match 02"], Rows(page));
    }

    [Fact]
    public async Task A_product_match_is_labelled_as_one()
    {
        // Defect 1: the original's Method column always read CPE.
        await using var ui = new UiTestContext();
        var scan = ui.Registered(WorkedExample.Finished());

        var page = Show(ui, scan);

        Assert.Equal("Product match", page.FindAll(".airlock-grid__row")[1].QuerySelectorAll("td")[3].TextContent);
    }

    [Theory]
    [InlineData("cve-2020-1747", "pyyaml")]
    [InlineData("FLASK", "flask")]
    [InlineData("jinja", "jinja2")]
    public async Task The_filter_keeps_rows_by_name_or_cve_id_ignoring_case(string text, string kept)
    {
        await using var ui = new UiTestContext();
        var scan = ui.Registered(WorkedExample.Finished());
        var page = Show(ui, scan);

        page.Find(".airlock-grid__filter").Input(text);
        ui.Clock.Advance(TimeSpan.FromMilliseconds(200));

        await ui.UntilAsync(() => Assert.Equal([kept], page.FindAll(".airlock-grid__name").Select(cell => cell.TextContent)));
    }

    [Fact]
    public async Task Versions_are_not_searched()
    {
        await using var ui = new UiTestContext();
        var scan = ui.Registered(WorkedExample.Finished());
        var page = Show(ui, scan);

        page.Find(".airlock-grid__filter").Input("2.10");
        ui.Clock.Advance(TimeSpan.FromMilliseconds(200));

        await ui.UntilAsync(() => Assert.Equal("No matching assets found in project inventory.", page.Find(".airlock-grid .airlock-empty").TextContent.Trim()));
    }

    [Fact]
    public async Task The_filter_waits_for_the_last_keystroke()
    {
        await using var ui = new UiTestContext();
        var scan = ui.Registered(WorkedExample.Finished());
        var page = Show(ui, scan);

        page.Find(".airlock-grid__filter").Input("flask");
        ui.Clock.Advance(TimeSpan.FromMilliseconds(150));
        page.Find(".airlock-grid__filter").Input("pyyaml");
        ui.Clock.Advance(TimeSpan.FromMilliseconds(150));
        Assert.Equal(3, page.FindAll(".airlock-grid__row").Count);
        ui.Clock.Advance(TimeSpan.FromMilliseconds(50));

        await ui.UntilAsync(() => Assert.Equal(["pyyaml"], page.FindAll(".airlock-grid__name").Select(cell => cell.TextContent)));
    }

    [Fact]
    public async Task Two_versions_of_a_package_are_selected_apart()
    {
        await using var ui = new UiTestContext();
        var scan = ui.Registered(Scans.Finished(
            2,
            [
                Scans.Vulnerable("requests", "2.25.1", Scans.FindingOf("CVE-2023-32681", 7.5m, Severity.High)),
                Scans.Vulnerable("requests", "2.31.0", Scans.FindingOf("CVE-2024-35195", 7.5m, Severity.High)),
            ]));
        var page = Show(ui, scan);

        page.FindAll(".airlock-grid__row")[0].Click();

        Assert.Equal(["true", "false"], page.FindAll(".airlock-grid__row").Select(row => row.GetAttribute("aria-selected")));
        Assert.Equal("Active Analysis: requests 2.25.1 (python)", page.Find(".airlock-detail__package").TextContent);
        page.FindAll(".airlock-grid__row")[1].KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        Assert.Equal(["false", "true"], page.FindAll(".airlock-grid__row").Select(row => row.GetAttribute("aria-selected")));
        Assert.Equal("Active Analysis: requests 2.31.0 (python)", page.Find(".airlock-detail__package").TextContent);
        page.FindAll(".airlock-grid__row")[1].Click();
        Assert.Equal("true", page.FindAll(".airlock-grid__row")[1].GetAttribute("aria-selected"));
    }

    [Fact]
    public async Task The_filter_keeps_the_selection_while_its_row_shows_and_clears_it_otherwise()
    {
        await using var ui = new UiTestContext();
        var scan = ui.Registered(WorkedExample.Finished());
        var page = Show(ui, scan);
        page.FindAll(".airlock-grid__row")[0].Click();

        page.Find(".airlock-grid__filter").Input("py");
        ui.Clock.Advance(TimeSpan.FromMilliseconds(200));
        await ui.UntilAsync(() => Assert.Single(page.FindAll(".airlock-detail")));
        page.Find(".airlock-grid__filter").Input("flask");
        ui.Clock.Advance(TimeSpan.FromMilliseconds(200));

        await ui.UntilAsync(() => Assert.Empty(page.FindAll(".airlock-detail")));
    }

    [Fact]
    public async Task The_detail_pane_shows_the_match_each_finding_and_its_link()
    {
        await using var ui = new UiTestContext();
        var scan = ui.Registered(WorkedExample.Finished());
        var page = Show(ui, scan);

        page.FindAll(".airlock-grid__row")[0].Click();

        Assert.Equal("Vulnerability Details", page.Find(".airlock-detail h2").TextContent);
        Assert.Equal("Active Analysis: pyyaml 5.3 (python)", page.Find(".airlock-detail__package").TextContent);
        Assert.Equal("Matched by CPE: cpe:2.3:a:pyyaml:pyyaml:5.3:*:*:*:*:*:*:* (generated)", page.Find(".airlock-detail__match").TextContent);
        var findings = page.FindAll(".airlock-finding");
        Assert.Equal(["CVE-2020-14343", "CVE-2020-1747"], findings.Select(finding => finding.QuerySelector(".airlock-finding__id")!.TextContent));
        var link = findings[0].QuerySelector("a")!;
        Assert.Equal("https://nvd.nist.gov/vuln/detail/CVE-2020-14343", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
        Assert.Equal("CVSS v3.1 Score · NVD", findings[0].QuerySelector(".airlock-finding__score .airlock-label")!.TextContent);
        Assert.Equal("9.8 CRITICAL", findings[0].QuerySelector(".airlock-finding__value")!.TextContent);
        Assert.Equal("width: 98%", findings[0].QuerySelector(".airlock-scorebar__fill")!.GetAttribute("style"));
        Assert.Equal($"\"{WorkedExample.Description("CVE-2020-14343")}\"", findings[0].QuerySelector(".airlock-finding__description")!.TextContent);
    }

    [Fact]
    public async Task The_detail_pane_names_the_product_the_cvss_version_and_the_cna()
    {
        await using var ui = new UiTestContext();
        var cna = Scans.Vulnerable("lodash", "4.17.4", Scans.FindingOf("CVE-2019-10744", 9.1m, Severity.Critical, source: CvssSource.Secondary));
        var scan = ui.Registered(Scans.Finished(5, [cna]));
        var page = Show(ui, scan);

        page.FindAll(".airlock-grid__row")[0].Click();

        Assert.Equal("CVSS v3.1 Score · CNA", page.Find(".airlock-finding__score .airlock-label").TextContent);

        await using var worked = new UiTestContext();
        var example = Show(worked, worked.Registered(WorkedExample.Finished()));
        example.FindAll(".airlock-grid__row")[1].Click();
        Assert.Equal("Matched by product: flask", example.Find(".airlock-detail__match").TextContent);
        Assert.Equal(
            ["CVSS v3.0 Score · NVD", "CVSS v3.0 Score · NVD", "CVSS v3.1 Score · NVD"],
            example.FindAll(".airlock-finding__score .airlock-label").Select(label => label.TextContent));
    }

    [Fact]
    public async Task An_unverified_package_says_why_and_closing_clears_the_selection()
    {
        await using var ui = new UiTestContext();
        var scan = ui.Registered(WorkedExample.Finished());
        var page = Show(ui, scan);

        page.FindAll(".airlock-grid__row")[2].Click();
        Assert.Equal("Not verified — lookup failed: NVD returned 503", page.Find(".airlock-detail__unverified").TextContent);
        page.Find("button[aria-label='Close details']").Click();

        Assert.Empty(page.FindAll(".airlock-detail"));
        Assert.All(page.FindAll(".airlock-grid__row"), row => Assert.Equal("false", row.GetAttribute("aria-selected")));
        page.FindAll(".airlock-grid__row")[0].Click();
        page.Find(".airlock-detail").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(page.FindAll(".airlock-detail"));
    }

    [Theory]
    [InlineData(true, "Description copied")]
    [InlineData(false, "Copy failed — select the text and copy it")]
    public async Task Copy_puts_the_description_on_the_clipboard_and_says_so(bool copied, string message)
    {
        await using var ui = new UiTestContext();
        var (_, _, snackbars) = ui.RenderProviders();
        ui.Module.Setup<bool>("copyText", WorkedExample.Description("CVE-2020-14343")).SetResult(copied);
        var page = Show(ui, ui.Registered(WorkedExample.Finished()));
        page.FindAll(".airlock-grid__row")[0].Click();

        page.Find(".airlock-finding").QuerySelectorAll("button").Single(button => button.TextContent.Trim() == "COPY").Click();

        await ui.UntilAsync(() => Assert.Contains(message, snackbars.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rescan_of_a_directory_submits_its_path_and_opens_the_new_scan()
    {
        await using var ui = await ReadyAsync();
        ui.PlanSubmissions();
        var directory = ui.Directory("acme-app");
        var shown = ui.Registered(Scans.Finished(0, [], target: new DirectoryTarget(directory)));
        var navigation = ui.Services.GetRequiredService<BunitNavigationManager>();
        var page = Show(ui, shown);

        page.FindAll("button").Single(button => button.TextContent.Trim() == "Rescan Project").Click();
        await ui.UntilAsync(() => Assert.Equal("Vetting In Progress", page.Find("h1").TextContent));
        var target = Assert.IsType<ScanSubmissionTarget>(Assert.Single(ui.Module.Invocations["submitScan"]).Arguments[0]);
        Assert.Equal(ScanSubmissionTarget.ForPath(directory), target);
        var rescan = await ui.StartScanAsync(directory);
        await ui.AnswerAsync(rescan);

        await ui.UntilAsync(() => Assert.EndsWith("/reports/" + rescan.Id, navigation.Uri, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rescan_of_held_files_uploads_them_again()
    {
        await using var ui = await ReadyAsync();
        var shown = ui.Registered(WorkedExample.Finished());
        ui.Module.Setup<bool>("resubmitScan", _ => true).SetResult(true);
        var page = Show(ui, shown);

        page.FindAll("button").Single(button => button.TextContent.Trim() == "Rescan Project").Click();

        var invocation = Assert.Single(ui.Module.Invocations["resubmitScan"]);
        Assert.Equal(shown.Id.ToString(), invocation.Arguments[0]);
        await ui.UntilAsync(() => Assert.Equal("Vetting In Progress", page.Find("h1").TextContent));
    }

    [Fact]
    public async Task Rescan_after_a_reload_asks_to_reselect_the_artifacts()
    {
        await using var ui = await ReadyAsync();
        var shown = ui.Registered(Scans.Finished(0, [], target: Scans.Artifacts("requirements.txt", "app-image.tar")));
        ui.Module.Setup<bool>("resubmitScan", _ => true).SetResult(false);
        var navigation = ui.Services.GetRequiredService<BunitNavigationManager>();
        var page = Show(ui, shown);

        page.FindAll("button").Single(button => button.TextContent.Trim() == "Rescan Project").Click();

        await ui.UntilAsync(() => Assert.Equal("http://localhost/", navigation.Uri));
        Assert.Equal(new InlineNotice(NoticeKind.RescanHint, "Re-select the artifacts to rescan: requirements.txt, app-image.tar", shown.Id), ui.Session.State.Notice);
    }

    private static async Task<UiTestContext> ReadyAsync()
    {
        var ui = new UiTestContext();
        ui.PlanPage();
        await ui.StartSessionAsync();
        return ui;
    }

    private static IRenderedComponent<Reports> Show(UiTestContext ui, Scan scan) =>
        ui.Render<Reports>(parameters => parameters.Add(reports => reports.Id, scan.Id.Value));

    private static IElement Export(IRenderedComponent<Reports> page, string name) =>
        page.FindAll(".airlock-report__actions a").Single(link => link.TextContent.Trim() == name);

    private static List<string> Rows(IRenderedComponent<Reports> page) =>
        [.. page.FindAll(".airlock-grid__row").Select(row => Normalized(row.TextContent))];

    private static string Normalized(string text) => Spaces().Replace(text.Trim(), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}