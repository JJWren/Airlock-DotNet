using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;
using Microsoft.Extensions.Logging;

namespace Airlock.Application.Logging;

/// <summary>
/// The Live Log catalogue of the U2 business logic model, in the original's wording where it had a message. Event ids:
/// 1100s scan lifecycle, 1200s SBOM generation, 1300s correlation, 1400s stopping and cleanup.
/// </summary>
internal static partial class ScanLog
{
    [LoggerMessage(EventId = 1101, Level = LogLevel.Information, Message = "🚀 Initializing vetting sequence for: {Target}")]
    public static partial void ScanStarting(this ILogger logger, string target);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Information, Message = "📥 Processing {Count} uploaded artifact(s)")]
    public static partial void ProcessingArtifacts(this ILogger logger, int count);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Warning, Message = "❌ Vetting sequence refused: {Reason}")]
    public static partial void ScanRefused(this ILogger logger, string reason);

    [LoggerMessage(EventId = 1104, Level = LogLevel.Information,
        Message = "✅ Vetting sequence complete: {Vulnerable} vulnerable, {Unverified} unverified, {Clean} clean of {Total} package(s)")]
    public static partial void ScanCompleted(this ILogger logger, int vulnerable, int unverified, int clean, int total);

    [LoggerMessage(EventId = 1105, Level = LogLevel.Error, Message = "⚠️ Vetting Sequence Aborted: {Reason}")]
    public static partial void ScanFailed(this ILogger logger, string reason);

    [LoggerMessage(EventId = 1106, Level = LogLevel.Error, Message = "⚠️ Vetting Sequence Aborted: Unexpected error — see the server log")]
    public static partial void ScanCrashed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Debug, Message = "📑 Generating SBOM for: {Input}")]
    public static partial void GeneratingSbom(this ILogger logger, string input);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Information, Message = "📦 {Count} package(s) identified in {Input}")]
    public static partial void PackagesIdentified(this ILogger logger, int count, string input);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Warning, Message = "⚠️ No packages identified in {Input}")]
    public static partial void NoPackagesIdentified(this ILogger logger, string input);

    [LoggerMessage(EventId = 1204, Level = LogLevel.Warning, Message = "⚠️ Distribution backports are not considered for {Input}")]
    public static partial void BackportsNotConsidered(this ILogger logger, string input);

    [LoggerMessage(EventId = 1205, Level = LogLevel.Error, Message = "❌ Syft failed: {Reason}")]
    public static partial void SyftFailed(this ILogger logger, string reason);

    [LoggerMessage(EventId = 1206, Level = LogLevel.Information, Message = "⚙️ {Count} unique package(s) to vet")]
    public static partial void PackagesToVet(this ILogger logger, int count);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Debug, Message = "🔍 CPE search: {Cpe}")]
    public static partial void CpeSearch(this ILogger logger, string cpe);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Information, Message = "🚩 Found {Count} vulnerabilities via CPE for {Package}")]
    public static partial void CpeFindings(this ILogger logger, int count, Package package);

    [LoggerMessage(EventId = 1303, Level = LogLevel.Debug, Message = "✅ No vulnerabilities found for this CPE: {Cpe}")]
    public static partial void CpeClean(this ILogger logger, string cpe);

    [LoggerMessage(EventId = 1304, Level = LogLevel.Debug, Message = "🔍 Product search: '{Product}' {Version}")]
    public static partial void ProductSearch(this ILogger logger, string product, string version);

    [LoggerMessage(EventId = 1305, Level = LogLevel.Information, Message = "🚩 Found {Count} vulnerabilities via product match for {Package}")]
    public static partial void ProductFindings(this ILogger logger, int count, Package package);

    [LoggerMessage(EventId = 1306, Level = LogLevel.Debug, Message = "✅ No vulnerabilities found via product match for {Package}")]
    public static partial void ProductClean(this ILogger logger, Package package);

    [LoggerMessage(EventId = 1307, Level = LogLevel.Debug, Message = "⏭ {Package}: name too generic for a product match")]
    public static partial void NameTooGeneric(this ILogger logger, Package package);

    [LoggerMessage(EventId = 1308, Level = LogLevel.Warning, Message = "⚠️ NVD CPE Search failed for {Cpe}: {Reason}")]
    public static partial void CpeSearchFailed(this ILogger logger, string cpe, string reason);

    [LoggerMessage(EventId = 1309, Level = LogLevel.Warning, Message = "⚠️ NVD product search failed for {Package}: {Reason}")]
    public static partial void ProductSearchFailed(this ILogger logger, Package package, string reason);

    [LoggerMessage(EventId = 1310, Level = LogLevel.Warning, Message = "⚠️ {Package}: version unknown, not checked")]
    public static partial void VersionUnknown(this ILogger logger, Package package);

    [LoggerMessage(EventId = 1311, Level = LogLevel.Error,
        Message = "❌ NVD unavailable: {Count} lookups failed in a row; remaining packages not checked")]
    public static partial void NvdUnavailable(this ILogger logger, int count);

    [LoggerMessage(EventId = 1401, Level = LogLevel.Warning, Message = "🛑 EMERGENCY STOP: Terminating scan {ScanId}")]
    public static partial void EmergencyStop(this ILogger logger, ScanId scanId);

    [LoggerMessage(EventId = 1402, Level = LogLevel.Error, Message = "🚨 CRITICAL: Sequence aborted. System resources released.")]
    public static partial void ScanAborted(this ILogger logger);

    [LoggerMessage(EventId = 1403, Level = LogLevel.Error, Message = "⚠️ Cleanup step failed: {Step}: {Reason}")]
    public static partial void CleanupFailed(this ILogger logger, string step, string reason);

    [LoggerMessage(EventId = 1404, Level = LogLevel.Warning, Message = "⚠️ Allowed scan root does not exist: {Root}")]
    public static partial void ScanRootMissing(this ILogger logger, string root);
}
