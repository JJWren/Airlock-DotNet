namespace Airlock.Infrastructure.Options;

/// <summary>Where uploads and Syft's temporary files are kept, bound from <c>Airlock:Staging</c> (IR-11).</summary>
public sealed class StagingOptions
{
    public const string SectionName = "Airlock:Staging";

    public string Root { get; set; } = Path.Combine(Path.GetTempPath(), "airlock-staging");
}
