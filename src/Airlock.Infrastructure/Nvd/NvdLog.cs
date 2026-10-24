using Microsoft.Extensions.Logging;

namespace Airlock.Infrastructure.Nvd;

/// <summary>
/// The NVD lines of the Live Log; event ids in the 2200s (U3-NFR-08). Each request is a Trace line: the Application
/// already logs each lookup at Debug, so the Terminal does not show every request twice.
/// </summary>
internal static partial class NvdLog
{
    [LoggerMessage(EventId = 2201, Level = LogLevel.Trace, Message = "NVD {Parameter} request from index {StartIndex}: {Status} in {ElapsedMilliseconds} ms (attempt {Attempt})")]
    public static partial void Requested(this ILogger logger, string parameter, int startIndex, int status, long elapsedMilliseconds, int attempt);

    [LoggerMessage(EventId = 2202, Level = LogLevel.Warning, Message = "⏳ NVD {Answer}; retrying in {DelaySeconds:0.0} s (attempt {Next} of {Attempts})")]
    public static partial void Retrying(this ILogger logger, string answer, double delaySeconds, int next, int attempts);

    [LoggerMessage(EventId = 2203, Level = LogLevel.Warning, Message = "⚠️ NVD rejected the CPE as malformed ({Message}): {Cpe}")]
    public static partial void MalformedCpe(this ILogger logger, string message, string cpe);

    [LoggerMessage(EventId = 2204, Level = LogLevel.Debug, Message = "NVD lookup failed: {Reason}")]
    public static partial void LookupFailed(this ILogger logger, string reason);
}
