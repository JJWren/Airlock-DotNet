using Airlock.Domain.Scanning;
using Microsoft.Extensions.Logging;

namespace Airlock.Infrastructure.Staging;

/// <summary>The staging lines of the Live Log; event ids in the 2300s (U3-NFR-08).</summary>
internal static partial class StagingLog
{
    [LoggerMessage(EventId = 2301, Level = LogLevel.Debug, Message = "📁 Staging area ready for scan {ScanId}")]
    public static partial void AreaCreated(this ILogger logger, ScanId scanId);

    [LoggerMessage(EventId = 2302, Level = LogLevel.Debug, Message = "📥 Staged {FileName} ({Bytes} bytes)")]
    public static partial void ArtifactStaged(this ILogger logger, string fileName, long bytes);

    [LoggerMessage(EventId = 2303, Level = LogLevel.Debug, Message = "📥 {FileName} is larger than the bytes left for the scan; nothing of it is kept")]
    public static partial void ArtifactTooLarge(this ILogger logger, string fileName);

    [LoggerMessage(EventId = 2304, Level = LogLevel.Warning, Message = "⚠️ A staging folder is still in use; deleting it again (attempt {Attempt} of {Attempts})")]
    public static partial void DeletionRetried(this ILogger logger, int attempt, int attempts);

    [LoggerMessage(EventId = 2305, Level = LogLevel.Information, Message = "🧹 Removed {Count} staging folder(s) left by an earlier run")]
    public static partial void LeftoversRemoved(this ILogger logger, int count);

    [LoggerMessage(EventId = 2306, Level = LogLevel.Warning, Message = "⚠️ A staging folder left by an earlier run could not be removed: {Reason}")]
    public static partial void LeftoverNotRemoved(this ILogger logger, string reason);
}
