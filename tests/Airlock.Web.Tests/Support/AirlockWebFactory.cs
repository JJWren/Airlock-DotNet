using Airlock.Application.Ports;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Airlock.Web.Tests.Support;

/// <summary>
/// The real host — Application, Infrastructure and Web — with fakes for Syft and NVD, a staging root of its own and,
/// where a test needs one, a fake clock (U4-NFR-13). Settings given here win over every other configuration source.
/// </summary>
internal sealed class AirlockWebFactory : WebApplicationFactory<Program>
{
    private readonly TemporaryFolder staging = new();
    private readonly Dictionary<string, string?> settings;
    private readonly string environment;
    private readonly bool realAdapters;

    public AirlockWebFactory(
        Action<Dictionary<string, string?>>? configure = null,
        FakeTimeProvider? clock = null,
        string environment = "Production",
        bool realAdapters = false)
    {
        settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Airlock:Staging:Root"] = staging.Path,
            ["Airlock:Syft:BinaryPath"] = Path.Combine(staging.Path, "no-syft-here", "syft"),
            ["Logging:LogLevel:Airlock"] = "Debug",
        };
        configure?.Invoke(settings);
        Clock = clock;
        this.environment = environment;
        this.realAdapters = realAdapters;

        // The redirect handler buffers every request body so it could resend it, which no upload test can live with.
        ClientOptions.AllowAutoRedirect = false;
        ClientOptions.HandleCookies = false;
    }

    public FakeSbomGenerator Sbom { get; } = new();

    public FakeVulnerabilityCatalog Catalog { get; } = new();

    public FakeTimeProvider? Clock { get; }

    public string StagingRoot => staging.Path;

    /// <summary>The started host, for tests that stop it.</summary>
    public IHost? StartedHost { get; private set; }

    public FakeLogCollector Logs => Services.GetFakeLogCollector();

    /// <summary>The scan folders under the staging root — the lock file aside.</summary>
    public IReadOnlyList<string> StagingFolders() =>
        Directory.Exists(staging.Path) ? [.. Directory.EnumerateDirectories(staging.Path)] : [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        builder.ConfigureLogging(logging => logging.AddFakeLogging());
        builder.ConfigureTestServices(services =>
        {
            if (!realAdapters)
            {
                services.RemoveAll<ISbomGenerator>();
                services.AddSingleton<ISbomGenerator>(Sbom);
                services.RemoveAll<IVulnerabilityCatalog>();
                services.AddSingleton<IVulnerabilityCatalog>(Catalog);
            }

            if (Clock is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
            }
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        StartedHost = host;
        return host;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        staging.Dispose();
    }
}
