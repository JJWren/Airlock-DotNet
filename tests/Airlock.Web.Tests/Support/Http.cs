using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Airlock.Web.Tests.Support;

/// <summary>Requests and answers of the API, as a client builds and reads them.</summary>
internal static class Http
{
    public const string ScansUrl = "/api/v2/scans";

    /// <summary>A form with the given artifacts, each a part named <c>artifacts</c>.</summary>
    public static MultipartFormDataContent Artifacts(params (string FileName, byte[] Content)[] files)
    {
        var form = new MultipartFormDataContent();
        foreach (var (fileName, content) in files)
        {
            form.Add(new ByteArrayContent(content), "artifacts", fileName);
        }

        return form;
    }

    public static MultipartFormDataContent Requirements() => Artifacts(("requirements.txt", A.Bytes(A.RequirementsText)));

    public static MultipartFormDataContent Directory(string path)
    {
        var form = new MultipartFormDataContent { { new StringContent(path), "targetPath" } };
        return form;
    }

    /// <summary>A multipart body written by hand, for what <see cref="MultipartFormDataContent"/> cannot send.</summary>
    public static ByteArrayContent Raw(string body, string boundary = "airlock-test") => Raw(Encoding.UTF8.GetBytes(body), boundary);

    public static ByteArrayContent Raw(byte[] body, string boundary = "airlock-test")
    {
        var content = new ByteArrayContent(body);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={boundary}");
        return content;
    }

    /// <summary>One part of a hand-written body, with CRLF line ends.</summary>
    public static string Part(string disposition, string content, string boundary = "airlock-test") =>
        $"--{boundary}\r\nContent-Disposition: form-data; {disposition}\r\n\r\n{content}\r\n";

    public static string End(string boundary = "airlock-test") => $"--{boundary}--\r\n";

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Test.Token);
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    /// <summary>Submits a scan and returns its id, failing the test unless the answer is 202.</summary>
    public static async Task<string> StartScanAsync(this HttpClient client, HttpContent content)
    {
        using var response = await client.PostAsync(ScansUrl, content, Test.Token);
        var body = await response.JsonAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Accepted, $"Expected 202, got {(int)response.StatusCode}: {body}");
        return body.GetProperty("id").GetString()!;
    }

    /// <summary>
    /// Submits a scan once the slot is free again: a finished scan's status is recorded before its cleanup releases the
    /// slot (U2 AR-32), so a client may meet a 409 for a few milliseconds.
    /// </summary>
    public static async Task<string> StartScanWhenFreeAsync(this HttpClient client, Func<HttpContent> content)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            using var response = await client.PostAsync(ScansUrl, content(), Test.Token);
            if (response.StatusCode != HttpStatusCode.Conflict || DateTime.UtcNow > deadline)
            {
                var body = await response.JsonAsync();
                Assert.True(response.StatusCode == HttpStatusCode.Accepted, $"Expected 202, got {(int)response.StatusCode}: {body}");
                return body.GetProperty("id").GetString()!;
            }

            await Task.Delay(10, Test.Token);
        }
    }

    public static async Task<JsonElement> GetScanAsync(this HttpClient client, string id)
    {
        using var response = await client.GetAsync($"{ScansUrl}/{id}", Test.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.JsonAsync();
    }

    /// <summary>Polls the scan until it has finished, in real time, for at most 15 seconds.</summary>
    public static async Task<JsonElement> WaitForEndAsync(this HttpClient client, string id)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var scan = await client.GetScanAsync(id);
            if (scan.GetProperty("status").GetString() != "RUNNING")
            {
                return scan;
            }

            Assert.True(DateTime.UtcNow < deadline, $"Scan {id} is still running.");
            await Task.Delay(10, Test.Token);
        }
    }

    /// <summary>The problem's members, asserting its status and kind.</summary>
    public static async Task<JsonElement> ProblemAsync(this HttpResponseMessage response, HttpStatusCode status, string slug)
    {
        var problem = await response.JsonAsync();
        Assert.True(response.StatusCode == status, $"Expected {(int)status}, got {(int)response.StatusCode}: {problem}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("https://github.com/JJWren/Airlock-DotNet/blob/main/docs/api/problems.md#" + slug, problem.GetProperty("type").GetString());
        return problem;
    }

    public static string Detail(this JsonElement problem) => problem.GetProperty("detail").GetString()!;
}
