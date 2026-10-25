using System.Net;
using Airlock.Web.Tests.Support;

namespace Airlock.Web.Tests.Endpoints;

// WR-12, WR-13 — snapshot first, finished last, one event and one data line each, no ids (E03, E08; defect 14).
public sealed class ScanEventStreamTests
{
    [Fact]
    public async Task A_running_scans_stream_follows_it_from_its_snapshot_to_finished()
    {
        await using var factory = new AirlockWebFactory();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Sbom.ProducesAfter(FakeSbomGenerator.Manifests, release.Task, A.Sbom("pyyaml", "5.3"), A.Sbom("requests", "2.25.1"));
        factory.Catalog.Cpe(A.CpeFor("pyyaml", "5.3"), A.Cve("CVE-2020-1747"));
        using var client = factory.CreateClient();
        var id = await client.StartScanAsync(Http.Requirements());
        await factory.Sbom.RunStarted.Task.Within();

        using var response = await client.GetAsync($"{Http.ScansUrl}/{id}/events", HttpCompletionOption.ResponseHeadersRead, Test.Token);
        using var events = new SseReader(await response.Content.ReadAsStreamAsync(Test.Token));
        var snapshot = await events.NextAsync(Test.Token).Within();
        release.SetResult();
        var rest = await events.ToEndAsync();

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("snapshot", snapshot!.Name);
        Assert.Equal(id, snapshot.Json.GetProperty("id").GetString());
        Assert.Equal("RUNNING", snapshot.Json.GetProperty("status").GetString());
        var phases = rest.Where(item => item.Name == "phase").Select(item => item.Json.GetProperty("phase").GetString()).ToList();
        Assert.Equal(["CORRELATION", "SUMMARY"], phases);
        var progress = rest.Where(item => item.Name == "progress").Select(item => item.Json.GetProperty("correlated").GetInt32()).ToList();
        Assert.Equal(2, progress[^1]);
        Assert.All(rest.Where(item => item.Name == "progress"), item => Assert.Equal(2, item.Json.GetProperty("total").GetInt32()));
        Assert.Contains(rest, item => item.Name == "log");
        Assert.All(rest.Where(item => item.Name == "log"), item => Assert.False(item.Json.TryGetProperty("scanId", out _)));
        Assert.Equal("finished", rest[^1].Name);
        Assert.Equal("COMPLETED", rest[^1].Json.GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_finished_scans_stream_is_its_snapshot_and_finished()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        var id = await client.StartScanAsync(Http.Requirements());
        await client.WaitForEndAsync(id);

        using var response = await client.GetAsync($"{Http.ScansUrl}/{id}/events", HttpCompletionOption.ResponseHeadersRead, Test.Token);
        using var events = new SseReader(await response.Content.ReadAsStreamAsync(Test.Token));
        var all = await events.ToEndAsync();

        Assert.Equal(["snapshot", "finished"], all.Select(item => item.Name));
        Assert.Equal("COMPLETED", all[0].Json.GetProperty("status").GetString());
        Assert.Equal("COMPLETED", all[1].Json.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Each_event_is_one_event_line_and_one_data_line_without_ids()
    {
        await using var factory = new AirlockWebFactory();
        factory.Sbom.Produces(FakeSbomGenerator.Manifests, A.Sbom("pyyaml", "5.3"));
        using var client = factory.CreateClient();
        var id = await client.StartScanAsync(Http.Requirements());
        await client.WaitForEndAsync(id);

        using var response = await client.GetAsync($"{Http.ScansUrl}/{id}/events", HttpCompletionOption.ResponseHeadersRead, Test.Token);
        using var events = new SseReader(await response.Content.ReadAsStreamAsync(Test.Token));
        await events.ToEndAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no", response.Headers.GetValues("X-Accel-Buffering").Single());
        for (var i = 0; i < events.Lines.Count; i += 3)
        {
            Assert.StartsWith("event: ", events.Lines[i], StringComparison.Ordinal);
            Assert.StartsWith("data: {", events.Lines[i + 1], StringComparison.Ordinal);
            Assert.Equal(string.Empty, events.Lines[i + 2]);
        }

        Assert.DoesNotContain(events.Lines, line => line.StartsWith("id:", StringComparison.Ordinal) || line.StartsWith("retry:", StringComparison.Ordinal));
    }
}
