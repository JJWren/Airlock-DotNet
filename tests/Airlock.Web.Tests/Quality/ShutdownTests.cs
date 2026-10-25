using Airlock.Web.Tests.Support;

namespace Airlock.Web.Tests.Quality;

// U4-NFR-05, U2 AR-30 — stopping the host cancels the running scan, its stream ends with that status, and every stream
// ends, so shutdown never waits for clients.
public sealed class ShutdownTests
{
    [Fact]
    public async Task Stopping_the_host_ends_a_scans_stream_with_its_cancelled_status_and_the_live_log_stream()
    {
        await using var factory = new AirlockWebFactory();
        factory.Sbom.BlocksUntilCancelled(FakeSbomGenerator.Manifests);
        using var client = factory.CreateClient();
        var id = await client.StartScanAsync(Http.Requirements());
        await factory.Sbom.RunStarted.Task.Within();
        using var scanResponse = await client.GetAsync($"{Http.ScansUrl}/{id}/events", HttpCompletionOption.ResponseHeadersRead, Test.Token);
        using var scanEvents = new SseReader(await scanResponse.Content.ReadAsStreamAsync(Test.Token));
        Assert.Equal("snapshot", (await scanEvents.NextAsync(Test.Token).Within())!.Name);
        using var logResponse = await client.GetAsync("/api/v2/logs/stream", HttpCompletionOption.ResponseHeadersRead, Test.Token);
        using var logEvents = new SseReader(await logResponse.Content.ReadAsStreamAsync(Test.Token));
        var logsEnded = logEvents.ToEndAsync();

        var stopping = factory.StartedHost!.StopAsync(Test.Token);
        var rest = await scanEvents.ToEndAsync();
        await logsEnded.Within(10);
        await stopping.Within(10);

        Assert.Equal("finished", rest[^1].Name);
        Assert.Equal("CANCELLED", rest[^1].Json.GetProperty("status").GetString());
    }
}
