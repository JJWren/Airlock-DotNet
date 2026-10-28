using System.Globalization;
using Airlock.Domain.Intelligence;
using Airlock.Web.Components.Pages;
using Airlock.Web.State;
using Airlock.Web.Tests.Support;
using Bunit;

namespace Airlock.Web.Tests.Ui;

// UR-34 to UR-38, U5-NFR-09: times, durations and sizes in the invariant culture and the browser's zone; badges that
// carry their text; keyboard reach; the motion the original intended (defect 17) and its reduced-motion switch.
public sealed class PresentationTests
{
    private static readonly TimeZoneInfo Berlin = UiFormat.ResolveZone("Europe/Berlin");
    private static readonly TimeZoneInfo Phoenix = UiFormat.ResolveZone("America/Phoenix");

    [Theory]
    [InlineData("2026-10-22T12:01:32Z", "Europe/Berlin", "14:01:32", "2026-10-22 14:01:32 UTC+02:00")]
    [InlineData("2026-10-25T00:30:00Z", "Europe/Berlin", "02:30:00", "2026-10-25 02:30:00 UTC+02:00")]
    [InlineData("2026-10-25T01:30:00Z", "Europe/Berlin", "02:30:00", "2026-10-25 02:30:00 UTC+01:00")]
    [InlineData("2026-07-01T12:00:00Z", "America/Phoenix", "05:00:00", "2026-07-01 05:00:00 UTC-07:00")]
    [InlineData("2026-12-01T12:00:00Z", "America/Phoenix", "05:00:00", "2026-12-01 05:00:00 UTC-07:00")]
    public void Times_show_in_the_browsers_zone_whatever_the_servers_culture(string utc, string zone, string clock, string timestamp)
    {
        var time = DateTimeOffset.Parse(utc, CultureInfo.InvariantCulture);
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            Assert.Equal(clock, UiFormat.Clock(time, UiFormat.ResolveZone(zone)));
            Assert.Equal(timestamp, UiFormat.Timestamp(time, UiFormat.ResolveZone(zone)));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Times_read_a_dash_until_the_zone_is_known()
    {
        Assert.Equal("—", UiFormat.Clock(Test.StartTime, null));
        Assert.Equal("—", UiFormat.Timestamp(Test.StartTime, null));
    }

    [Theory]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("")]
    [InlineData(null)]
    public void A_zone_dotnet_does_not_know_falls_back_to_utc(string? name)
    {
        Assert.Equal(TimeZoneInfo.Utc, UiFormat.ResolveZone(name));
        Assert.Equal("2026-10-22 12:00:00 UTC+00:00", UiFormat.Timestamp(Test.StartTime, UiFormat.ResolveZone(name)));
    }

