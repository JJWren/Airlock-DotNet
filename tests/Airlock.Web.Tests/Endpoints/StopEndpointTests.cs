using System.Net;
using Airlock.Web.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Airlock.Web.Tests.Endpoints;

// WR-15, WR-16 — cancel answers 200 or 202; Emergency Stop ends whatever holds the slot (E09; defects 3, 15).
public sealed class StopEndpointTests
{
    [Fact]
    public async Task Cancel_answers_200_with_the_cancelled_scan()
    {
        await using var factory = new AirlockWebFactory();
        factory.Sbom.BlocksUntilCancelled(FakeSbomGenerator.Manifests);
        using var client = factory.CreateClient();
        var id = await client.StartScanAsync(Http.Requirements());
        await factory.Sbom.RunStarted.Task.Within();

        using var response = await client.PostAsync($"{Http.ScansUrl}/{id}/cancel", null, Test.Token);
        var scan = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("CANCELLED", scan.GetProperty("status").GetString());
        Assert.Equal("Cancelled by request", scan.GetProperty("cancellationReason").GetString());
    }

    [Fact]
    public async Task Cancel_answers_202_with_a_location_while_the_cancellation_is_under_way()
    {
        var clock = new FakeTimeProvider(Test.StartTime);
        await using var factory = new AirlockWebFactory(clock: clock);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        factory.Sbom.IgnoresCancellation(FakeSbomGenerator.Manifests, release.Task);
        using var client = factory.CreateClient();
        var id = await client.StartScanAsync(Http.Requirements());
        await factory.Sbom.RunStarted.Task.Within();

        using var response = await clock.DriveAsync(client.PostAsync($"{Http.ScansUrl}/{id}/cancel", null, Test.Token), TimeSpan.FromMilliseconds(250));
        var scan = await response.JsonAsync();
        release.SetResult();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal($"/api/v2/scans/{id}", response.Headers.Location?.OriginalString);
        Assert.Equal("RUNNING", scan.GetProperty("status").GetString());
        Assert.Equal("CANCELLED", (await client.WaitForEndAsync(id)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Cancelling_a_finished_scan_conflicts_and_an_unknown_one_is_not_found()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        var id = await client.StartScanAsync(Http.Requirements());
        await client.WaitForEndAsync(id);

        using var finished = await client.PostAsync($"{Http.ScansUrl}/{id}/cancel", null, Test.Token);
        using var unknown = await client.PostAsync($"{Http.ScansUrl}/{Guid.NewGuid()}/cancel", null, Test.Token);

        Assert.Equal("The scan has already finished", (await finished.ProblemAsync(HttpStatusCode.Conflict, "scan-already-finished")).Detail());
        await unknown.ProblemAsync(HttpStatusCode.NotFound, "scan-not-found");
    }

    [Fact]
    public async Task Emergency_stop_terminates_the_running_scan_whoever_started_it()
    {
        await using var factory = new AirlockWebFactory();
        factory.Sbom.BlocksUntilCancelled(FakeSbomGenerator.Manifests);
        using var starter = factory.CreateClient();
        using var other = factory.CreateClient();
        var id = await starter.StartScanAsync(Http.Requirements());
        await factory.Sbom.RunStarted.Task.Within();

        using var response = await other.PostAsync("/api/v2/system/stop", null, Test.Token);
        var stopped = await response.JsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("TERMINATED", stopped.GetProperty("status").GetString());
        Assert.Equal(id, stopped.GetProperty("scanId").GetString());
        var scan = await starter.WaitForEndAsync(id);
        Assert.Equal("CANCELLED", scan.GetProperty("status").GetString());
        Assert.Equal("Emergency Stop", scan.GetProperty("cancellationReason").GetString());
        await starter.StartScanWhenFreeAsync(Http.Requirements);
    }

    [Fact]
    public async Task Emergency_stop_with_nothing_running_is_idle()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/v2/system/stop", null, Test.Token);
        var stopped = await response.JsonAsync();

        Assert.Equal("IDLE", stopped.GetProperty("status").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, stopped.GetProperty("scanId").ValueKind);
    }

    [Fact]
    public async Task Emergency_stop_during_an_upload_answers_that_upload_upload_stopped()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var uploading = client.PostAsync(Http.ScansUrl, GatedUpload.Create("app-image.tar", gate.Task), Test.Token);
        await Test.EventuallyAsync(() => factory.StagingFolders().Count == 1, "The upload never reached staging.");

        using var stop = await client.PostAsync("/api/v2/system/stop", null, Test.Token);
        var stopped = await stop.JsonAsync();
        using var upload = await uploading.Within();
        gate.SetResult();

        Assert.Equal("TERMINATED", stopped.GetProperty("status").GetString());
        Assert.Equal("Emergency Stop ended the upload before the scan started", (await upload.ProblemAsync(HttpStatusCode.Conflict, "upload-stopped")).Detail());
        using var never = await client.GetAsync($"{Http.ScansUrl}/{stopped.GetProperty("scanId").GetString()}", Test.Token);
        await never.ProblemAsync(HttpStatusCode.NotFound, "scan-not-found");
        await Test.EventuallyAsync(() => factory.StagingFolders().Count == 0, "The staging area was kept.");
    }
}
