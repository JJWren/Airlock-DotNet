using Airlock.Application.Ports;
using Airlock.Domain.Intelligence;
using Airlock.Infrastructure.Nvd;
using Airlock.Infrastructure.Options;
using Airlock.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Airlock.Infrastructure.Tests.Nvd;

// U3-NFR-02 and U3-NFR-03 — retries, backoff, Retry-After, the attempt timeout, and the pace under load (defect 9).
public sealed class NvdResilienceTests : IDisposable
{
    private const string PyyamlCpe = "cpe:2.3:a:pyyaml:pyyaml:5.3:*:*:*:*:*:*:*";

    private readonly FakeTimeProvider clock = new(Test.StartTime);
    private readonly FakeLogger logger = new();
    private readonly FakeNvdHandler nvd;

    public NvdResilienceTests() => nvd = new FakeNvdHandler(clock);

    public static TheoryData<int, double?, double, double> Delays => new()
    {
        { 0, null, 0.0, 1.6 },
        { 0, null, 0.5, 2.0 },
        { 1, null, 0.5, 4.0 },
        { 2, null, 0.5, 8.0 },
        { 3, null, 0.5, 16.0 },
        { 4, null, 0.5, 30.0 },
        { 9, null, 1.0, 36.0 },
        { 0, 0.0, 0.5, 2.0 },
        { 0, 0.5, 0.5, 2.0 },
        { 0, 7.0, 0.5, 7.0 },
        { 2, 1.0, 0.5, 1.0 },
        { 0, 120.0, 0.5, 60.0 },
    };

    [Theory]
    [MemberData(nameof(Delays))]
    public void The_delay_is_a_usable_Retry_After_or_a_doubling_backoff_with_jitter(int retry, double? retryAfter, double random, double seconds)
    {
        var delay = NvdPipeline.Delay(retry, retryAfter is { } after ? TimeSpan.FromSeconds(after) : null, () => random);

        Assert.Equal(seconds, delay.TotalSeconds, 6);
    }

    [Fact]
    public async Task A_Retry_After_of_7_seconds_is_honoured()
    {
        nvd.On(PyyamlCpe, () => FakeNvdHandler.Status(429, "7"), () => FakeNvdHandler.Recorded("cpe-pyyaml-5.3"));

        var answer = await clock.DriveAsync(Client().GetPageAsync(NvdQuery.ForCpe(Cpe(PyyamlCpe)), 0, Test.Token), TimeSpan.FromMilliseconds(100));

        Assert.IsType<NvdPage>(answer);
        var gap = nvd.Requests[1].At - nvd.Requests[0].At;
        Assert.InRange(gap, TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(7.2));
    }

    [Fact]
    public async Task An_attempt_that_stalls_in_its_body_times_out_and_is_retried()
    {
        nvd.On(PyyamlCpe, () => FakeNvdHandler.Stalled());

        var answer = await clock.DriveAsync(Client().GetPageAsync(NvdQuery.ForCpe(Cpe(PyyamlCpe)), 0, Test.Token), TimeSpan.FromSeconds(1));

        Assert.Equal("NVD did not answer in time", Assert.IsType<NvdFailed>(answer).Reason);
        Assert.Equal(NvdPipeline.MaxAttempts, nvd.Requests.Count);
        Assert.True(nvd.Requests[1].At - nvd.Requests[0].At >= NvdPipeline.AttemptTimeout);
    }

    [Fact]
    public async Task A_stalled_body_that_recovers_answers_on_the_next_attempt()
    {
        nvd.On(PyyamlCpe, () => FakeNvdHandler.Stalled(), () => FakeNvdHandler.Recorded("cpe-pyyaml-5.3"));

        var answer = await clock.DriveAsync(Client().GetPageAsync(NvdQuery.ForCpe(Cpe(PyyamlCpe)), 0, Test.Token), TimeSpan.FromSeconds(1));

        Assert.IsType<NvdPage>(answer);
        Assert.Equal(2, nvd.Requests.Count);
    }

    [Fact]
    public async Task Sixteen_lookups_with_throttled_first_attempts_keep_the_pace_retries_included()
    {
        using var catalog = new NvdVulnerabilityCatalog(new TestHttpClientFactory(nvd), new NvdOptions { ApiKey = "test-key" }, clock, () => 0.5, logger);
        var cpes = Enumerable.Range(0, 16).Select(index => $"cpe:2.3:a:vendor{index}:product{index}:1.0:*:*:*:*:*:*:*").ToList();
        foreach (var cpe in cpes)
        {
            nvd.On(cpe, () => FakeNvdHandler.Recorded("throttled"), () => FakeNvdHandler.Recorded("cpe-pyyaml-5.3"));
        }

        var lookups = await clock.DriveAsync(
            Task.WhenAll(cpes.Select(cpe => catalog.FindByCpeAsync(Cpe(cpe), Test.Token).AsTask())), TimeSpan.FromMilliseconds(100), 20_000);

        Assert.All(lookups, lookup => Assert.IsType<CatalogFound>(lookup));
        var starts = nvd.Requests.Select(request => request.At).Order().ToList();
        Assert.Equal(32, starts.Count);
        Assert.All(starts.Zip(starts.Skip(1)), pair => Assert.True(pair.Second - pair.First >= TimeSpan.FromMilliseconds(600)));
        for (var i = 0; i + 50 < starts.Count; i++)
        {
            Assert.True(starts[i + 50] - starts[i] >= TimeSpan.FromSeconds(30));
        }
    }

    public void Dispose() => nvd.Dispose();

    private static CandidateCpe Cpe(string value) => new(value, CpeSource.Generated);

    private NvdApiClient Client() =>
        new(
            new TestHttpClientFactory(nvd),
            new NvdOptions { ApiKey = "test-key" },
            NvdPipeline.Build(new NvdRequestPacer(TimeSpan.FromMilliseconds(600), clock), clock, () => 0.5, logger),
            clock,
            logger);
}
