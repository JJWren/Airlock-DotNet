using System.Globalization;
using Airlock.Domain.Results;
using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Airlock.Infrastructure.Reports;

/// <summary>
/// The PDF Scan Report (IR-20): title and scan details, summary, findings, Unverified packages, warnings, and a footer on
/// every page. Severity is always written out, never shown by colour alone.
/// </summary>
internal sealed class ScanReportDocument(Scan scan, DateTimeOffset renderedAt) : IDocument
{
    public const string Title = "Airlock Scan Report";

    /// <summary>The font QuestPDF ships with; the machine's own fonts are not used (IR-21).</summary>
    private const string FontFamily = "Lato";

    private const int MaxTargetLength = 300;

    private static readonly string[] SummaryTitles =
        ["Total", "Vulnerable", "Clean", "Unverified", "Critical findings", "High findings"];

    private static readonly string[] FindingTitles = ["Package", "Version", "Method", "CVE", "Severity", "Score", "Description"];

    private static readonly string[] UnverifiedTitles = ["Package", "Version", "Reason"];

    public DocumentMetadata GetMetadata() => new()
    {
        Title = Title,
        Author = "Airlock",
        Subject = $"Scan {scan.Id}",
        CreationDate = renderedAt,
        ModifiedDate = renderedAt,
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(style => style.FontFamily(FontFamily).FontSize(9));
            page.Header().Element(Header);
            page.Content().PaddingVertical(12).Column(column =>
            {
                column.Spacing(16);
                column.Item().Element(Summary);
                column.Item().Element(Findings);
                column.Item().Element(Unverified);
                column.Item().Element(Warnings);
            });
            page.Footer().AlignRight().Text(text =>
            {
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
                text.Span(" · Rendered " + Utc(renderedAt));
            });
        });
    }

    internal static string Utc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";

    internal static string Duration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : duration.ToString(@"mm\:ss", CultureInfo.InvariantCulture);

    internal static string Method(MatchMethod method) => method switch
    {
        CpeMatch cpe => "CPE match " + cpe.Cpe.Value,
        ProductMatch product => "Product match " + product.Product,
        _ => "—",
    };

    private static IContainer HeaderCell(IContainer cell) =>
        cell.BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingVertical(3).PaddingRight(4);

    private static IContainer Cell(IContainer cell) =>
        cell.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).PaddingRight(4);

    private static void Section(ColumnDescriptor column, string title) =>
        column.Item().PaddingBottom(4).Text(title).FontSize(12).Bold();

    private void Header(IContainer header)
    {
        header.Column(column =>
        {
            column.Item().Text(Title).FontSize(18).Bold();
            column.Item().Text($"Scan {scan.Id} · {Target()} · {scan.Status}");
            var finished = scan.FinishedAt is { } at ? " · Finished " + Utc(at) : string.Empty;
            var duration = scan.Duration is { } length ? " · Duration " + Duration(length) : string.Empty;
            column.Item().Text("Started " + Utc(scan.StartedAt) + finished + duration);
        });
    }

    private string Target()
    {
        var target = scan.Target switch
        {
            DirectoryTarget directory => directory.Path,
            ArtifactsTarget artifacts => string.Join(", ", artifacts.Artifacts.Select(artifact => artifact.FileName)),
            _ => string.Empty,
        };
        return target.Length > MaxTargetLength ? target[..(MaxTargetLength - 1)] + "…" : target;
    }

    private void Summary(IContainer container)
    {
        container.Column(column =>
        {
            Section(column, "Summary");
            if (scan.Summary is not { } summary)
            {
                column.Item().Text(scan.Failure is { } failure
                    ? $"The scan failed: {failure.Reason}" + (failure.Input is null ? string.Empty : $" ({failure.Input})")
                    : $"The scan has no results: {scan.CancellationReason ?? scan.Status.ToString()}");
                return;
            }

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    for (var i = 0; i < 6; i++)
                    {
                        columns.RelativeColumn();
                    }
                });
                foreach (var title in SummaryTitles)
                {
                    table.Cell().Element(HeaderCell).Text(title).Bold();
                }

                foreach (var count in new[]
                {
                    summary.TotalPackages, summary.VulnerablePackages, summary.CleanPackages, summary.UnverifiedPackages,
                    summary.CriticalFindings, summary.HighFindings,
                })
                {
                    table.Cell().Element(Cell).Text(count.ToString(CultureInfo.InvariantCulture));
                }
            });
        });
    }

    private void Findings(IContainer container)
    {
        container.Column(column =>
        {
            Section(column, "Findings");
            if (scan.VulnerablePackages.Count == 0)
            {
                column.Item().Text("No findings.");
                return;
            }

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(1.2f);
                    columns.RelativeColumn(2.6f);
                    // Build and Test: wide enough for a 16-character id such as CVE-2018-1000656, which 2.2 broke in two.
                    columns.RelativeColumn(2.8f);
                    columns.RelativeColumn(1.4f);
                    columns.RelativeColumn(1.6f);
                    columns.RelativeColumn(4.4f);
                });
                table.Header(row =>
                {
                    foreach (var title in FindingTitles)
                    {
                        row.Cell().Element(HeaderCell).Text(title).Bold();
                    }
                });
                foreach (var vulnerable in scan.VulnerablePackages)
                {
                    foreach (var finding in vulnerable.Findings)
                    {
                        table.Cell().Element(Cell).Text(vulnerable.Package.Name);
                        table.Cell().Element(Cell).Text(vulnerable.Package.Version);
                        table.Cell().Element(Cell).Text(Method(vulnerable.Method));
                        table.Cell().Element(Cell).Column(cve =>
                        {
                            var link = finding.Cve.NvdUrl.ToString();
                            cve.Item().Hyperlink(link).Text(finding.Cve.Id).FontColor(Colors.Blue.Darken2).Underline();
                            cve.Item().Text(link).FontSize(7).FontColor(Colors.Grey.Darken2);
                        });
                        table.Cell().Element(Cell).Text(finding.Severity.ToString().ToUpperInvariant()).Bold();
                        table.Cell().Element(Cell).Text(
                            string.Create(CultureInfo.InvariantCulture, $"{finding.Score.BaseScore:0.0} ({finding.Score.VersionLabel})"));
                        table.Cell().Element(Cell).Text(finding.Cve.Description);
                    }
                }
            });
        });
    }

    private void Unverified(IContainer container)
    {
        container.Column(column =>
        {
            Section(column, "Unverified packages");
            if (scan.UnverifiedPackages.Count == 0)
            {
                column.Item().Text("None: every package's lookups completed.");
                return;
            }

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2);
                    columns.RelativeColumn(1.2f);
                    columns.RelativeColumn(5);
                });
                table.Header(row =>
                {
                    foreach (var title in UnverifiedTitles)
                    {
                        row.Cell().Element(HeaderCell).Text(title).Bold();
                    }
                });
                foreach (var unverified in scan.UnverifiedPackages)
                {
                    table.Cell().Element(Cell).Text(unverified.Package.Name);
                    table.Cell().Element(Cell).Text(unverified.Package.Version);
                    table.Cell().Element(Cell).Text(unverified.Detail);
                }
            });
        });
    }

    private void Warnings(IContainer container)
    {
        container.Column(column =>
        {
            Section(column, "Warnings");
            if (scan.Warnings.Count == 0)
            {
                column.Item().Text("None.");
                return;
            }

            foreach (var warning in scan.Warnings)
            {
                column.Item().Text(warning.Input is null ? warning.Message : $"{warning.Message} ({warning.Input})");
            }
        });
    }
}
