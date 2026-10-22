using System.Diagnostics;
using Airlock.Application.Logging;
using Airlock.Application.Options;
using Airlock.Application.Ports;
using Airlock.Application.Tests.Fakes;
using Airlock.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Airlock.Application.Tests.Quality;

// The values table of the U2 NFR Design, at both ends of every range (E13).
public sealed class OptionsValidationTests
{
    public static TheoryData<string, string, bool> Cases => new()
    {
        { "defaults", "", true },
        { "Limits:MaxArtifactsPerScan", "0", false },
        { "Limits:MaxArtifactsPerScan", "1", true },
        { "Limits:MaxArtifactsPerScan", "1000", true },
        { "Limits:MaxArtifactsPerScan", "1001", false },
        { "Limits:MaxBytesPerScan", "1048575", false },
        { "Limits:MaxBytesPerScan", "1048576", true },
        { "Timeouts:Scan", "00:00:59", false },
        { "Timeouts:Scan", "1.00:00:00", true },
        { "Timeouts:Scan", "1.00:00:01", false },
        { "Timeouts:SyftRun", "00:00:59", false },
        { "Timeouts:SyftRun", "01:00:00", true },
        { "Timeouts:SyftRun", "01:00:01", false },
        { "Timeouts:CancelGracePeriod", "-00:00:01", false },
        { "Timeouts:CancelGracePeriod", "00:00:00", true },
        { "Timeouts:CancelGracePeriod", "00:00:10", true },
        { "Timeouts:CancelGracePeriod", "00:00:11", false },
        { "Retention:Duration", "00:00:59", false },
        { "Retention:Duration", "30.00:00:00", true },
        { "Retention:Duration", "30.00:00:01", false },
        { "Retention:MaxScans", "0", false },
        { "Retention:MaxScans", "1000", true },
        { "Retention:MaxScans", "1001", false },
        { "Correlation:MaxParallelism", "0", false },
        { "Correlation:MaxParallelism", "1", true },
        { "Correlation:MaxParallelism", "16", true },
        { "Correlation:MaxParallelism", "17", false },
        { "Correlation:OutageThreshold", "2", false },
        { "Correlation:OutageThreshold", "3", true },
        { "Correlation:OutageThreshold", "20", true },
        { "Correlation:OutageThreshold", "21", false },
        { "AllowedScanRoots", "/uploads", true },
        { "AllowedScanRoots", "uploads", false },
        { "AllowedScanRoots", " ", false },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Each_option_is_checked_against_its_range(string option, string value, bool valid)
    {
        var options = new AirlockOptions();
        Set(options, option, value);

        var result = new AirlockOptionsValidator(new FakeHostFileSystem(HostMode.Linux)).Validate(null, options);

        Assert.Equal(valid, result.Succeeded);
        if (!valid)
        {
            Assert.Contains("Airlock:" + option, result.FailureMessage, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_message_names_the_option_and_its_range()
    {
        var options = new AirlockOptions();
        options.Correlation.MaxParallelism = 17;

        var result = new AirlockOptionsValidator(new FakeHostFileSystem(HostMode.Linux)).Validate(null, options);

        Assert.Equal("Airlock:Correlation:MaxParallelism must be between 1 and 16", result.FailureMessage);
        Assert.Throws<ArgumentNullException>(() => new AirlockOptionsValidator(new FakeHostFileSystem(HostMode.Linux)).Validate(null, null!));
    }

    private static void Set(AirlockOptions options, string option, string value)
    {
        switch (option)
        {
            case "Limits:MaxArtifactsPerScan": options.Limits.MaxArtifactsPerScan = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
            case "Limits:MaxBytesPerScan": options.Limits.MaxBytesPerScan = long.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
            case "Timeouts:Scan": options.Timeouts.Scan = TimeSpan.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
            case "Timeouts:SyftRun": options.Timeouts.SyftRun = TimeSpan.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
            case "Timeouts:CancelGracePeriod": options.Timeouts.CancelGracePeriod = TimeSpan.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
            case "Retention:Duration": options.Retention.Duration = TimeSpan.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
            case "Retention:MaxScans": options.Retention.MaxScans = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
            case "Correlation:MaxParallelism": options.Correlation.MaxParallelism = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
            case "Correlation:OutageThreshold": options.Correlation.OutageThreshold = int.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
            case "AllowedScanRoots": options.AllowedScanRoots = [value]; break;
            default: break;
        }
    }
}

// AR-25: text from outside Airlock.
public sealed class ExternalTextTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_text_reads_as_no_details(string? text)
    {
        Assert.Equal("no details given", ExternalText.Clean(text));
    }

    [Fact]
    public void Text_becomes_one_clean_line()
    {
        Assert.Equal("one two three", ExternalText.Clean($"one{(char)13}{(char)10}two{(char)9}{(char)9}three"));
        Assert.Equal("red text", ExternalText.Clean($"{(char)27}[1;31mred{(char)27}[0m text"));
        Assert.Equal("X", ExternalText.Clean($"{(char)27}cX"));
        Assert.Equal("abcdef", ExternalText.Clean($"abc{(char)0x202E}def{(char)7}"));
        Assert.Equal("no details given", ExternalText.Clean($"{(char)27}[0m{(char)0}"));
    }

    [Fact]
    public void Long_text_is_cut_with_an_ellipsis_without_splitting_a_character()
    {
        var cut = ExternalText.Clean(new string('x', 600));
        Assert.Equal(512, cut.Length);
        Assert.EndsWith("…", cut, StringComparison.Ordinal);

        var emoji = ExternalText.Clean(new string('x', 3) + "🚀🚀", 5);
        Assert.Equal("xxx…", emoji);
        Assert.Throws<ArgumentOutOfRangeException>(() => ExternalText.Clean("x", 1));
    }
}

// U2-NFR-10: the Application adds little to NVD's time.
public sealed class PerformanceSmokeTests
{
    [Fact]
    public async Task Ten_thousand_packages_correlate_well_within_ten_seconds()
    {
        using var harness = new ScanHarness(configure: options => options.Correlation.MaxParallelism = 16);
        harness.Sbom.Produces(
            ScanHarness.Directory,
            [.. Enumerable.Range(0, 10_000).Select(i => A.SbomPackage($"package-{i}", "1.0", "npm", $"vendor:package-{i}"))]);

        var watch = Stopwatch.StartNew();
        var scan = await harness.RunDirectoryScanAsync();
        watch.Stop();

        Assert.Equal(10_000, scan.Summary!.CleanPackages);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"The scan took {watch.Elapsed}.");
    }
}

// AddAirlockApplication wires every use case and checks the options at startup.
public sealed class CompositionTests
{
    [Fact]
    public async Task Every_use_case_resolves_and_invalid_options_stop_startup()
    {
        using var harness = new ScanHarness();
        await using var provider = Services(harness).BuildServiceProvider();
        var invalid = Services(harness);
        invalid.Configure<AirlockOptions>(options => options.Correlation.MaxParallelism = 0);
        await using var invalidProvider = invalid.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<StartScan>());
        Assert.NotNull(provider.GetRequiredService<CancelScan>());
        Assert.NotNull(provider.GetRequiredService<EmergencyStop>());
        Assert.NotNull(provider.GetRequiredService<GetScan>());
        Assert.NotNull(provider.GetRequiredService<WatchScan>());
        Assert.NotNull(provider.GetRequiredService<WatchActiveScan>());
        Assert.NotNull(provider.GetRequiredService<RenderScanReport>());
        Assert.NotNull(provider.GetRequiredService<GetSystemHealth>());
        Assert.NotNull(provider.GetRequiredService<WatchLiveLog>());
        Assert.Single(provider.GetServices<IHostedService>());
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
        var failure = Assert.Throws<OptionsValidationException>(() => invalidProvider.GetRequiredService<IOptions<AirlockOptions>>().Value);
        Assert.Contains("Airlock:Correlation:MaxParallelism must be between 1 and 16", failure.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => ApplicationServiceCollectionExtensions.AddAirlockApplication(null!));
    }

    private static IServiceCollection Services(ScanHarness harness) =>
        new ServiceCollection()
            .AddLogging()
            .AddSingleton<ISbomGenerator>(harness.Sbom)
            .AddSingleton<IVulnerabilityCatalog>(harness.Catalog)
            .AddSingleton<IArtifactStaging>(harness.Staging)
            .AddSingleton<IScanRegistry>(harness.Registry)
            .AddSingleton<IScanReportRenderer>(harness.Renderer)
            .AddSingleton<ILiveLog>(new FakeLiveLog())
            .AddSingleton<IHostFileSystem>(harness.FileSystem)
            .AddAirlockApplication();
}
