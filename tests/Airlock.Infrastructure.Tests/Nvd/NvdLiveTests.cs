using Airlock.Application;
using Airlock.Application.Ports;
using Airlock.Domain.Intelligence;
using Airlock.Infrastructure.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Airlock.Infrastructure.Tests.Nvd;

// The real NVD, without a key (Category=Network): run only when AIRLOCK_LIVE_NVD=1, once in Code Generation's checks.
[Trait("Category", "Network")]
public sealed class NvdLiveTests
{
    [Fact]
    public async Task Pyyaml_5_3_has_its_two_CVEs_on_the_real_NVD()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("AIRLOCK_LIVE_NVD") == "1", "Set AIRLOCK_LIVE_NVD=1 to ask the real NVD.");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAirlockApplication();
        services.AddAirlockInfrastructure(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<IVulnerabilityCatalog>();

        var lookup = await catalog.FindByCpeAsync(
            new CandidateCpe("cpe:2.3:a:pyyaml:pyyaml:5.3:*:*:*:*:*:*:*", CpeSource.Generated), Test.Token).Within(150);

        var records = Assert.IsType<CatalogFound>(lookup).Records;
        Assert.Contains(records, record => record.Id == "CVE-2020-1747");
        Assert.Contains(records, record => record.Id == "CVE-2020-14343");
    }
}
