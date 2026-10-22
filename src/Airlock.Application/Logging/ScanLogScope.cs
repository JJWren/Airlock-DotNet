using Airlock.Domain.Scanning;

namespace Airlock.Application.Logging;

/// <summary>
/// The logging scope a scan's work runs in (AR-24). It carries one entry, <see cref="Key"/>, whose value is the
/// <see cref="ScanId"/>; U3's Live Log provider reads it to give each entry its scan.
/// </summary>
public static class ScanLogScope
{
    public const string Key = "ScanId";

    public static IReadOnlyList<KeyValuePair<string, object?>> For(ScanId scanId) => [new(Key, scanId)];
}
