using Airlock.Application.Events;
using Airlock.Application.Logging;
using Airlock.Domain.Scanning;
using Microsoft.Extensions.Logging;

namespace Airlock.Infrastructure.Logging;

/// <summary>
/// Captures Airlock's own log entries for the Live Log and hands scan-scoped ones to the Application's
/// <see cref="ScanLogRouter"/> (IR-15, IR-16). Its filter rules, added by <c>AddAirlockInfrastructure</c>, keep it at
/// Debug for the <c>Airlock</c> categories whatever <c>LOG_LEVEL</c> says (IR-18).
/// </summary>
public sealed class LiveLogLoggerProvider(LiveLogBroadcaster broadcaster, ScanLogRouter router, TimeProvider timeProvider)
    : ILoggerProvider, ISupportExternalScope
{
    /// <summary>The category prefix the Live Log captures.</summary>
    public const string Category = "Airlock";

    // An entry logged while this thread is publishing is dropped, so no log call on that path can recurse.
    [ThreadStatic]
    private static bool publishing;

    private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();

    public ILogger CreateLogger(string categoryName) => new LiveLogLogger(categoryName, this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => scopes = scopeProvider;

    public void Dispose()
    {
    }

    // IR-15: Airlock's own categories only.
    internal static bool Captures(string categoryName) =>
        categoryName == Category || categoryName.StartsWith(Category + ".", StringComparison.Ordinal);

    private void Write(LogLevel level, string category, string message)
    {
        if (publishing)
        {
            return;
        }

        publishing = true;
        try
        {
            var entry = new LiveLogEntry(timeProvider.GetUtcNow(), level, category, message, CurrentScanId());
            broadcaster.Publish(entry);

            // IR-16: an entry with a scan id also goes onto that scan's event stream.
            if (entry.ScanId is not null)
            {
                router.Route(entry);
            }
        }
        finally
        {
            publishing = false;
        }
    }

    // IR-15: the scan id from the ScanId scope entry; the innermost scope wins.
    private ScanId? CurrentScanId()
    {
        var found = new ScanIdHolder();
        scopes.ForEachScope(
            static (scope, holder) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                {
                    foreach (var pair in pairs)
                    {
                        if (pair.Key == ScanLogScope.Key && pair.Value is ScanId id)
                        {
                            holder.Value = id;
                        }
                    }
                }
            },
            found);
        return found.Value;
    }

    private sealed class ScanIdHolder
    {
        public ScanId? Value { get; set; }
    }

    private sealed class LiveLogLogger(string category, LiveLogLoggerProvider provider) : ILogger
    {
        private readonly bool captured = Captures(category);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => provider.scopes.Push(state);

        // IR-15: Debug and above.
        public bool IsEnabled(LogLevel logLevel) => captured && logLevel is >= LogLevel.Debug and < LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (IsEnabled(logLevel))
            {
                // IR-15: the formatted message only, never the exception's details.
                provider.Write(logLevel, category, formatter(state, null));
            }
        }
    }
}
