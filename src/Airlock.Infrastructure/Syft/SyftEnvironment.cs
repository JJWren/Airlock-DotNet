namespace Airlock.Infrastructure.Syft;

/// <summary>The environment every Syft run gets (IR-01, U3-NFR-04, U3-NFR-07).</summary>
internal sealed class SyftEnvironment(string? nvdApiKey, IReadOnlyDictionary<string, string>? extra = null)
{
    /// <summary>The variables that may hold the NVD key, removed by name.</summary>
    public static readonly IReadOnlyCollection<string> HiddenNames = ["NVD_API_KEY", "Airlock__Nvd__ApiKey"];

    public SyftRunRequest Request(string fileName, IReadOnlyList<string> arguments, TimeSpan timeLimit, string? temporaryFolder = null)
    {
        // IR-01: the update check off; Syft's temporary files in the run's own folder, deleted after the run.
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["SYFT_CHECK_FOR_APP_UPDATE"] = "false" };
        if (temporaryFolder is not null)
        {
            environment["TMPDIR"] = temporaryFolder;
            environment["TMP"] = temporaryFolder;
            environment["TEMP"] = temporaryFolder;
        }

        foreach (var (name, value) in extra ?? new Dictionary<string, string>())
        {
            environment[name] = value;
        }

        return new SyftRunRequest(fileName, arguments, timeLimit)
        {
            Environment = environment,
            HiddenNames = HiddenNames,
            HiddenValues = string.IsNullOrWhiteSpace(nvdApiKey) ? [] : [nvdApiKey],
        };
    }
}
