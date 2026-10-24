using Airlock.Application;
using Airlock.Application.Events;
using Airlock.Application.Options;
using Airlock.Application.Outcomes;
using Airlock.Application.Ports;
using Airlock.Application.UseCases;
using Airlock.Domain.Scanning;
using Airlock.Infrastructure.FileSystem;
using Airlock.Infrastructure.Logging;
using Airlock.Infrastructure.Nvd;
using Airlock.Infrastructure.Options;
using Airlock.Infrastructure.Reports;
using Airlock.Infrastructure.Scans;
using Airlock.Infrastructure.Staging;
using Airlock.Infrastructure.Syft;
using Airlock.Infrastructure.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Airlock.Infrastructure.Tests.Quality;

// U3-NFR-11 — the composition: every port, startup validation, a degraded start, the Live Log's filters, and one scan
// through the composed Application and Infrastructure with the stand-in as Syft (IR-16: scan-scoped lines on the scan's
// events).
public sealed class CompositionTests : IDisposable
{
    private const string PyyamlCpe = "cpe:2.3:a:pyyaml:pyyaml:5.3:*:*:*:*:*:*:*";

    private readonly TemporaryFolder folder = new();
    private readonly FakeNvdHandler nvd = new(TimeProvider.System);

    public static TheoryData<string, string, string> InvalidOptions => new()
    {
        { "Airlock:Nvd:BaseUrl", "http://services.nvd.nist.gov/rest/json/cves/2.0", "Airlock:Nvd:BaseUrl must be an absolute https URL" },
        { "Airlock:Nvd:RequestsPerWindow", "0", "Airlock:Nvd:RequestsPerWindow must be between 1 and 1,000" },
        { "Airlock:Syft:StatusCacheDuration", "00:00:00", "Airlock:Syft:StatusCacheDuration must be between 1 second and 1 hour" },
        { "Airlock:Staging:Root", "relative", "Airlock:Staging:Root must be a full path" },
    };

    [Fact]
    public async Task Every_port_resolves_to_its_Infrastructure_adapter()
    {
        await using var provider = Services(Configuration()).BuildServiceProvider();

        Assert.IsType<SyftSbomGenerator>(provider.GetRequiredService<ISbomGenerator>());
        Assert.IsType<NvdVulnerabilityCatalog>(provider.GetRequiredService<IVulnerabilityCatalog>());
        Assert.IsType<DiskArtifactStaging>(provider.GetRequiredService<IArtifactStaging>());
        Assert.IsType<InMemoryScanRegistry>(provider.GetRequiredService<IScanRegistry>());
        Assert.IsType<QuestPdfScanReportRenderer>(provider.GetRequiredService<IScanReportRenderer>());
        Assert.IsType<HostFileSystem>(provider.GetRequiredService<IHostFileSystem>());
        Assert.Same(provider.GetRequiredService<LiveLogBroadcaster>(), provider.GetRequiredService<ILiveLog>());
        Assert.Contains(provider.GetServices<ILoggerProvider>(), loggerProvider => loggerProvider is LiveLogLoggerProvider);
        Assert.Contains(provider.GetServices<IHostedService>(), service => service is StagingSweepService);
        Assert.Equal(QuestPDF.Infrastructure.LicenseType.Community, QuestPDF.Settings.License);
    }

    [Theory]
    [MemberData(nameof(InvalidOptions))]
    public async Task An_invalid_option_stops_startup_naming_it(string option, string value, string message)
    {
        await using var provider = Services(Configuration((option, value))).BuildServiceProvider();

        var failure = Assert.Throws<OptionsValidationException>(() =>
        {
            _ = provider.GetRequiredService<IOptions<NvdOptions>>().Value;
            _ = provider.GetRequiredService<IOptions<SyftOptions>>().Value;
            _ = provider.GetRequiredService<IOptions<StagingOptions>>().Value;
        });
        Assert.Contains(message, failure.Failures);
    }

    [Fact]
    public async Task Without_a_key_and_without_Syft_Airlock_still_starts_degraded()
    {
        var missing = folder.Combine("no-syft-here", SyftLocator.FileName);
        await using var provider = Services(Configuration(("Airlock:Nvd:ApiKey", null), ("Airlock:Syft:BinaryPath", missing))).BuildServiceProvider();

        Assert.False(provider.GetRequiredService<IVulnerabilityCatalog>().IsConfigured);
        var status = await provider.GetRequiredService<ISbomGenerator>().GetStatusAsync(Test.Token).Within();
        Assert.False(status.Available);
        Assert.Equal("Syft not found at " + missing, status.Detail);
    }

