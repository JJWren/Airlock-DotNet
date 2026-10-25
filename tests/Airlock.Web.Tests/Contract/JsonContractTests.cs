using System.Text;
using System.Text.Json;
using Airlock.Domain.Intelligence;
using Airlock.Domain.Results;
using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;
using Airlock.Web.Api.Contracts;
using Airlock.Web.Api.Mapping;
using Airlock.Web.Tests.Support;

namespace Airlock.Web.Tests.Contract;

// WR-02, WR-11 — the JSON the API writes, byte for byte where the design shows it.
public sealed class JsonContractTests
{
    private static readonly JsonSerializerOptions HttpOptions = CreateHttpOptions();

    [Fact]
    public void The_worked_example_is_written_byte_for_byte()
    {
        var expected = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "worked-example-scan.json")).Replace("\r\n", "\n", StringComparison.Ordinal);

        var written = JsonSerializer.Serialize(ScanDtoMapper.ToDto(WorkedExample.Finished()), HttpOptions);

        Assert.Equal(expected, written);
    }

    [Fact]
    public void A_running_scan_has_every_property_with_null_and_empty_values()
    {
        var json = JsonSerializer.Serialize(ScanDtoMapper.ToDto(WorkedExample.Running()), HttpOptions);

        Assert.Contains("\"status\": \"RUNNING\"", json, StringComparison.Ordinal);
        Assert.Contains("\"phase\": \"CORRELATION\"", json, StringComparison.Ordinal);
        foreach (var empty in new[] { "\"finishedAt\": null", "\"durationSeconds\": null", "\"summary\": null", "\"failure\": null", "\"cancellationReason\": null", "\"vulnerablePackages\": []", "\"unverifiedPackages\": []", "\"warnings\": []" })
        {
            Assert.Contains(empty, json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Times_are_utc_with_milliseconds_and_z()
    {
        var local = new DateTimeOffset(2026, 10, 22, 5, 0, 0, TimeSpan.FromHours(-7)).AddTicks(1_234_567);

        Assert.Equal("2026-10-22T12:00:00.123Z", ApiTime.Format(local));
        Assert.Equal("2026-10-22T12:00:00.000Z", ApiTime.Format(new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void A_duration_always_has_three_decimal_places()
    {
        var scan = Scan.Start(WorkedExample.Id, WorkedExample.Target, Test.StartTime).Fail(
            new ScanFailure(ScanFailureCode.SyftFailed, "Syft exited with code 1: no", "requirements.txt"), Test.StartTime.AddSeconds(92).AddTicks(4_999));

        var json = JsonSerializer.Serialize(ScanDtoMapper.ToDto(scan), HttpOptions);

        Assert.Contains("\"durationSeconds\": 92.000", json, StringComparison.Ordinal);
        Assert.Contains("\"code\": \"SYFT_FAILED\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ScanStatusValue.Incomplete, "INCOMPLETE")]
    [InlineData(ScanPhaseValue.SbomGeneration, "SBOM_GENERATION")]
    [InlineData(SeverityValue.Critical, "CRITICAL")]
    [InlineData(UnverifiedReasonValue.LookupFailed, "LOOKUP_FAILED")]
    [InlineData(WarningCodeValue.DistributionBackportsNotConsidered, "DISTRIBUTION_BACKPORTS_NOT_CONSIDERED")]
    [InlineData(HealthStatusValue.Online, "ONLINE")]
    [InlineData(StopStatusValue.Terminated, "TERMINATED")]
    [InlineData(LogLevelValue.Info, "INFO")]
    public void Values_are_written_in_capitals_with_underscores(object value, string expected)
    {
        Assert.Equal('"' + expected + '"', JsonSerializer.Serialize(value, value.GetType(), HttpOptions));
    }

    [Fact]
    public void Every_contract_value_is_capitals_and_underscores()
    {
        var enums = typeof(ScanDto).Assembly.GetTypes().Where(type => type.IsEnum && type.Namespace == typeof(ScanDto).Namespace);
        foreach (var type in enums)
        {
            foreach (var value in Enum.GetValues(type))
            {
                var written = JsonSerializer.Serialize(value, type, HttpOptions).Trim('"');
                Assert.Matches("^[A-Z][A-Z_]*$", written);
            }
        }
    }

    [Fact]
    public void Text_is_written_as_it_is_escaping_only_what_json_needs()
    {
        var cve = new Cve("CVE-2023-30861", "One client's `session` cookie \"leaks\" ⚠️ <tag> & more\nsecond line", [new CvssScore(7.5m, Severity.High, CvssVersion.V31, CvssSource.Primary)], []);
        var vulnerable = new VulnerablePackage(new Package("python", "flask", "0.12.2"), new ProductMatch("flask"), Finding.Select([cve]).Findings);
        var scan = Scan.Start(WorkedExample.Id, WorkedExample.Target, Test.StartTime).EnterPhase(ScanPhase.Correlation)
            .RecordProgress(1, 1).EnterPhase(ScanPhase.Summary).Finish(1, [vulnerable], [], Test.StartTime);

        var json = JsonSerializer.Serialize(ScanDtoMapper.ToDto(scan), HttpOptions);

        Assert.Contains("One client's `session` cookie \\\"leaks\\\" ⚠️ <tag> & more\\nsecond line", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Event_data_is_one_compact_line()
    {
        var data = Encoding.UTF8.GetString(ApiJson.SerializeCompact(ScanDtoMapper.ToDto(WorkedExample.Finished())));

        Assert.DoesNotContain('\n', data);
        Assert.StartsWith("{\"id\":\"01a1637b-7e00-7c3a-9f12-6b84d0e5a1c7\",\"status\":\"INCOMPLETE\"", data, StringComparison.Ordinal);
    }

    [Fact]
    public void A_directory_target_has_its_path_and_no_artifacts()
    {
        var scan = Scan.Start(WorkedExample.Id, new DirectoryTarget("/uploads/acme-app"), Test.StartTime);

        var target = ScanDtoMapper.ToDto(scan).Target;

        Assert.Equal(TargetKindValue.Directory, target.Kind);
        Assert.Equal("/uploads/acme-app", target.Path);
        Assert.Empty(target.Artifacts);
    }

    private static JsonSerializerOptions CreateHttpOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        ApiJson.Configure(options);
        options.MakeReadOnly();
        return options;
    }
}
