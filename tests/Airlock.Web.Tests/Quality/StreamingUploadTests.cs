using System.Net;
using Airlock.Web.Tests.Support;

namespace Airlock.Web.Tests.Quality;

// U4-NFR-02 — an upload streams to staging; nothing holds it in memory.
[Collection(nameof(Measurements))]
public sealed class StreamingUploadTests
{
    private const long Mebibyte = 1024 * 1024;

    [Fact]
    public async Task A_256_mebibyte_upload_is_staged_with_under_32_megabytes_allocated()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        await client.WaitForEndAsync(await client.StartScanAsync(Http.Requirements()));
        var upload = new GeneratedStream(256 * Mebibyte);
        using var form = new MultipartFormDataContent { { new StreamContent(upload, 80 * 1024), "artifacts", "big.bin" } };

        var before = GC.GetTotalAllocatedBytes(precise: true);
        using var response = await client.PostAsync(Http.ScansUrl, form, Test.Token);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(256 * Mebibyte, upload.BytesRead);
        Assert.True(allocated < 32 * 1000 * 1000, $"{allocated / 1000 / 1000} MB were allocated.");
        var scan = await client.WaitForEndAsync((await response.JsonAsync()).GetProperty("id").GetString()!);
        Assert.Equal(256 * Mebibyte, scan.GetProperty("target").GetProperty("artifacts")[0].GetProperty("sizeBytes").GetInt64());
        await Test.EventuallyAsync(() => factory.StagingFolders().Count == 0, "The staging area was kept.");
    }
}
