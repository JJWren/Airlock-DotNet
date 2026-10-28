using System.Globalization;
using System.Text;
using Airlock.Application.Options;
using Airlock.Application.UseCases;
using Airlock.Domain.Intelligence;
using Airlock.Domain.Results;
using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;

namespace Airlock.Web.State;

/// <summary>
/// UR-34, U5-NFR-09: every time, duration, size, score and count the UI writes, in the browser's time zone and the
/// invariant culture, and the texts built from the Application's values; the process culture is never changed.
/// </summary>
internal static class UiFormat
{
    /// <summary>A time shown before the browser's zone is known.</summary>
    public const string UnknownTime = "—";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly string[] SizeUnits = ["KB", "MB", "GB", "TB"];

    /// <summary>The browser's IANA zone; UTC for a name .NET does not know.</summary>
    public static TimeZoneInfo ResolveZone(string? ianaName) =>
        !string.IsNullOrWhiteSpace(ianaName) && TimeZoneInfo.TryFindSystemTimeZoneById(ianaName, out var zone) ? zone : TimeZoneInfo.Utc;

    /// <summary>The terminal's <c>HH:mm:ss</c>.</summary>
    public static string Clock(DateTimeOffset time, TimeZoneInfo? zone) =>
        zone is null ? UnknownTime : TimeZoneInfo.ConvertTime(time, zone).ToString("HH:mm:ss", Invariant);

    /// <summary><c>yyyy-MM-dd HH:mm:ss UTC+02:00</c>, for the Reports header and the log export.</summary>
    public static string Timestamp(DateTimeOffset time, TimeZoneInfo? zone)
    {
        if (zone is null)
        {
            return UnknownTime;
        }

        var local = TimeZoneInfo.ConvertTime(time, zone);
        return local.ToString("yyyy-MM-dd HH:mm:ss", Invariant) + " " + Offset(local.Offset);
    }

