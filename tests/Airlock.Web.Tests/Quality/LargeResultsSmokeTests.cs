using System.Diagnostics;
using Airlock.Web.Components.Pages;
using Airlock.Web.Tests.Support;
using Bunit;

namespace Airlock.Web.Tests.Quality;

// U5-NFR-04 — bounds ten times the targets (drawn within 1 s, filtered within 300 ms, selected within 100 ms);
// Build and Test measures the targets themselves in the browser.
[Collection(nameof(Measurements))]
public sealed class LargeResultsSmokeTests
{
    [Fact]
    public async Task Two_thousand_rows_are_drawn_filtered_and_selected_quickly()
    {
        await using var ui = new UiTestContext();
        var scan = ui.Registered(Scans.Large(2_000));
        ui.Render<Reports>(parameters => parameters.Add(reports => reports.Id, WorkedExample.Id.Value));

        var watch = Stopwatch.StartNew();
        var page = ui.Render<Reports>(parameters => parameters.Add(reports => reports.Id, scan.Id.Value));
        var drawn = watch.Elapsed;
        Assert.NotEmpty(page.FindAll(".airlock-grid__row"));

        watch.Restart();
        page.Find(".airlock-grid__filter").Input("package-1999");
        ui.Clock.Advance(TimeSpan.FromMilliseconds(200));
        page.WaitForAssertion(() => Assert.Single(page.FindAll(".airlock-grid__row")), TimeSpan.FromSeconds(10));
        var filtered = watch.Elapsed;

        watch.Restart();
        page.Find(".airlock-grid__row").Click();
        var selected = watch.Elapsed;

        Assert.Single(page.FindAll(".airlock-detail"));
        Assert.True(drawn < TimeSpan.FromSeconds(10), $"Drawing took {drawn.TotalMilliseconds} ms.");
        Assert.True(filtered < TimeSpan.FromSeconds(3), $"Filtering took {filtered.TotalMilliseconds} ms.");
        Assert.True(selected < TimeSpan.FromSeconds(1), $"Selecting took {selected.TotalMilliseconds} ms.");
    }
}