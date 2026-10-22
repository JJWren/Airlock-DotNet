using Airlock.Application.Correlation;
using Airlock.Application.Logging;
using Airlock.Application.Options;
using Airlock.Application.Scanning;
using Airlock.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Airlock.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the use cases, the scan services and the options validation. The host binds <see cref="AirlockOptions"/>
    /// from the <c>Airlock</c> section, and U3's <c>AddAirlockInfrastructure</c> registers the ports.
    /// </summary>
    public static IServiceCollection AddAirlockApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<AirlockOptions>().ValidateOnStart();
        services.AddSingleton<IValidateOptions<AirlockOptions>, AirlockOptionsValidator>();

        services.AddSingleton<ScanLogRouter>();
        services.AddSingleton<PackageCorrelator>();
        services.AddSingleton<ScanPipeline>();
        services.AddSingleton<ScanCoordinator>();

        services.AddSingleton<StartScan>();
        services.AddSingleton<CancelScan>();
        services.AddSingleton<EmergencyStop>();
        services.AddSingleton<GetScan>();
        services.AddSingleton<WatchScan>();
        services.AddSingleton<WatchActiveScan>();
        services.AddSingleton<RenderScanReport>();
        services.AddSingleton<GetSystemHealth>();
        services.AddSingleton<WatchLiveLog>();

        services.AddHostedService<ScanLifetimeService>();
        return services;
    }
}
