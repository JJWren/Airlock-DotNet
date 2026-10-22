namespace Airlock.Application.Options;

/// <summary>The <c>Airlock</c> configuration section. The defaults are the approved ones (U2 NFR Design).</summary>
public sealed class AirlockOptions
{
    public const string SectionName = "Airlock";

    public LimitsOptions Limits { get; set; } = new();

    public TimeoutOptions Timeouts { get; set; } = new();

    public RetentionOptions Retention { get; set; } = new();

    public CorrelationOptions Correlation { get; set; } = new();

    /// <summary>Directories a directory target must lie in; empty allows any directory (AR-05).</summary>
    public IReadOnlyList<string> AllowedScanRoots { get; set; } = [];
}

public sealed class LimitsOptions
{
    public int MaxArtifactsPerScan { get; set; } = 25;

    public long MaxBytesPerScan { get; set; } = 2L * 1024 * 1024 * 1024;
}

public sealed class TimeoutOptions
{
    public TimeSpan SyftRun { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan Scan { get; set; } = TimeSpan.FromMinutes(60);

    public TimeSpan CancelGracePeriod { get; set; } = TimeSpan.FromSeconds(5);
}

public sealed class RetentionOptions
{
    public TimeSpan Duration { get; set; } = TimeSpan.FromHours(24);

    public int MaxScans { get; set; } = 20;
}

public sealed class CorrelationOptions
{
    public int MaxParallelism { get; set; } = 4;

    public int OutageThreshold { get; set; } = 5;
}
