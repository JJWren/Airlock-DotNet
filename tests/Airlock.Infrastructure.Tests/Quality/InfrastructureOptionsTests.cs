using Airlock.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Airlock.Infrastructure.Tests.Quality;

// IR-22, IR-23 — the values table of the U3 NFR Design at both ends of every range, and the original variable names.
public sealed class InfrastructureOptionsTests
{
    public static TheoryData<string, string, bool> Cases => new()
    {
        { "defaults", string.Empty, true },
        { "Nvd:BaseUrl", "https://services.nvd.nist.gov/rest/json/cves/2.0", true },
        { "Nvd:BaseUrl", "http://services.nvd.nist.gov/rest/json/cves/2.0", false },
        { "Nvd:BaseUrl", "ftp://example.org/", false },
        { "Nvd:RequestsPerWindow", "0", false },
        { "Nvd:RequestsPerWindow", "1", true },
        { "Nvd:RequestsPerWindow", "1000", true },
        { "Nvd:RequestsPerWindow", "1001", false },
        { "Nvd:Window", "00:00:00.999", false },
        { "Nvd:Window", "00:00:01", true },
        { "Nvd:Window", "01:00:00", true },
        { "Nvd:Window", "01:00:01", false },
        { "Syft:BinaryPath", " ", false },
        { "Syft:BinaryPath", "tools/syft", true },
        { "Syft:StatusCacheDuration", "00:00:00", false },
        { "Syft:StatusCacheDuration", "00:00:01", true },
        { "Syft:StatusCacheDuration", "01:00:00", true },
        { "Syft:StatusCacheDuration", "01:00:01", false },
        { "Staging:Root", "relative/staging", false },
        { "Staging:Root", " ", false },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Each_option_is_checked_against_its_range(string option, string value, bool valid)
    {
        var failures = Validate(option, value);

        if (valid)
        {
            Assert.Empty(failures);
        }
        else
        {
            Assert.StartsWith("Airlock:" + option + " must ", Assert.Single(failures), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_default_staging_root_is_a_full_path_under_the_systems_temporary_folder()
    {
        var root = new StagingOptions().Root;

        Assert.True(Path.IsPathFullyQualified(root));
        Assert.StartsWith(Path.GetTempPath(), root, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Validate("Staging:Root", root));
    }

    [Fact]
    public void The_original_variable_names_map_onto_the_options()
    {
        var configuration = Configure(new()
        {
            ["NVD_API_KEY"] = "key-from-environment",
            ["SYFT_BINARY_PATH"] = "/opt/syft/syft",
            ["LOG_LEVEL"] = "WARNING",
        });

        Assert.Equal("key-from-environment", configuration["Airlock:Nvd:ApiKey"]);
        Assert.Equal("/opt/syft/syft", configuration["Airlock:Syft:BinaryPath"]);
        Assert.Equal("Warning", configuration["Logging:LogLevel:Default"]);
    }

    [Fact]
    public void Blank_variables_are_ignored()
    {
        var configuration = Configure(new() { ["NVD_API_KEY"] = " ", ["SYFT_BINARY_PATH"] = string.Empty, ["LOG_LEVEL"] = " " });

        Assert.Null(configuration["Airlock:Nvd:ApiKey"]);
        Assert.Null(configuration["Airlock:Syft:BinaryPath"]);
        Assert.Null(configuration["Logging:LogLevel:Default"]);
    }

    [Theory]
    [InlineData("DEBUG", LogLevel.Debug)]
    [InlineData("info", LogLevel.Information)]
    [InlineData("INFO", LogLevel.Information)]
    [InlineData("Information", LogLevel.Information)]
    [InlineData(" warning ", LogLevel.Warning)]
    [InlineData("ERROR", LogLevel.Error)]
    [InlineData("CRITICAL", LogLevel.Critical)]
    [InlineData("Trace", LogLevel.Trace)]
    [InlineData("none", LogLevel.None)]
    public void LOG_LEVEL_accepts_the_originals_names_and_NETs(string value, LogLevel level)
    {
        Assert.Equal(level.ToString(), Configure(new() { ["LOG_LEVEL"] = value })["Logging:LogLevel:Default"]);
    }

    [Theory]
    [InlineData("VERBOSE")]
    [InlineData("3")]
    public void Any_other_LOG_LEVEL_stops_startup_naming_it(string value)
    {
        var failure = Assert.Throws<InvalidOperationException>(() => Configure(new() { ["LOG_LEVEL"] = value }));

        Assert.StartsWith("LOG_LEVEL must be ", failure.Message, StringComparison.Ordinal);
        Assert.Contains(value, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_public_extension_reads_the_processs_environment()
    {
        var configuration = new ConfigurationBuilder().AddAirlockEnvironmentVariables().Build();

        Assert.Equal(Environment.GetEnvironmentVariable("SYFT_BINARY_PATH") is { Length: > 0 } path ? path : null, configuration["Airlock:Syft:BinaryPath"]);
        Assert.Throws<ArgumentNullException>(() => AirlockConfigurationBuilderExtensions.AddAirlockEnvironmentVariables(null!));
    }

    private static IConfiguration Configure(Dictionary<string, string?> variables) =>
        new ConfigurationBuilder().Add(new AirlockEnvironmentVariables(name => variables.GetValueOrDefault(name))).Build();

    private static IReadOnlyList<string> Validate(string option, string value)
    {
        var section = option.Split(':')[0];
        var result = section switch
        {
            "Nvd" => new NvdOptionsValidator().Validate(null, Bind<NvdOptions>(option, value)),
            "Syft" => new SyftOptionsValidator().Validate(null, Bind<SyftOptions>(option, value)),
            "Staging" => new StagingOptionsValidator().Validate(null, Bind<StagingOptions>(option, value)),
            _ => Combine(
                new NvdOptionsValidator().Validate(null, new NvdOptions()),
                new SyftOptionsValidator().Validate(null, new SyftOptions()),
                new StagingOptionsValidator().Validate(null, new StagingOptions())),
        };
        return result.Failed ? [.. result.Failures!] : [];
    }

    private static ValidateOptionsResult Combine(params ValidateOptionsResult[] results) =>
        results.Any(result => result.Failed)
            ? ValidateOptionsResult.Fail(results.Where(result => result.Failed).SelectMany(result => result.Failures!))
            : ValidateOptionsResult.Success;

    private static T Bind<T>(string option, string value)
        where T : new()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [option.Split(':')[1]] = value })
            .Build();
        var options = new T();
        configuration.Bind(options);
        return options;
    }
}
