using System.Globalization;
using System.Net;
using Airlock.Web.Tests.Support;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Airlock.Web.Tests.Quality;

// U4-NFR-03, U4-NFR-06, WR-06, WR-08 — what only a real Kestrel shows: its body limits, its headers, 100-continue.
public sealed class KestrelTests
{
    private const int Mebibyte = 1024 * 1024;

    [Fact]
    public async Task The_submission_raises_its_own_limit_above_the_servers_one_mebibyte()
    {
        await using var server = await KestrelAsync(maxBytesPerScan: 4 * Mebibyte);
        using var client = server.Client;

        var id = await client.StartScanAsync(Http.Artifacts(("three.bin", new byte[3 * Mebibyte])));

        Assert.Equal(3 * Mebibyte, (await client.WaitForEndAsync(id)).GetProperty("target").GetProperty("artifacts")[0].GetProperty("sizeBytes").GetInt64());
    }

    [Fact]
    public async Task An_upload_past_the_scan_limit_is_answered_413_too_large()
    {
        await using var server = await KestrelAsync(maxBytesPerScan: 4 * Mebibyte);
        using var client = server.Client;

        using var response = await client.PostAsync(Http.ScansUrl, Http.Artifacts(("big.bin", new byte[(4 * Mebibyte) + (Mebibyte / 2)])), Test.Token);

        Assert.Equal("Uploads are limited to 4 MB per scan", (await response.ProblemAsync(HttpStatusCode.RequestEntityTooLarge, "too-large")).Detail());
    }

    [Fact]
    public async Task A_client_that_expects_100_continue_receives_a_refusal_before_it_sends_its_files()
    {
        await using var server = await KestrelAsync();
        server.Factory.Catalog.IsConfigured = false;
        using var client = server.Client;
        var upload = new GeneratedStream(64L * Mebibyte);
        using var request = new HttpRequestMessage(HttpMethod.Post, Http.ScansUrl)
        {
            Content = new MultipartFormDataContent { { new StreamContent(upload), "artifacts", "app-image.tar" } },
        };
        request.Headers.ExpectContinue = true;

        using var response = await client.SendAsync(request, Test.Token);

        await response.ProblemAsync(HttpStatusCode.ServiceUnavailable, "not-ready");
        Assert.True(upload.BytesRead < Mebibyte, $"{upload.BytesRead} bytes were sent.");
    }

    [Fact]
    public async Task Answers_carry_no_server_header()
    {
        await using var server = await KestrelAsync();
        using var client = server.Client;

        using var response = await client.GetAsync("/api/v2/system/health", Test.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Headers.Server);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
    }

    private static async Task<KestrelServer> KestrelAsync(int? maxBytesPerScan = null)
    {
        var factory = new AirlockWebFactory(settings =>
        {
            settings["Urls"] = "http://127.0.0.1:0";
            if (maxBytesPerScan is { } limit)
            {
                settings["Airlock:Limits:MaxBytesPerScan"] = limit.ToString(CultureInfo.InvariantCulture);
            }
        });
        factory.UseKestrel(0);
        factory.StartServer();
        var address = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        await Task.CompletedTask;
        return new KestrelServer(factory, new HttpClient { BaseAddress = new Uri(address) });
    }

    private sealed class KestrelServer(AirlockWebFactory factory, HttpClient client) : IAsyncDisposable
    {
        public AirlockWebFactory Factory => factory;

        public HttpClient Client => client;

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await factory.DisposeAsync();
        }
    }
}