    [Fact]
    public void The_named_zones_resolve_on_this_system()
    {
        Assert.NotEqual(TimeZoneInfo.Utc, Berlin);
        Assert.NotEqual(TimeZoneInfo.Utc, Phoenix);
    }

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(56L, "56 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(12_698L, "12.4 KB")]
    [InlineData(1_048_575L, "1.0 MB")]
    [InlineData(450L << 20, "450.0 MB")]
    [InlineData(1L << 30, "1.0 GB")]
    [InlineData(5L << 40, "5.0 TB")]
    public void Sizes_use_powers_of_1024_with_one_decimal_above_bytes(long bytes, string expected)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            Assert.Equal(expected, UiFormat.Size(bytes));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData(-5.0, "00:00")]
    [InlineData(0.0, "00:00")]
    [InlineData(92.418, "01:32")]
    [InlineData(3599.9, "59:59")]
    [InlineData(3723.0, "1:02:03")]
    [InlineData(36_000.0, "10:00:00")]
    public void Durations_on_the_timer(double seconds, string expected) =>
        Assert.Equal(expected, UiFormat.Elapsed(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Numbers_are_written_alike_in_every_culture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            Assert.Equal("92.42", UiFormat.Seconds(TimeSpan.FromMilliseconds(92_418)));
            Assert.Equal("9.8", UiFormat.Score(9.8m));
            Assert.Equal("10.0", UiFormat.Score(10m));
            Assert.Equal("02", UiFormat.Count(2));
            Assert.Equal("123", UiFormat.Count(123));
            Assert.Equal("UTC-03:30", UiFormat.Offset(TimeSpan.FromHours(-3.5)));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task Badges_carry_their_text_and_rows_and_buttons_are_reachable_by_keyboard()
    {
        await using var ui = new UiTestContext();
        var scan = ui.Registered(WorkedExample.Finished());

        var page = ui.Render<Reports>(parameters => parameters.Add(reports => reports.Id, scan.Id.Value));

        Assert.Equal(["CRITICAL", "HIGH", "UNVERIFIED"], page.FindAll(".airlock-grid__row .airlock-badge").Select(badge => badge.TextContent));
        Assert.All(page.FindAll(".airlock-grid__row"), row => Assert.Equal("0", row.GetAttribute("tabindex")));
        Assert.All(page.FindAll(".airlock-report__actions a[href]"), link => Assert.False(link.HasAttribute("tabindex")));
        Assert.All(page.FindAll("button"), button => Assert.False(button.GetAttribute("tabindex") == "-1"));
    }

    [Fact]
    public async Task Pages_and_the_detail_pane_carry_the_motion_the_original_intended()
    {
        // Defect 17: the animations never ran; that they run, and stop under reduced motion, is checked by hand.
        await using var ui = new UiTestContext();
        var scan = ui.Registered(WorkedExample.Finished());

        var page = ui.Render<Reports>(parameters => parameters.Add(reports => reports.Id, scan.Id.Value));
        page.FindAll(".airlock-grid__row")[0].Click();

        Assert.Single(page.FindAll(".airlock-page.airlock-fade"));
        Assert.Single(page.FindAll(".airlock-detail.airlock-slide"));
        Assert.Single(page.FindAll(".airlock-stat--critical .airlock-pulse"));
    }

    [Fact]
    public void The_stylesheet_switches_the_motion_off_under_reduced_motion()
    {
        var css = File.ReadAllText(Path.Combine(Repository.Root, "src", "Airlock.Web", "wwwroot", "css", "airlock.css"));
        var start = css.IndexOf("@media (prefers-reduced-motion: reduce)", StringComparison.Ordinal);
        Assert.True(start >= 0, "the reduced-motion rule");
        var rule = css[start..css.IndexOf('}', css.IndexOf('}', start) + 1)];

        foreach (var selector in new[] { ".airlock-fade", ".airlock-slide", ".airlock-pulse", ".airlock-pill__dot" })
        {
            Assert.Contains(selector, rule, StringComparison.Ordinal);
        }

        Assert.Contains("animation: none !important", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void The_worked_example_while_it_runs_reads_correlating_two_of_four()
    {
        Assert.Equal("Correlating 2 of 4", UiFormat.Phase(WorkedExample.Running()));
        Assert.Equal("SBOM Generation", UiFormat.Phase(Scans.Running()));
        Assert.Equal("Summary", UiFormat.Phase(WorkedExample.Running().EnterPhase(Domain.Scanning.ScanPhase.Summary)));
    }

    [Fact]
    public void Severities_and_statuses_are_written_in_capitals()
    {
        Assert.Equal("CRITICAL", UiFormat.Severity(Severity.Critical));
        Assert.Equal("INCOMPLETE", UiFormat.Status(Domain.Scanning.ScanStatus.Incomplete));
    }
}

/// <summary>The repository's root, for checks of files that ship as they are written.</summary>
internal static class Repository
{
    public static string Root { get; } = Find();

    private static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Airlock.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root was not found above " + AppContext.BaseDirectory);
    }
}