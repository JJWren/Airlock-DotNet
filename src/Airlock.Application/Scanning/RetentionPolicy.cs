using Airlock.Application.Options;
using Airlock.Domain.Scanning;

namespace Airlock.Application.Scanning;

internal static class RetentionPolicy
{
    // AR-33: finished scans past the retention duration, then the earliest finished beyond the maximum; never a running one.
    public static IReadOnlyList<ScanId> ScansToRemove(IEnumerable<Scan> scans, DateTimeOffset now, RetentionOptions retention)
    {
        var finished = scans
            .Where(scan => scan.FinishedAt is not null)
            .OrderBy(scan => scan.FinishedAt)
            .ToList();
        var expired = finished.Where(scan => now - scan.FinishedAt!.Value > retention.Duration).ToList();
        var kept = finished.Except(expired).ToList();
        var excess = Math.Max(0, kept.Count - retention.MaxScans);

        return [.. expired.Concat(kept.Take(excess)).Select(scan => scan.Id)];
    }
}
