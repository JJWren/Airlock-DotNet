using System.Text;
using Airlock.Web.Tests.Support;

namespace Airlock.Web.Tests.Endpoints;

// WR-18 — the last lines first, then every line as it is written, each with its scan id (E08; defect 4).
public sealed class LiveLogStreamTests
{
    [Fact]
    public async Task A_new_subscriber_receives_the_replay_then_live_lines_with_their_scan_ids()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        using (var refused = await client.PostAsync(Http.ScansUrl, new StringContent("{}", Encoding.UTF8, "application/json"), Test.Token))
        {
            Assert.Equal(System.Net.HttpStatusCode.UnsupportedMediaType, refused.StatusCode);
        }

        using var response = await client.GetAsync("/api/v2/logs/stream", HttpCompletionOption.ResponseHeadersRead, Test.Token);
        using var events = new SseReader(await response.Content.ReadAsStreamAsync(Test.Token));
        var replayed = await NextLineAsync(events, line => line.GetProperty("message").GetString()!.Contains("not multipart/form-data", StringComparison.Ordinal));
        factory.Sbom.BlocksUntilCancelled(FakeSbomGenerator.Manifests);
        var id = await client.StartScanAsync(Http.Requirements());
        var live = await NextLineAsync(events, line => line.GetProperty("scanId").GetString() == id);

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("WARNING", replayed.GetProperty("level").GetString());
        Assert.Equal("Airlock.Web.Api.Uploads.MultipartScanReader", replayed.GetProperty("category").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, replayed.GetProperty("scanId").ValueKind);
        Assert.Matches("^2[0-9]{3}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{3}Z$", live.GetProperty("timestamp").GetString()!);
        Assert.Equal(["timestamp", "level", "category", "message", "scanId"], live.EnumerateObject().Select(member => member.Name));
        using var stop = await client.PostAsync("/api/v2/system/stop", null, Test.Token);
        await client.WaitForEndAsync(id);
    }

    private static async Task<System.Text.Json.JsonElement> NextLineAsync(SseReader events, Func<System.Text.Json.JsonElement, bool> wanted)
    {
        while (await events.NextAsync(Test.Token).Within() is { } next)
        {
            Assert.Equal("log", next.Name);
            if (wanted(next.Json))
            {
                return next.Json;
            }
        }

        throw new InvalidOperationException("The Live Log stream ended.");
    }
}
