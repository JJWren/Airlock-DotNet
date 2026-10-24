using Airlock.Domain.Intelligence;
using Airlock.Domain.Results;
using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;
using Airlock.Infrastructure.Reports;
using Airlock.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using UglyToad.PdfPig;

namespace Airlock.Infrastructure.Tests.Reports;

// IR-20, IR-21 — the PDF Scan Report, read back with PdfPig: every section, for Completed and Incomplete scans.
public sealed class ScanReportRendererTests
{
    private static readonly DateTimeOffset RenderedAt = new(2026, 10, 22, 12, 15, 3, TimeSpan.Zero);

    private readonly QuestPdfScanReportRenderer renderer = new(new FakeTimeProvider(RenderedAt), new FakeLogger<QuestPdfScanReportRenderer>());

    [Fact]
    public async Task An_Incomplete_scans_report_has_every_section_of_the_layout()
    {
        var scan = A.IncompleteScan();

        var report = await RenderAsync(scan);

        foreach (var expected in new[]
        {
            ScanReportDocument.Title, $"Scan {scan.Id}", "/uploads/acme-app", "Incomplete",
            "Started 2026-10-22 12:00:00 UTC", "Finished 2026-10-22 12:11:42 UTC", "Duration 11:42",
            "Summary", "Total", "Vulnerable", "Clean", "Unverified", "Critical findings", "High findings",
            "Findings", "pyyaml", "5.3", "CPE match cpe:2.3:a:pyyaml:pyyaml:5.3", "CVE-2020-14343", "CRITICAL", "9.8 (CVSS v3.1)",
            "A vulnerability was discovered in the PyYAML library.", "Product match minimist",
            "Unverified packages", "failing-2", "NVD returned 503",
            "Warnings", "busybox 1.36.1-r31: CVE-2025-60876 not yet scored by NVD",
            "Page 1 of", "Rendered 2026-10-22 12:15:03 UTC",
        })
        {
            Assert.True(report.Contains(expected), $"The report lacks \"{expected}\" in: {report.Squeezed}");
        }

        Assert.Contains("https://nvd.nist.gov/vuln/detail/CVE-2020-14343", report.Links);
        Assert.Equal(ScanReportDocument.Title, report.Title);
    }

    [Fact]
    public async Task A_Completed_scans_report_says_there_is_nothing_unverified_and_no_warning()
    {
        var report = await RenderAsync(A.CompletedScan());

        foreach (var expected in new[] { "Completed", "None: every package's lookups completed.", "HIGH", "7.5 (CVSS v3.1)" })
        {
            Assert.True(report.Contains(expected), $"The report lacks \"{expected}\" in: {report.Squeezed}");
        }
    }

    [Fact]
    public async Task A_failed_scans_report_gives_the_reason_and_its_input()
    {
        var scan = Scan.Start(ScanId.New(), new DirectoryTarget("/uploads/acme-app"), Test.StartTime)
            .Fail(new ScanFailure(ScanFailureCode.SyftFailed, "Syft exited with code 1: could not determine source", "app.tar"), Test.StartTime.AddMinutes(1));

        var report = await RenderAsync(scan);

        Assert.True(report.Contains("The scan failed: Syft exited with code 1: could not determine source (app.tar)"));
        Assert.True(report.Contains("No findings."));
    }

    [Fact]
    public async Task A_cancelled_scans_report_says_why_it_has_no_results()
    {
        var scan = Scan.Start(ScanId.New(), new ArtifactsTarget([new Artifact("requirements.txt", 10), new Artifact("app.tar", 20)]), Test.StartTime)
            .Cancel("Emergency Stop", Test.StartTime.AddMinutes(2));

        var report = await RenderAsync(scan);

        Assert.True(report.Contains("The scan has no results: Emergency Stop"));
        Assert.True(report.Contains("requirements.txt, app.tar"));
    }

    [Fact]
    public async Task A_character_the_font_lacks_does_not_fail_the_report()
    {
        var unusual = "Affects 日本語 users " + char.ConvertFromUtf32(0x1F600) + " in a long description.";
        var scan = A.FinishedScan(
            1, [A.Vulnerable(A.Package(), new ProductMatch("pyyaml"), A.Cve("CVE-2026-0001", 9.8m, Severity.Critical, unusual))], []);

        var report = await RenderAsync(scan);

        Assert.True(report.Contains("CVE-2026-0001"));
    }

    [Fact]
    public async Task A_cancelled_request_renders_nothing()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => renderer.RenderPdfAsync(A.CompletedScan(), cancelled.Token).AsTask());
    }

    [Theory]
    [InlineData(42, "00:42")]
    [InlineData(702, "11:42")]
    [InlineData(3_600, "1:00:00")]
    [InlineData(5_025, "1:23:45")]
    public void Durations_read_as_minutes_and_seconds_with_hours_past_an_hour(int seconds, string text)
    {
        Assert.Equal(text, ScanReportDocument.Duration(TimeSpan.FromSeconds(seconds)));
    }

    private async Task<RenderedReport> RenderAsync(Scan scan)
    {
        var pdf = await renderer.RenderPdfAsync(scan, Test.Token).Within(60);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5), StringComparison.Ordinal);
        using var document = PdfDocument.Open(pdf);
        var pages = document.GetPages().ToList();
        return new RenderedReport(
            string.Concat(pages.SelectMany(page => page.Letters).Select(letter => letter.Value).Where(text => !string.IsNullOrWhiteSpace(text))),
            [.. pages.SelectMany(page => page.GetHyperlinks()).Select(link => link.Uri)],
            document.Information.Title);
    }

    /// <summary>
    /// The report's letters in the order they were drawn — cell by cell, unlike words read line by line across the
    /// table — run together, so a cell's line breaks never hide a phrase; and its links.
    /// </summary>
    private sealed record RenderedReport(string Squeezed, IReadOnlyList<string> Links, string? Title)
    {
        public bool Contains(string phrase) => Squeezed.Contains(phrase.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }
}
