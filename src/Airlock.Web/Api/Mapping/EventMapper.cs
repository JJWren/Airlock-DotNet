using Airlock.Application.Events;
using Airlock.Web.Api.Contracts;
using Airlock.Web.Api.Streams;
using Microsoft.Extensions.Logging;

namespace Airlock.Web.Api.Mapping;

/// <summary>Turns the Application's events into the streams' events (domain-entities.md, events).</summary>
internal static class EventMapper
{
    // WR-12: snapshot first, then phase, progress and log, and finished last.
    public static EventStreamItem ForScan(ScanEvent scanEvent) => scanEvent switch
    {
        ScanSnapshot snapshot => new("snapshot", ApiJson.SerializeCompact(ScanDtoMapper.ToDto(snapshot.Scan))),
        PhaseChanged changed => new("phase", ApiJson.SerializeCompact(new PhaseEventDto(ScanDtoMapper.Phase(changed.Phase)))),
        PackagesCorrelated correlated => new(
            "progress", ApiJson.SerializeCompact(new ScanProgressDto(correlated.Progress.Correlated, correlated.Progress.Total))),
        ScanLogged logged => new("log", ApiJson.SerializeCompact(
            new ScanLogEntryDto(ApiTime.Format(logged.Entry.Timestamp), Level(logged.Entry.Level), logged.Entry.Category, logged.Entry.Message))),
        ScanFinished finished => new("finished", ApiJson.SerializeCompact(new FinishedEventDto(ScanDtoMapper.Status(finished.Status)))),
        _ => throw new ArgumentOutOfRangeException(nameof(scanEvent), scanEvent, null),
    };

    // WR-18: every Live Log line carries its scan id, or null outside a scan.
    public static EventStreamItem ForLiveLog(LiveLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new("log", ApiJson.SerializeCompact(
            new LogEntryDto(ApiTime.Format(entry.Timestamp), Level(entry.Level), entry.Category, entry.Message, entry.ScanId?.ToString())));
    }

    private static LogLevelValue Level(LogLevel level) => level switch
    {
        LogLevel.Trace => LogLevelValue.Trace,
        LogLevel.Debug => LogLevelValue.Debug,
        LogLevel.Information => LogLevelValue.Info,
        LogLevel.Warning => LogLevelValue.Warning,
        LogLevel.Error => LogLevelValue.Error,
        LogLevel.Critical => LogLevelValue.Critical,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };
}
