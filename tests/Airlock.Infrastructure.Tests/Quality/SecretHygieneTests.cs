using System.Text.Json;
using Airlock.Application;
using Airlock.Application.Ports;
using Airlock.Application.UseCases;
using Airlock.Domain.Intelligence;
using Airlock.Infrastructure.Nvd;
using Airlock.Infrastructure.Syft;
using Airlock.Infrastructure.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Airlock.Infrastructure.Tests.Quality;

// U3-NFR-07, IR-24 — the NVD key travels in the apiKey header and appears nowhere else.
public sealed class SecretHygieneTests : IDisposable
{
    private const string PyyamlCpe = "cpe:2.3:a:pyyaml:pyyaml:5.3:*:*:*:*:*:*:*";

    private readonly string secret = "nvd-key-" + Guid.NewGuid().ToString("N");
    private readonly FakeLogCollector logs = new();
    private readonly FakeNvdHandler nvd = new(TimeProvider.System);

    [Fact]
    public async Task Lookups_logged_at_Trace_in_every_category_never_show_the_key()
    {
        nvd.On(PyyamlCpe, () => FakeNvdHandler.Status(503), () => FakeNvdHandler.Recorded("cpe-pyyaml-5.3"));
        nvd.OnRecorded("cpe:2.3:a:foo", "cpe-malformed");
        nvd.On("cpe:2.3:a:broken:broken:1.0:*:*:*:*:*:*:*", () => FakeNvdHandler.Status(400));
        await using var provider = Services().BuildServiceProvider();
        var catalog = provider.GetRequiredService<IVulnerabilityCatalog>();

        var found = await catalog.FindByCpeAsync(new CandidateCpe(PyyamlCpe, CpeSource.Generated), Test.Token).Within(60);
        await catalog.FindByCpeAsync(new CandidateCpe("cpe:2.3:a:foo", CpeSource.Generated), Test.Token).Within();
        var failed = await catalog.FindByCpeAsync(new CandidateCpe("cpe:2.3:a:broken:broken:1.0:*:*:*:*:*:*:*", CpeSource.Generated), Test.Token).Within();

        Assert.IsType<CatalogFound>(found);
        Assert.DoesNotContain(secret, Assert.IsType<CatalogFailed>(failed).Reason, StringComparison.Ordinal);
        Assert.All(nvd.Requests, request => Assert.Equal(secret, request.ApiKey));
        var records = logs.GetSnapshot();
        Assert.Contains(records, record => record.Level == LogLevel.Trace && record.Category?.StartsWith("Airlock.Infrastructure.Nvd", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(records, record => record.Category?.StartsWith("System.Net.Http.HttpClient", StringComparison.Ordinal) == true);
        Assert.All(records, record =>
        {
            Assert.DoesNotContain(secret, record.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, record.Exception?.ToString() ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, string.Join(";", record.StructuredState ?? []), StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task The_health_details_say_only_whether_a_key_is_configured()
    {
        await using var provider = Services().BuildServiceProvider();

        var health = await provider.GetRequiredService<GetSystemHealth>().ExecuteAsync(Test.Token).Within();

        Assert.DoesNotContain(secret, JsonSerializer.Serialize(health), StringComparison.Ordinal);
        Assert.True(provider.GetRequiredService<IVulnerabilityCatalog>().IsConfigured);
    }

    [Fact]
    public async Task Syfts_environment_never_carries_the_key_by_name_or_by_value()
    {
        var name = "AIRLOCK_TEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name, secret);
        try
        {
            var request = new SyftEnvironment(secret).Request(StandIn.Path, ["env"], TimeSpan.FromMinutes(1));

            var run = await new SyftProcessRunner(TimeProvider.System).RunAsync(
                request, static async (output, token) => await new StreamReader(output).ReadToEndAsync(token), Test.Token).Within();

            Assert.Contains("NVD_API_KEY", request.HiddenNames);
            Assert.Contains("Airlock__Nvd__ApiKey", request.HiddenNames);
            Assert.DoesNotContain(secret, run.Output, StringComparison.Ordinal);
            Assert.DoesNotContain(name, run.Output, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    public void Dispose() => nvd.Dispose();

    private ServiceCollection Services()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Airlock:Nvd:ApiKey"] = secret,
                ["Airlock:Nvd:RequestsPerWindow"] = "1000",
                ["Airlock:Nvd:Window"] = "00:00:01",
                ["Airlock:Syft:BinaryPath"] = StandIn.Path,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(new FakeLoggerProvider(logs)));
        services.AddAirlockApplication();
        services.AddAirlockInfrastructure(configuration);
        services.AddHttpClient(NvdApiClient.ClientName).ConfigurePrimaryHttpMessageHandler(() => nvd);
        return services;
    }
}
