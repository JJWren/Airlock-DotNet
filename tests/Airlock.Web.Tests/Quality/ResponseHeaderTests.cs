using System.Net.Http.Headers;
using System.Text;
using Airlock.Web.Tests.Support;

namespace Airlock.Web.Tests.Quality;

// U4-NFR-06, WR-02 — nosniff and no-store on every API answer, problems and streams included; no CORS; no compression.
public sealed class ResponseHeaderTests
{
    [Fact]
    public async Task Every_api_answer_carries_nosniff_and_no_store()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        var id = await client.StartScanAsync(Http.Requirements());
        await client.WaitForEndAsync(id);

        var answers = new List<HttpResponseMessage>
        {
            await client.GetAsync("/api/v2/system/health", Test.Token),
            await client.GetAsync($"{Http.ScansUrl}/{id}", Test.Token),
            await client.GetAsync($"{Http.ScansUrl}/{id}/events", Test.Token),
            await client.GetAsync($"{Http.ScansUrl}/{id}/report.pdf", Test.Token),
            await client.GetAsync($"{Http.ScansUrl}/{Guid.NewGuid()}", Test.Token),
            await client.GetAsync("/api/v2/nothing", Test.Token),
            await client.PostAsync(Http.ScansUrl, new StringContent("{}", Encoding.UTF8, "application/json"), Test.Token),
            await client.PostAsync("/api/v2/system/stop", null, Test.Token),
        };

        foreach (var answer in answers)
        {
            using (answer)
            {
                var url = answer.RequestMessage!.RequestUri;
                Assert.True(answer.Headers.TryGetValues("X-Content-Type-Options", out var sniff) && sniff.Single() == "nosniff", $"{url}");
                Assert.True(answer.Headers.CacheControl?.NoStore, $"{url}");
            }
        }
    }

    [Fact]
    public async Task No_cors_headers_and_no_compression_are_ever_sent()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v2/system/health");
        request.Headers.TryAddWithoutValidation("Origin", "https://evil.example");
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));

        using var response = await client.SendAsync(request, Test.Token);

        Assert.DoesNotContain(response.Headers, header => header.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(response.Content.Headers.ContentEncoding);
    }
}
