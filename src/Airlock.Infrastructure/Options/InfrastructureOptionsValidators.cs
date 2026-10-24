using Microsoft.Extensions.Options;

namespace Airlock.Infrastructure.Options;

/// <summary>Checks the NVD options against the ranges of the U3 NFR Design; an invalid value stops startup (IR-22).</summary>
internal sealed class NvdOptionsValidator : IValidateOptions<NvdOptions>
{
    public ValidateOptionsResult Validate(string? name, NvdOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        List<string> failures = [];
        OptionChecks.Check(failures, options.BaseUrl is { IsAbsoluteUri: true, Scheme: "https" },
            "Nvd:BaseUrl must be an absolute https URL");
        OptionChecks.Check(failures, options.RequestsPerWindow is >= 1 and <= 1_000,
            "Nvd:RequestsPerWindow must be between 1 and 1,000");
        OptionChecks.Check(failures, OptionChecks.Between(options.Window, TimeSpan.FromSeconds(1), TimeSpan.FromHours(1)),
            "Nvd:Window must be between 1 second and 1 hour");
        return OptionChecks.Result(failures);
    }
}

/// <summary>Checks the Syft options (IR-22).</summary>
internal sealed class SyftOptionsValidator : IValidateOptions<SyftOptions>
{
    public ValidateOptionsResult Validate(string? name, SyftOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        List<string> failures = [];
        OptionChecks.Check(failures, options.BinaryPath is null || !string.IsNullOrWhiteSpace(options.BinaryPath),
            "Syft:BinaryPath must not be blank");
        OptionChecks.Check(failures,
            OptionChecks.Between(options.StatusCacheDuration, TimeSpan.FromSeconds(1), TimeSpan.FromHours(1)),
            "Syft:StatusCacheDuration must be between 1 second and 1 hour");
        return OptionChecks.Result(failures);
    }
}

/// <summary>Checks the staging options (IR-22).</summary>
internal sealed class StagingOptionsValidator : IValidateOptions<StagingOptions>
{
    public ValidateOptionsResult Validate(string? name, StagingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        List<string> failures = [];
        OptionChecks.Check(failures, !string.IsNullOrWhiteSpace(options.Root) && Path.IsPathFullyQualified(options.Root),
            "Staging:Root must be a full path");
        return OptionChecks.Result(failures);
    }
}

/// <summary>The message form the Application's validator uses: the option's full name, then the rule.</summary>
internal static class OptionChecks
{
    public static void Check(List<string> failures, bool valid, string message)
    {
        if (!valid)
        {
            failures.Add("Airlock:" + message);
        }
    }

    public static bool Between(TimeSpan value, TimeSpan minimum, TimeSpan maximum) => value >= minimum && value <= maximum;

    public static ValidateOptionsResult Result(List<string> failures) =>
        failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
}
