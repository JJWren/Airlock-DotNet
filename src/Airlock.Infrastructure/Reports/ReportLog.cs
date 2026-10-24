using Airlock.Domain.Scanning;
using Microsoft.Extensions.Logging;

namespace Airlock.Infrastructure.Reports;

/// <summary>The report lines of the Live Log; event ids in the 2500s (U3-NFR-08).</summary>
internal static partial class ReportLog
{
    [LoggerMessage(EventId = 2501, Level = LogLevel.Debug, Message = "📄 Scan report for {ScanId} rendered: {Bytes} bytes in {ElapsedMilliseconds} ms")]
    public static partial void ReportRendered(this ILogger logger, ScanId scanId, int bytes, long elapsedMilliseconds);
}
