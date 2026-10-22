using Airlock.Domain.Scanning;
using Microsoft.Extensions.Logging;

namespace Airlock.Application.Events;

/// <summary>One line of the Live Log: when (UTC), how severe, from which part of Airlock, what, and for which scan.</summary>
public sealed record LiveLogEntry(DateTimeOffset Timestamp, LogLevel Level, string Category, string Message, ScanId? ScanId);
