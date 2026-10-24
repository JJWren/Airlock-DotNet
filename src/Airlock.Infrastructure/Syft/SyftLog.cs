using Microsoft.Extensions.Logging;

namespace Airlock.Infrastructure.Syft;

/// <summary>The Syft lines of the Live Log; event ids in the 2100s (U3-NFR-08).</summary>
internal static partial class SyftLog
{
    [LoggerMessage(EventId = 2101, Level = LogLevel.Debug, Message = "⚙️ Syft finished {Input} in {ElapsedMilliseconds} ms with exit code {ExitCode}")]
    public static partial void RunFinished(this ILogger logger, string input, long elapsedMilliseconds, int exitCode);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Warning, Message = "🛑 Syft was stopped for {Input}: {Cause}")]
    public static partial void RunStopped(this ILogger logger, string input, string cause);

    [LoggerMessage(EventId = 2103, Level = LogLevel.Warning, Message = "⚠️ The NuGet package details in {Input} could not be read: {Reason}")]
    public static partial void NuspecUnreadable(this ILogger logger, string input, string reason);

    [LoggerMessage(EventId = 2104, Level = LogLevel.Debug, Message = "📦 NuGet package {Name} {Version} added from the .nuspec of {Input}")]
    public static partial void NuspecPackageAdded(this ILogger logger, string name, string version, string input);

    [LoggerMessage(EventId = 2105, Level = LogLevel.Warning, Message = "⚠️ Syft is not available: {Detail}")]
    public static partial void SyftUnavailable(this ILogger logger, string detail);

    [LoggerMessage(EventId = 2106, Level = LogLevel.Debug, Message = "🔧 {Detail}")]
    public static partial void SyftFound(this ILogger logger, string detail);

    [LoggerMessage(EventId = 2107, Level = LogLevel.Warning, Message = "⚠️ Syft's temporary folder could not be prepared or removed: {Reason}")]
    public static partial void TemporaryFolderProblem(this ILogger logger, string reason);
}
