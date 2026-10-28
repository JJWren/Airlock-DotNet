using Airlock.Application.Ports;
using Airlock.Web.Components.Parts;
using Airlock.Web.State;
using Airlock.Web.Tests.Support;
using Bunit;

namespace Airlock.Web.Tests.Ui;

// UR-04, UR-05: LOADING, ONLINE and DEGRADED from the health read; OFFLINE is the page script's (checked by hand).
public sealed class StatusPillTests
{
    [Fact]
    public async Task The_pill_reads_loading_until_the_first_health_answer()
    {
        await using var ui = new UiTestContext();
        ui.RenderProviders();

        var pill = ui.Render<StatusPill>();

        Assert.Equal("System Status: LOADING", Text(pill));
        Assert.Contains("airlock-pill--loading", pill.Find(".airlock-pill").ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_pill_reads_online_when_every_check_passes()
    {
        await using var ui = new UiTestContext();
        ui.RenderProviders();
        await ui.StartSessionAsync();

        var pill = ui.Render<StatusPill>();

        Assert.Equal("System Status: ONLINE", Text(pill));
        Assert.Empty(pill.FindAll(".airlock-pill .visually-hidden"));
    }

    [Fact]
    public async Task Degraded_lists_each_failing_detail_for_screen_readers_and_in_a_tooltip_focus_opens()
    {
        await using var ui = new UiTestContext();
        var (popovers, _, _) = ui.RenderProviders();
        ui.Catalog.IsConfigured = false;
        ui.Sbom.Status = new SbomToolStatus(false, null, "Syft not found at /opt/airlock/bin/syft");
        await ui.StartSessionAsync();

        var pill = ui.Render<StatusPill>();
        Assert.Equal("System Status: DEGRADED", Text(pill));
        Assert.Equal(": Syft not found at /opt/airlock/bin/syft; NVD API key not configured", pill.Find(".airlock-pill .visually-hidden").TextContent.Trim());
        Assert.Equal("0", pill.Find(".airlock-pill").GetAttribute("tabindex"));
        pill.Find(".mud-tooltip-root").FocusIn();

        popovers.WaitForAssertion(() =>
        {
            Assert.Contains("Syft not found at /opt/airlock/bin/syft", popovers.Markup, StringComparison.Ordinal);
            Assert.Contains("NVD API key not configured", popovers.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Health_is_read_again_after_ten_seconds()
    {
        await using var ui = new UiTestContext();
        ui.RenderProviders();
        await ui.StartSessionAsync();
        var pill = ui.Render<StatusPill>();
        Assert.Equal("System Status: ONLINE", Text(pill));

        ui.Catalog.IsConfigured = false;
        ui.Clock.Advance(ScanSession.HealthInterval);

        await ui.UntilAsync(() => Assert.Equal("System Status: DEGRADED", Text(pill)));
    }

    // The pill holds both labels; the stylesheet shows OFFLINE instead while the connection is lost.
    private static string Text(IRenderedComponent<StatusPill> pill) =>
        "System Status: " + pill.Find(".airlock-pill__state").TextContent;
}