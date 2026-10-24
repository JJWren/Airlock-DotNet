using Airlock.Domain.Intelligence;
using Airlock.Infrastructure.Nvd;
using Airlock.Infrastructure.Options;
using Airlock.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Airlock.Infrastructure.Tests.Nvd;

// IR-07, IR-08 — the recorded S0 answers through a fake HTTP handler.
public sealed class NvdApiClientTests : IDisposable
{
    private const string PyyamlCpe = "cpe:2.3:a:pyyaml:pyyaml:5.3:*:*:*:*:*:*:*";

    private readonly FakeTimeProvider clock = new(Test.StartTime);
    private readonly FakeLogger logger = new();
    private readonly FakeNvdHandler nvd;
    private readonly NvdRequestPacer pacer;

    public NvdApiClientTests()
    {
        nvd = new FakeNvdHandler(clock);
        pacer = new NvdRequestPacer(TimeSpan.FromMilliseconds(600), clock);
    }

    [Fact]
    public async Task A_CPE_lookup_asks_for_the_cpeName_without_rejected_records_in_pages_of_2000()
    {
        nvd.OnRecorded(PyyamlCpe, "cpe-pyyaml-5.3");

        var answer = await Client().GetPageAsync(Cpe(PyyamlCpe), 0, Test.Token).Within();

        Assert.Equal(2, Assert.IsType<NvdPage>(answer).Response.TotalResults);
        var request = Assert.Single(nvd.Requests);
        Assert.StartsWith("https://services.nvd.nist.gov/rest/json/cves/2.0?cpeName=", request.Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.EndsWith("&noRejected&resultsPerPage=2000&startIndex=0", request.Uri.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal(("cpeName", PyyamlCpe), (request.Parameter, request.Value));
    }

    [Fact]
    public async Task The_key_travels_in_the_apiKey_header()
    {
        await Client("my-key").GetPageAsync(Cpe(PyyamlCpe), 0, Test.Token).Within();

        var request = Assert.Single(nvd.Requests);
        Assert.Equal("my-key", request.ApiKey);
        Assert.DoesNotContain("my-key", request.Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_key_no_apiKey_header_is_sent()
    {
        await Client(null).GetPageAsync(Cpe(PyyamlCpe), 0, Test.Token).Within();

        Assert.Null(Assert.Single(nvd.Requests).ApiKey);
    }

    [Fact]
    public async Task A_product_match_asks_for_the_product_from_any_vendor()
    {
        nvd.OnRecorded("cpe:2.3:a:*:minimist:1.2.5:*:*:*:*:*:*:*", "product-minimist-1.2.5");

        var answer = await Client().GetPageAsync(NvdQuery.ForProduct("minimist", "1.2.5"), 0, Test.Token).Within();

        Assert.Equal("CVE-2021-44906", Assert.Single(Assert.IsType<NvdPage>(answer).Response.Vulnerabilities!).Cve!.Id);
        Assert.Equal("virtualMatchString", Assert.Single(nvd.Requests).Parameter);
    }

    [Fact]
    public async Task A_malformed_CPE_is_no_records_and_a_warning()
    {
        nvd.OnRecorded("cpe:2.3:a:foo", "cpe-malformed");

        var answer = await Client().GetPageAsync(Cpe("cpe:2.3:a:foo"), 0, Test.Token).Within();

        Assert.IsType<NvdNotFound>(answer);
        var warning = Assert.Single(logger.Collector.GetSnapshot(), record => record.Level == LogLevel.Warning);
        Assert.Contains("Invalid cpeName parameter", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_503_on_every_attempt_fails_after_five_attempts()
    {
        nvd.On(PyyamlCpe, () => FakeNvdHandler.Status(503));

        var answer = await clock.DriveAsync(Client().GetPageAsync(Cpe(PyyamlCpe), 0, Test.Token), TimeSpan.FromMilliseconds(500));

        Assert.Equal("NVD returned 503", Assert.IsType<NvdFailed>(answer).Reason);
        Assert.Equal(NvdPipeline.MaxAttempts, nvd.Requests.Count);
    }

    [Fact]
    public async Task A_429_with_Retry_After_0_is_retried_after_the_own_minimum_backoff()
    {
        nvd.On(PyyamlCpe, () => FakeNvdHandler.Recorded("throttled"), () => FakeNvdHandler.Recorded("cpe-pyyaml-5.3"));

        var answer = await clock.DriveAsync(Client().GetPageAsync(Cpe(PyyamlCpe), 0, Test.Token), TimeSpan.FromMilliseconds(100));

        Assert.IsType<NvdPage>(answer);
        var requests = nvd.Requests;
        Assert.Equal(2, requests.Count);
        Assert.True(requests[1].At - requests[0].At >= TimeSpan.FromSeconds(1.6));
        Assert.Contains(logger.Collector.GetSnapshot(), record => record.Message.Contains("answered 429; retrying", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_body_that_is_not_NVD_JSON_fails_without_a_retry()
    {
        nvd.On(PyyamlCpe, () => FakeNvdHandler.Json("{ this is not json"));

        var answer = await Client().GetPageAsync(Cpe(PyyamlCpe), 0, Test.Token).Within();

        Assert.StartsWith("NVD's answer could not be read: ", Assert.IsType<NvdFailed>(answer).Reason, StringComparison.Ordinal);
        Assert.Single(nvd.Requests);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(302)]
    public async Task Any_other_status_fails_without_a_retry(int status)
    {
        nvd.On(PyyamlCpe, () => FakeNvdHandler.Status(status));

        var answer = await Client().GetPageAsync(Cpe(PyyamlCpe), 0, Test.Token).Within();

        Assert.Equal($"NVD returned {status}", Assert.IsType<NvdFailed>(answer).Reason);
        Assert.Single(nvd.Requests);
    }

    [Fact]
    public async Task A_network_error_on_every_attempt_says_NVD_could_not_be_reached()
    {
        nvd.On(PyyamlCpe, _ => throw new HttpRequestException("No such host is known."));

        var answer = await clock.DriveAsync(Client().GetPageAsync(Cpe(PyyamlCpe), 0, Test.Token), TimeSpan.FromMilliseconds(500));

        Assert.Equal("NVD could not be reached: No such host is known.", Assert.IsType<NvdFailed>(answer).Reason);
        Assert.Equal(NvdPipeline.MaxAttempts, nvd.Requests.Count);
    }

    [Fact]
    public async Task A_body_cut_off_on_every_attempt_says_so()
    {
        nvd.On(PyyamlCpe, _ => throw new IOException("The response ended prematurely."));

        var answer = await clock.DriveAsync(Client().GetPageAsync(Cpe(PyyamlCpe), 0, Test.Token), TimeSpan.FromMilliseconds(500));

        Assert.Equal("NVD's answer was cut off: The response ended prematurely.", Assert.IsType<NvdFailed>(answer).Reason);
    }

    [Theory]
    [InlineData("minimist", "minimist")]
    [InlineData("1.36.1-r31", "1.36.1-r31")]
    [InlineData("python_dateutil", "python_dateutil")]
    [InlineData("foo:bar", @"foo\:bar")]
    [InlineData("a b", @"a\ b")]
    [InlineData("x*", @"x\*")]
    [InlineData("1.0.0+build.1", @"1.0.0\+build.1")]
    public void Products_and_versions_are_escaped_as_CPE_2_3_formatted_strings(string value, string escaped)
    {
        Assert.Equal(escaped, CpeMatchString.Escape(value));
    }

    [Fact]
    public void A_product_match_names_any_vendor()
    {
        Assert.Equal("cpe:2.3:a:*:busybox:1.36.1-r31:*:*:*:*:*:*:*", CpeMatchString.AnyVendor("busybox", "1.36.1-r31"));
    }

    public void Dispose()
    {
        pacer.Dispose();
        nvd.Dispose();
    }

    private static NvdQuery Cpe(string value) => NvdQuery.ForCpe(new CandidateCpe(value, CpeSource.Generated));

    private NvdApiClient Client(string? apiKey = "test-key") =>
        new(new TestHttpClientFactory(nvd), new NvdOptions { ApiKey = apiKey }, NvdPipeline.Build(pacer, clock, () => 0.5, logger), clock, logger);
}