    [Fact]
    public async Task The_Live_Log_keeps_Airlocks_Debug_lines_when_the_console_is_set_to_Warning()
    {
        await using var provider = Services(Configuration(), LogLevel.Warning).BuildServiceProvider();
        var factory = provider.GetRequiredService<ILoggerFactory>();
        await using var subscription = provider.GetRequiredService<ILiveLog>().SubscribeAsync(Test.Token).GetAsyncEnumerator(Test.Token);
        var first = subscription.MoveNextAsync().AsTask();

        factory.CreateLogger("Microsoft.Test").Log(LogLevel.Warning, default, "not Airlock's", null, static (state, _) => state);
        factory.CreateLogger("Airlock.Test").Log(LogLevel.Debug, default, "Airlock's debug line", null, static (state, _) => state);

        Assert.True(await first.Within());
        Assert.Equal("Airlock's debug line", subscription.Current.Message);
    }

    [Fact]
    public async Task A_scan_runs_through_the_composed_layers_and_its_Infrastructure_lines_reach_its_events()
    {
        var target = folder.Combine("uploads", "acme-app");
        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "requirements.txt"), "flask==0.12.2\njinja2==2.10\npyyaml==5.3\nrequests==2.25.1\n", Test.Token);
        File.Copy(Fixtures.SyftOutput("manifest-file"), Path.Combine(target, "stand-in-output.json"));
        nvd.OnRecorded(PyyamlCpe, "cpe-pyyaml-5.3");
        await using var provider = Services(Configuration(("Airlock:AllowedScanRoots:0", folder.Combine("uploads")))).BuildServiceProvider();

        await using var reservation = Succeeded(await provider.GetRequiredService<StartScan>().ReserveAsync(Test.Token).Within());
        var started = Succeeded(await reservation.StartAsync(target, Test.Token).Within());
        var events = await CollectAsync(provider.GetRequiredService<WatchScan>(), started.Id).Within(60);

        var scan = provider.GetRequiredService<GetScan>().Execute(started.Id)!;
        Assert.Equal(ScanStatus.Completed, scan.Status);
        Assert.Equal(4, scan.Summary!.TotalPackages);
        Assert.Equal("pyyaml", Assert.Single(scan.VulnerablePackages).Package.Name);
        Assert.Equal(2, scan.Summary.CriticalFindings);
        Assert.Contains(events.OfType<ScanLogged>(), logged => logged.Entry.Category.StartsWith("Airlock.Infrastructure.", StringComparison.Ordinal));
        Assert.Empty(Directory.EnumerateDirectories(folder.Combine("staging")));
    }

    public void Dispose()
    {
        nvd.Dispose();
        folder.Dispose();
    }

    private static T Succeeded<T>(Outcome<T> outcome)
    {
        Assert.True(outcome.IsSuccess, outcome.Refusal?.Message);
        return outcome.Value!;
    }

    private static async Task<List<ScanEvent>> CollectAsync(WatchScan watch, ScanId id)
    {
        var events = new List<ScanEvent>();
        await foreach (var scanEvent in watch.SubscribeAsync(id, Test.Token))
        {
            events.Add(scanEvent);
        }

        return events;
    }

    private IConfiguration Configuration(params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["Airlock:Nvd:ApiKey"] = "test-key",
            ["Airlock:Nvd:RequestsPerWindow"] = "1000",
            ["Airlock:Nvd:Window"] = "00:00:01",
            ["Airlock:Syft:BinaryPath"] = StandIn.Path,
            ["Airlock:Staging:Root"] = folder.Combine("staging"),
        };
        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private ServiceCollection Services(IConfiguration configuration, LogLevel minimum = LogLevel.Information)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(minimum));
        services.AddOptions<AirlockOptions>().Bind(configuration.GetSection(AirlockOptions.SectionName));
        services.AddAirlockApplication();
        services.AddAirlockInfrastructure(configuration);
        services.AddHttpClient(NvdApiClient.ClientName).ConfigurePrimaryHttpMessageHandler(() => nvd);
        return services;
    }
}
