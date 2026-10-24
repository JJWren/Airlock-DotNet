namespace Airlock.Infrastructure.Options;

/// <summary>The NVD CVE API 2.0 settings, bound from <c>Airlock:Nvd</c> (IR-07, IR-22).</summary>
public sealed class NvdOptions
{
    public const string SectionName = "Airlock:Nvd";

    /// <summary>The NVD API key (<c>NVD_API_KEY</c>); without one Airlock starts DEGRADED.</summary>
    public string? ApiKey { get; set; }

    public Uri BaseUrl { get; set; } = new("https://services.nvd.nist.gov/rest/json/cves/2.0");

    /// <summary>With <see cref="Window"/>, the rate NVD allows: one request start every Window / RequestsPerWindow.</summary>
    public int RequestsPerWindow { get; set; } = 50;

    public TimeSpan Window { get; set; } = TimeSpan.FromSeconds(30);
}
