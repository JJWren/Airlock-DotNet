namespace Airlock.Domain.Scanning;

/// <summary>The state of a scan; every value except <see cref="Running"/> is final.</summary>
public enum ScanStatus
{
    Running,
    Completed,
    Incomplete,
    Failed,
    Cancelled,
}

/// <summary>The steps of a running scan, in order. Uploads are staged before a scan exists.</summary>
public enum ScanPhase
{
    SbomGeneration,
    Correlation,
    Summary,
}
