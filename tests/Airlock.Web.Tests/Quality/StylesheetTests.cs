using Airlock.Web.Tests.Ui;

namespace Airlock.Web.Tests.Quality;

// Build and Test: what the browser showed wrong and bUnit cannot, since it draws no styles. Where Airlock overrides
// MudBlazor, a MudBlazor selector of higher specificity won, so those declarations are !important; axe-core and a check of
// every override against the computed styles found them (U5-NFR-04, U5-NFR-07).
public sealed class StylesheetTests
{
    private static readonly string Css = File.ReadAllText(Path.Combine(Repository.Root, "src", "Airlock.Web", "wwwroot", "css", "airlock.css"));

    [Theory]
    // MudBlazor's sticky header set max-height: 100%, so the grid grew to all its rows and Virtualize drew only 120.
    [InlineData(".airlock-grid__table .mud-table-container", "max-height: 60vh !important;")]
    // The active item kept MudBlazor's #3B82F6 on its hover tint: 4.25:1, where text needs 4.5:1.
    [InlineData(".airlock-nav .mud-nav-link.active:not(.mud-nav-link-disabled)", "color: var(--airlock-primary-text) !important;")]
    [InlineData(".airlock-nav .mud-nav-link.active:not(.mud-nav-link-disabled)", "background-color: #2A2A2C !important;")]
    // The snackbar's link kept the page's text colour on the warning fill: 1.66:1.
    [InlineData(".mud-snackbar a", "color: inherit;")]
    [InlineData(".mud-snackbar a", "text-decoration: underline;")]
    // Dimming the whole card took the hint that says why the path is not sent below 4.5:1.
    [InlineData(".airlock-path.is-dimmed .mud-input-control-input-container", "opacity: 0.6;")]
    public void The_rule_holds_what_the_browser_showed_it_needs(string selector, string declaration) =>
        Assert.Contains(declaration, Rule(selector), StringComparison.Ordinal);

    [Fact]
    public void Staged_artifacts_dim_the_field_not_the_whole_card() =>
        Assert.DoesNotContain(".airlock-path.is-dimmed {", Css, StringComparison.Ordinal);

    private static string Rule(string selector)
    {
        var start = Css.IndexOf(selector + " {", StringComparison.Ordinal);
        Assert.True(start >= 0, $"the rule for {selector}");
        return Css[start..Css.IndexOf('}', start)];
    }
}
