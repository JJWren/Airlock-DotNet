namespace Airlock.Infrastructure.Options;

/// <summary>Where Syft is and how long its status is kept, bound from <c>Airlock:Syft</c> (IR-05, IR-06).</summary>
public sealed class SyftOptions
{
    public const string SectionName = "Airlock:Syft";

    /// <summary>The Syft binary or its folder (<c>SYFT_BINARY_PATH</c>); a relative path starts at the application's folder.</summary>
    public string? BinaryPath { get; set; }

    public TimeSpan StatusCacheDuration { get; set; } = TimeSpan.FromSeconds(30);
}
