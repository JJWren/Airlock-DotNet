using System.Net;
using Airlock.Web.Tests.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Airlock.Web.Tests.Host;

// WR-23, U4-NFR-10 — the real composition starts with its defaults, validates its options and starts DEGRADED (E13).
public sealed class HostCompositionTests
{
    [Fact]
    public async Task The_real_host_starts_with_its_defaults_and_without_syft_or_a_key_is_degraded()
    {
        await using var factory = new AirlockWebFactory(settings => settings["Airlock:Nvd:ApiKey"] = string.Empty, realAdapters: true);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/v2/system/health", Test.Token);
        var health = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("DEGRADED", health.GetProperty("status").GetString());
        var checks = health.GetProperty("checks");
        Assert.StartsWith("Syft not found at ", checks[0].GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal("NVD API key not configured", checks[1].GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("Airlock:Limits:MaxArtifactsPerScan", "-1")]
    [InlineData("Airlock:Nvd:BaseUrl", "http://services.nvd.nist.gov/rest/json/cves/2.0")]
    [InlineData("Airlock:Staging:Root", "relative/staging")]
    [InlineData("Airlock:DataProtection:KeysPath", "relative/keys")]
    public async Task An_invalid_option_stops_startup_naming_it(string key, string value)
    {
        using var staging = new TemporaryFolder();
        await using var app = Compose(staging.Path, $"--{key}={value}");

        var startup = await Assert.ThrowsAnyAsync<Exception>(() => app.StartAsync(Test.Token));

        Assert.Contains(key, Flatten(startup), StringComparison.Ordinal);
    }

    // U4-NFR-09: one Airlock per staging root; the lock goes when the first one stops.
    [Fact]
    public async Task A_second_airlock_on_a_held_staging_root_stops_naming_it()
    {
        using var staging = new TemporaryFolder();
        await using var first = Compose(staging.Path);
        await first.StartAsync(Test.Token);

        await using var second = Compose(staging.Path);
        var refused = await Assert.ThrowsAnyAsync<Exception>(() => second.StartAsync(Test.Token));
        Assert.Contains($"Another Airlock is using the staging root {staging.Path}", Flatten(refused), StringComparison.Ordinal);

        await first.StopAsync(Test.Token);
        await using var third = Compose(staging.Path);
        await third.StartAsync(Test.Token);
        await third.StopAsync(Test.Token);
    }

    [Fact]
    public async Task The_original_variable_names_rank_above_the_environment_and_below_the_command_line()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        var providers = ((IConfigurationRoot)factory.Services.GetRequiredService<IConfiguration>()).Providers.ToList();

        var airlock = providers.FindIndex(provider => provider.GetType().DeclaringType?.Name == "AirlockEnvironmentVariables");
        var json = providers.FindLastIndex(provider => provider is Microsoft.Extensions.Configuration.Json.JsonConfigurationProvider);
        var environment = providers.FindLastIndex(provider => provider is EnvironmentVariablesConfigurationProvider);
        var commandLine = providers.FindLastIndex(provider => provider is CommandLineConfigurationProvider);
        Assert.True(airlock > json, $"The original names come at {airlock}, the last JSON file at {json}.");

        var order = string.Join(", ", providers.Select(provider => provider.GetType().Name));
        Assert.True(airlock > environment, $"The original names come at {airlock}, the environment at {environment}: {order}.");
        Assert.True(commandLine < 0 || airlock < commandLine, $"The original names come at {airlock}, the command line at {commandLine}: {order}.");
    }

    [Fact]
    public void Moving_the_last_source_puts_it_in_front_of_the_last_command_line()
    {
        var hostCommandLine = new CommandLineConfigurationSource { Args = [] };
        var json = new MemoryConfigurationSourceMarker();
        var environment = new MemoryConfigurationSourceMarker();
        var appCommandLine = new CommandLineConfigurationSource { Args = [] };
        var airlock = new MemoryConfigurationSourceMarker();
        List<IConfigurationSource> sources = [hostCommandLine, json, environment, appCommandLine, airlock];

        Web.ConfigurationOrder.MoveLastBeforeCommandLine(sources);

        Assert.Equal([hostCommandLine, json, environment, airlock, appCommandLine], sources);
    }

    [Fact]
    public void Without_a_command_line_the_sources_stay_as_they_are()
    {
        var first = new MemoryConfigurationSourceMarker();
        var last = new MemoryConfigurationSourceMarker();
        List<IConfigurationSource> sources = [first, last];

        Web.ConfigurationOrder.MoveLastBeforeCommandLine(sources);

        Assert.Equal([first, last], sources);
    }

    [Fact]
    public async Task Kestrel_keeps_a_small_body_limit_and_sends_no_server_header()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();

        var kestrel = factory.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>>().Value;

        Assert.Equal(1024 * 1024, kestrel.Limits.MaxRequestBodySize);
        Assert.False(kestrel.AddServerHeader);
    }

    // The real composition on a loopback port of its own, started by the test rather than by WebApplicationFactory: when
    // startup fails, the application's own thread disposes the host, and the factory's deferred host can then report the
    // disposed service provider instead of the reason.
    private static WebApplication Compose(string stagingRoot, params string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            // As Program's own builder is named, so MapStaticAssets finds Airlock.Web's manifest (U5).
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = "Production",
            Args = ["--urls=http://127.0.0.1:0", $"--Airlock:Staging:Root={stagingRoot}", .. args],
        });
        builder.Logging.ClearProviders();
        AirlockHost.ConfigureServices(builder);
        var app = builder.Build();
        AirlockHost.ConfigurePipeline(app);
        return app;
    }

    private static string Flatten(Exception exception) =>
        exception is AggregateException aggregate
            ? string.Join(" | ", aggregate.InnerExceptions.Select(Flatten))
            : exception.Message + (exception.InnerException is { } inner ? " | " + Flatten(inner) : string.Empty);

    private sealed class MemoryConfigurationSourceMarker : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => throw new NotSupportedException();
    }
}
