using Airlock.Web.Components.Pages;
using Airlock.Web.Tests.Support;
using Bunit;
using Microsoft.Extensions.Logging;

namespace Airlock.Web.Tests.Quality;

// U5-NFR-12: routine UI activity logs nothing at Information, so the Live Log shows scans, not clicks; a failed call
// into the browser is logged at Error and the page carries on.
public sealed class UiLoggingTests
{
    [Fact]
    public async Task Following_a_scan_logs_no_ui_line_at_information()
    {
        await using var ui = new UiTestContext();
        ui.PlanPage();
        await ui.StartSessionAsync();
        var directory = ui.Directory("acme-app");
        ui.Sbom.Produces(directory, A.Sbom("flask", "0.12.2"));
        ui.Render<ControlCenter>();
        var reports = ui.Render<Reports>();

        var scan = await ui.StartScanAsync(directory);

        await ui.UntilAsync(() => Assert.Equal("Post-Scan Analysis", reports.Find("h1").TextContent));
        Assert.Equal(scan.Id, ui.Session.State.Current!.Id);
        Assert.DoesNotContain(
            ui.Logs.GetSnapshot(),
            record => record.Category!.StartsWith("Airlock.Web", StringComparison.Ordinal) && record.Level >= LogLevel.Information);
    }

    [Fact]
    public async Task A_failed_call_into_the_browser_is_logged_at_error_and_the_page_carries_on()
    {
        await using var ui = new UiTestContext();
        ui.Module.Setup<string?>("timeZone").SetException(new Microsoft.JSInterop.JSException("The page went away"));
        ui.Module.SetupVoid("watchConnection").SetVoidResult();

        await ui.Session.StartAsync();

        await ui.UntilAsync(() => Assert.Equal(TimeZoneInfo.Utc, ui.Session.State.TimeZone));
        var error = Assert.Single(ui.Logs.GetSnapshot(), record => record.Level >= LogLevel.Error);
        Assert.Equal("Airlock.Web.Interop.AirlockJs", error.Category);
        Assert.Equal("⚠️ A call into the browser failed: timeZone: The page went away", error.Message);
    }
}