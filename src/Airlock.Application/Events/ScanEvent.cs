using Airlock.Domain.Scanning;

namespace Airlock.Application.Events;

/// <summary>Something that happened to a scan, as its subscribers receive it (AR-23).</summary>
public abstract record ScanEvent(ScanId ScanId);

/// <summary>The scan's latest version; the first item of every subscription.</summary>
public sealed record ScanSnapshot(ScanId ScanId, Scan Scan) : ScanEvent(ScanId);

/// <summary>The scan entered a phase.</summary>
public sealed record PhaseChanged(ScanId ScanId, ScanPhase Phase) : ScanEvent(ScanId);

/// <summary>Correlation progress: packages decided of the total.</summary>
public sealed record PackagesCorrelated(ScanId ScanId, ScanProgress Progress) : ScanEvent(ScanId);

/// <summary>A Live Log entry written for this scan.</summary>
public sealed record ScanLogged(ScanId ScanId, LiveLogEntry Entry) : ScanEvent(ScanId);

/// <summary>The scan's final status; always the last item.</summary>
public sealed record ScanFinished(ScanId ScanId, ScanStatus Status) : ScanEvent(ScanId);