    public static string Offset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? '-' : '+';
        var size = offset.Duration();
        return string.Create(Invariant, $"UTC{sign}{size.Hours:00}:{size.Minutes:00}");
    }

    /// <summary>UR-15: whole seconds, <c>mm:ss</c>, and <c>h:mm:ss</c> from one hour.</summary>
    public static string Elapsed(TimeSpan elapsed)
    {
        var seconds = elapsed <= TimeSpan.Zero ? 0 : (long)Math.Floor(elapsed.TotalSeconds);
        var hours = seconds / 3600;
        return hours > 0
            ? string.Create(Invariant, $"{hours}:{seconds / 60 % 60:00}:{seconds % 60:00}")
            : string.Create(Invariant, $"{seconds / 60:00}:{seconds % 60:00}");
    }

    /// <summary>UR-23: a duration in seconds with two decimals, <c>92.42</c>.</summary>
    public static string Seconds(TimeSpan duration) => duration.TotalSeconds.ToString("0.00", Invariant);

    /// <summary>UR-34: B, KB, MB or GB in powers of 1,024, with one decimal above bytes.</summary>
    public static string Size(long bytes)
    {
        if (bytes < 1024)
        {
            return string.Create(Invariant, $"{Math.Max(bytes, 0)} B");
        }

        double value = bytes;
        var unit = -1;
        do
        {
            value /= 1024;
            unit++;
        }
        while (Math.Round(value, 1) >= 1024 && unit < SizeUnits.Length - 1);

        return value.ToString("0.0", Invariant) + " " + SizeUnits[unit];
    }

    public static string Score(decimal score) => score.ToString("0.0", Invariant);

    /// <summary>UR-27: a count in at least two digits, <c>02</c>.</summary>
    public static string Count(int count) => count.ToString("00", Invariant);

    /// <summary>UR-17: the level as <c>LOG_LEVEL</c> spells it.</summary>
    public static string Level(Microsoft.Extensions.Logging.LogLevel level) => level switch
    {
        Microsoft.Extensions.Logging.LogLevel.Trace => "TRACE",
        Microsoft.Extensions.Logging.LogLevel.Debug => "DEBUG",
        Microsoft.Extensions.Logging.LogLevel.Information => "INFO",
        Microsoft.Extensions.Logging.LogLevel.Warning => "WARNING",
        Microsoft.Extensions.Logging.LogLevel.Error => "ERROR",
        Microsoft.Extensions.Logging.LogLevel.Critical => "CRITICAL",
        _ => "NONE",
    };

    /// <summary>UR-35: statuses and severities in the API's capitals.</summary>
    public static string Status(ScanStatus status) => status.ToString().ToUpperInvariant();

    public static string Severity(Severity severity) => severity.ToString().ToUpperInvariant();

    /// <summary>The PDF's own name without its extension, which the JSON export shares (UR-32).</summary>
    public static string ExportStem(DateTimeOffset finishedAt) =>
        string.Create(Invariant, $"airlock_audit_{finishedAt.UtcDateTime:yyyy-MM-ddTHH-mm-ss-fff}Z");

    public static string ReportHref(ScanId id) => "/reports/" + id;

    /// <summary>UR-23: the directory, or the first artifact as the scan lists it and how many more.</summary>
    public static string Target(ScanTarget target) => target switch
    {
        DirectoryTarget directory => "Target: " + directory.Path,
        ArtifactsTarget { Artifacts.Count: 1 } artifacts => "Target: " + artifacts.Artifacts[0].FileName,
        ArtifactsTarget { Artifacts.Count: 2 } artifacts => "Target: " + artifacts.Artifacts[0].FileName + " and 1 more artifact",
        ArtifactsTarget artifacts => string.Create(
            Invariant, $"Target: {artifacts.Artifacts[0].FileName} and {artifacts.Artifacts.Count - 1} more artifacts"),
        _ => string.Empty,
    };

    /// <summary>UR-25, UR-30: "lookup failed: NVD returned 503", or "version unknown".</summary>
    public static string Reason(UnverifiedPackage package) =>
        package.Reason == UnverifiedReason.LookupFailed ? "lookup failed: " + package.Detail : package.Detail;

    /// <summary>A package's name and version, as the banner lists it.</summary>
    public static string Label(Package package) => package.Version.Length == 0 ? package.Name : package.Name + " " + package.Version;

    /// <summary>UR-27: "CPE match" or "Product match" (defect 1).</summary>
    public static string Method(MatchMethod method) => method is CpeMatch ? "CPE match" : "Product match";

    /// <summary>UR-30: how the package was matched.</summary>
    public static string Match(MatchMethod method) => method switch
    {
        CpeMatch cpe => $"Matched by CPE: {cpe.Cpe.Value} ({(cpe.Cpe.Source == CpeSource.Dictionary ? "dictionary" : "generated")})",
        ProductMatch product => "Matched by product: " + product.Product,
        _ => string.Empty,
    };

    /// <summary>UR-30: "CVSS v3.1 Score · NVD", or "· CNA" for the reporting CNA's score.</summary>
    public static string ScoreSource(CvssScore score) =>
        score.VersionLabel + " Score · " + (score.Source == CvssSource.Primary ? "NVD" : "CNA");

    /// <summary>UR-16: where the scan is.</summary>
    public static string Phase(Scan scan) => scan.Phase switch
    {
        ScanPhase.SbomGeneration => "SBOM Generation",
        ScanPhase.Correlation when scan.Progress is { } progress =>
            string.Create(Invariant, $"Correlating {progress.Correlated} of {progress.Total}"),
        ScanPhase.Correlation => "Correlation",
        _ => "Summary",
    };

    /// <summary>UR-11: "Transferring artifacts: 45% (450.0 MB of 1.0 GB)".</summary>
    public static string Transfer(UploadProgress upload) =>
        string.Create(Invariant, $"Transferring artifacts: {upload.Percent}% ({Size(upload.SentBytes)} of {Size(upload.TotalBytes)})");

    /// <summary>UR-08: the empty upload panel states the limits, in the Application's words.</summary>
    public static string Limits(LimitsOptions limits) =>
        string.Create(Invariant, $"Up to {limits.MaxArtifactsPerScan} files, {UploadLimits.Size(limits.MaxBytesPerScan)} in total");

    /// <summary>UR-09: the failing checks' details.</summary>
    public static string NotReady(SystemHealth health) =>
        "Airlock is not ready: " + string.Join("; ", health.Checks.Where(check => !check.Passing).Select(check => check.Detail));

    /// <summary>UR-19: the export's file name, with the time written as in the PDF's name.</summary>
    public static string LogFileName(DateTimeOffset now) => ExportStem(now) + ".log";

    /// <summary>UR-19: a first line naming Airlock's version, the time and the zone, then the lines as shown.</summary>
    public static string LogExport(IEnumerable<TerminalLine> lines, TimeZoneInfo? zone, DateTimeOffset now, string version)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var effective = zone ?? TimeZoneInfo.Utc;
        var text = new StringBuilder();
        text.Append(Invariant, $"# Airlock {version} Live Log, exported {Timestamp(now, effective)} ({effective.Id})").Append('\n');
        foreach (var line in lines)
        {
            text.Append(Clock(line.Timestamp, effective)).Append(' ').Append(Level(line.Level)).Append(' ').Append(line.Message).Append('\n');
        }

        return text.ToString();
    }
}