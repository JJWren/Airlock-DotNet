using Airlock.Application.Events;
using Airlock.Application.Logging;
using Airlock.Domain.Scanning;
using Microsoft.Extensions.Logging;

namespace Airlock.Application.Tests.Fakes;

/// <summary>
/// What U3's Live Log provider will do with scan-scoped entries: read the scan id from the logging scope and hand
/// the entry to the <see cref="ScanLogRouter"/>.
/// </summary>
internal sealed class RoutingLoggerProvider(ScanLogRouter router, TimeProvider timeProvider) : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();

    public ILogger CreateLogger(string categoryName) => new RoutingLogger(categoryName, this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => scopes = scopeProvider;

    public void Dispose()
    {
    }

    private void Route(LogLevel level, string category, string message)
    {
        if (CurrentScanId() is { } scanId)
        {
            router.Route(new LiveLogEntry(timeProvider.GetUtcNow(), level, category, message, scanId));
        }
    }

    private ScanId? CurrentScanId()
    {
        ScanId? found = null;
        scopes.ForEachScope(
            (scope, _) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                {
                    foreach (var pair in pairs)
                    {
                        if (pair.Key == ScanLogScope.Key && pair.Value is ScanId id)
                        {
                            found = id;
                        }
                    }
                }
            },
            (object?)null);
        return found;
    }

    private sealed class RoutingLogger(string category, RoutingLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => provider.scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            provider.Route(logLevel, category, formatter(state, null));
    }
}
