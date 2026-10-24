using System.Net;
using System.Net.Http.Headers;
using Airlock.Application.Ports;
using Airlock.Infrastructure.FileSystem;
using Airlock.Infrastructure.Logging;
using Airlock.Infrastructure.Nvd;
using Airlock.Infrastructure.Options;
using Airlock.Infrastructure.Reports;
using Airlock.Infrastructure.Scans;
using Airlock.Infrastructure.Staging;
using Airlock.Infrastructure.Syft;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Airlock.Infrastructure;

/// <summary>Composition of the Infrastructure layer.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the adapters behind the Application's ports, their options with validation, the NVD client and the Live
    /// Log provider. Call it after <c>AddAirlockApplication</c>, which registers the <c>ScanLogRouter</c> the provider uses.
    /// </summary>
    public static IServiceCollection AddAirlockInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);

        // IR-22: validated at startup; an invalid value stops it, naming the option.
        services.AddOptions<NvdOptions>().Bind(configuration.GetSection(NvdOptions.SectionName)).ValidateOnStart();
        services.AddOptions<SyftOptions>().Bind(configuration.GetSection(SyftOptions.SectionName)).ValidateOnStart();
        services.AddOptions<StagingOptions>().Bind(configuration.GetSection(StagingOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<NvdOptions>, NvdOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SyftOptions>, SyftOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<StagingOptions>, StagingOptionsValidator>());

        // IR-07, IR-24: the pipeline and the lookup deadline own time; answers come compressed. The client factory's own
        // logging is off: its formatted messages redact headers, but the structured state it gives every provider holds
        // the raw values, the apiKey header among them. NvdLog records each request without headers instead.
        var version = typeof(InfrastructureServiceCollectionExtensions).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        services.AddHttpClient(NvdApiClient.ClientName, client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Airlock", version));
            })
            .UseSocketsHttpHandler((handler, _) => handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Brotli)
            .RemoveAllLoggers()
            .RedactLoggedHeaders(_ => true);

        services.AddSingleton<ISbomGenerator, SyftSbomGenerator>();
        services.AddSingleton<IVulnerabilityCatalog, NvdVulnerabilityCatalog>();
        services.AddSingleton<IArtifactStaging, DiskArtifactStaging>();
        services.AddSingleton<IScanRegistry, InMemoryScanRegistry>();
        services.AddSingleton<IScanReportRenderer, QuestPdfScanReportRenderer>();
        services.AddSingleton<IHostFileSystem, HostFileSystem>();
        services.AddSingleton<LiveLogBroadcaster>();
        services.AddSingleton<ILiveLog>(provider => provider.GetRequiredService<LiveLogBroadcaster>());
        services.AddHostedService<StagingSweepService>();

        // IR-15, IR-18: the Live Log keeps Airlock's own Debug lines whatever LOG_LEVEL says, and nothing else.
        services.AddLogging(logging =>
        {
            logging.AddFilter<LiveLogLoggerProvider>(null, LogLevel.None);
            logging.AddFilter<LiveLogLoggerProvider>(LiveLogLoggerProvider.Category, LogLevel.Debug);
        });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, LiveLogLoggerProvider>());

        QuestPdfSettings.Ensure();
        return services;
    }
}
