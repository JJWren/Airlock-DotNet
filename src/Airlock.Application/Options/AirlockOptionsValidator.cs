using Airlock.Application.Ports;
using Microsoft.Extensions.Options;

namespace Airlock.Application.Options;

/// <summary>Checks every option against the ranges of the U2 NFR Design; an invalid value stops startup (E13).</summary>
public sealed class AirlockOptionsValidator(IHostFileSystem fileSystem) : IValidateOptions<AirlockOptions>
{
    private const long Mebibyte = 1024 * 1024;

    public ValidateOptionsResult Validate(string? name, AirlockOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        List<string> failures = [];

        Check(failures, options.Limits.MaxArtifactsPerScan is >= 1 and <= 1_000,
            "Limits:MaxArtifactsPerScan must be between 1 and 1,000");
        Check(failures, options.Limits.MaxBytesPerScan >= Mebibyte,
            "Limits:MaxBytesPerScan must be at least 1 MiB (1,048,576 bytes)");
        Check(failures, Between(options.Timeouts.Scan, TimeSpan.FromMinutes(1), TimeSpan.FromHours(24)),
            "Timeouts:Scan must be between 1 minute and 24 hours");
        Check(failures, Between(options.Timeouts.SyftRun, TimeSpan.FromMinutes(1), options.Timeouts.Scan),
            "Timeouts:SyftRun must be between 1 minute and Timeouts:Scan");
        Check(failures, Between(options.Timeouts.CancelGracePeriod, TimeSpan.Zero, TimeSpan.FromSeconds(10)),
            "Timeouts:CancelGracePeriod must be between 0 and 10 seconds");
        Check(failures, Between(options.Retention.Duration, TimeSpan.FromMinutes(1), TimeSpan.FromDays(30)),
            "Retention:Duration must be between 1 minute and 30 days");
        Check(failures, options.Retention.MaxScans is >= 1 and <= 1_000,
            "Retention:MaxScans must be between 1 and 1,000");
        Check(failures, options.Correlation.MaxParallelism is >= 1 and <= 16,
            "Correlation:MaxParallelism must be between 1 and 16");
        Check(failures, options.Correlation.OutageThreshold is >= 3 and <= 20,
            "Correlation:OutageThreshold must be between 3 and 20");

        foreach (var root in options.AllowedScanRoots)
        {
            Check(failures, !string.IsNullOrWhiteSpace(root) && fileSystem.IsAbsolute(root),
                $"AllowedScanRoots must hold absolute paths, not '{root}'");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool Between(TimeSpan value, TimeSpan minimum, TimeSpan maximum) => value >= minimum && value <= maximum;

    private static void Check(List<string> failures, bool valid, string message)
    {
        if (!valid)
        {
            failures.Add(AirlockOptions.SectionName + ":" + message);
        }
    }
}
