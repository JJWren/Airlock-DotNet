namespace Airlock.Web.Logging;

/// <summary>
/// The Web's log lines (U4-NFR-11), in <c>Airlock.Web.*</c> categories, which the Live Log shows. Client text is cleaned
/// before it gets here (U2 AR-25).
/// </summary>
internal static partial class WebLog
{
    [LoggerMessage(EventId = 3101, Level = LogLevel.Warning, Message = "🛡️ Refused a cross-site request from {Origin}")]
    public static partial void CrossSiteRequestRefused(this ILogger logger, string origin);

    [LoggerMessage(EventId = 3102, Level = LogLevel.Warning, Message = "❌ Refused a submission that is not multipart/form-data")]
    public static partial void UnsupportedMediaType(this ILogger logger);

    [LoggerMessage(EventId = 3103, Level = LogLevel.Warning, Message = "❌ Refused an invalid submission: {Detail}")]
    public static partial void InvalidSubmission(this ILogger logger, string detail);

    [LoggerMessage(EventId = 3104, Level = LogLevel.Warning, Message = "⚠️ An upload sent nothing for {Seconds} seconds and was ended")]
    public static partial void UploadStalled(this ILogger logger, int seconds);

    [LoggerMessage(EventId = 3105, Level = LogLevel.Information, Message = "📴 The client ended its upload before the scan started")]
    public static partial void UploadAbandoned(this ILogger logger);

    [LoggerMessage(EventId = 3201, Level = LogLevel.Error, Message = "Unexpected error while answering {Method} {Path}")]
    public static partial void UnexpectedError(this ILogger logger, Exception exception, string method, string path);

    [LoggerMessage(EventId = 3301, Level = LogLevel.Debug, Message = "An event stream ended because the server is stopping")]
    public static partial void StreamEndedByShutdown(this ILogger logger);

    // U5-NFR-12: the UI's failures, at Error; routine UI activity logs nothing at Information.
    [LoggerMessage(EventId = 3401, Level = LogLevel.Error, Message = "💥 A page failed: {Reason}")]
    public static partial void PageFailed(this ILogger logger, Exception exception, string reason);

    [LoggerMessage(EventId = 3402, Level = LogLevel.Error, Message = "⚠️ A call into the browser failed: {Function}: {Reason}")]
    public static partial void BrowserCallFailed(this ILogger logger, string function, string reason);

    [LoggerMessage(EventId = 3403, Level = LogLevel.Error, Message = "⚠️ A tab stopped following {Feed}: {Reason}")]
    public static partial void SessionFeedFailed(this ILogger logger, Exception exception, string feed, string reason);
}
