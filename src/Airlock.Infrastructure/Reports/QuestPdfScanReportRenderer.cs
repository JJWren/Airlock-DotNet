using Airlock.Application.Ports;
using Airlock.Domain.Scanning;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;

namespace Airlock.Infrastructure.Reports;

/// <summary>Renders a scan as the PDF Scan Report with QuestPDF (IR-20, IR-21).</summary>
public sealed class QuestPdfScanReportRenderer(TimeProvider timeProvider, ILogger<QuestPdfScanReportRenderer> logger)
    : IScanReportRenderer
{
    public ValueTask<byte[]> RenderPdfAsync(Scan scan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scan);
        cancellationToken.ThrowIfCancellationRequested();
        QuestPdfSettings.Ensure();
        var started = timeProvider.GetTimestamp();
        var pdf = new ScanReportDocument(scan, timeProvider.GetUtcNow()).GeneratePdf();
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.ReportRendered(scan.Id, pdf.Length, (long)timeProvider.GetElapsedTime(started).TotalMilliseconds);
        }

        return ValueTask.FromResult(pdf);
    }
}
