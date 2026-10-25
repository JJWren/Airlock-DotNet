using System.Net;
using System.Text.Json;
using Airlock.Web.Tests.Support;

namespace Airlock.Web.Tests.Contract;

// WR-22 — every endpoint and answer of WR-01 in the OpenAPI 3.1 document, Scalar at /docs.
public sealed class OpenApiDocumentTests : IAsyncLifetime
{
    private readonly AirlockWebFactory factory = new();
    private JsonElement document;

    public static TheoryData<string, string, int[]> Endpoints => new()
    {
        { "/api/v2/scans", "post", [202, 400, 403, 409, 413, 415, 503] },
        { "/api/v2/scans/{id}", "get", [200, 404] },
        { "/api/v2/scans/{id}/events", "get", [200, 404] },
        { "/api/v2/scans/{id}/report.pdf", "get", [200, 404, 409] },
        { "/api/v2/scans/{id}/cancel", "post", [200, 202, 403, 404, 409] },
        { "/api/v2/system/stop", "post", [200, 403] },
        { "/api/v2/system/health", "get", [200] },
        { "/api/v2/logs/stream", "get", [200] },
    };

    public async ValueTask InitializeAsync()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v2.json", Test.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        document = await response.JsonAsync();
    }

    public async ValueTask DisposeAsync() => await factory.DisposeAsync();

    [Fact]
    public void The_document_is_openapi_3_1_for_the_airlock_api()
    {
        Assert.StartsWith("3.1", document.GetProperty("openapi").GetString(), StringComparison.Ordinal);
        Assert.Equal("Airlock API", document.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal(8, document.GetProperty("paths").EnumerateObject().Count());
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public void Every_endpoint_lists_every_answer(string path, string method, int[] statuses)
    {
        var operation = document.GetProperty("paths").GetProperty(path).GetProperty(method);

        var listed = operation.GetProperty("responses").EnumerateObject().Select(response => int.Parse(response.Name, System.Globalization.CultureInfo.InvariantCulture)).Order();

        Assert.Equal(statuses, listed);
        Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("summary").GetString()));
        foreach (var response in operation.GetProperty("responses").EnumerateObject().Where(response => response.Name[0] is '4' or '5'))
        {
            Assert.True(response.Value.GetProperty("content").TryGetProperty("application/problem+json", out _), $"{method} {path} {response.Name}");
        }
    }

    [Fact]
    public void The_streams_and_the_report_name_their_content_types()
    {
        var paths = document.GetProperty("paths");

        Assert.True(Ok(paths, "/api/v2/scans/{id}/events").TryGetProperty("text/event-stream", out _));
        Assert.True(Ok(paths, "/api/v2/logs/stream").TryGetProperty("text/event-stream", out _));
        Assert.True(Ok(paths, "/api/v2/scans/{id}/report.pdf").TryGetProperty("application/pdf", out _));
    }

    [Fact]
    public void The_submission_is_a_multipart_form_with_target_path_and_artifacts()
    {
        var body = document.GetProperty("paths").GetProperty("/api/v2/scans").GetProperty("post").GetProperty("requestBody");

        var schema = Resolve(body.GetProperty("content").GetProperty("multipart/form-data").GetProperty("schema"));

        var properties = schema.GetProperty("properties").EnumerateObject().Select(property => property.Name).ToList();
        Assert.Equal(["targetPath", "artifacts"], properties);
    }

    [Fact]
    public void Enumerations_list_their_values_as_the_json_writes_them()
    {
        var status = document.GetProperty("components").GetProperty("schemas").GetProperty("ScanStatus");

        Assert.Equal(["RUNNING", "COMPLETED", "INCOMPLETE", "FAILED", "CANCELLED"], status.GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public void Schemas_are_named_after_the_glossary()
    {
        var names = document.GetProperty("components").GetProperty("schemas").EnumerateObject().Select(schema => schema.Name).ToList();

        Assert.Contains("Scan", names);
        Assert.Contains("Finding", names);
        Assert.Contains("UnverifiedPackage", names);
        Assert.Contains("VulnerablePackage", names);
        Assert.DoesNotContain(names, name => name.EndsWith("Dto", StringComparison.Ordinal) || name.EndsWith("Value", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Scalar_answers_at_docs()
    {
        using var client = factory.CreateClient();

        using var redirect = await client.GetAsync("/docs", Test.Token);
        using var page = await client.GetAsync("/docs/", Test.Token);

        Assert.Equal(HttpStatusCode.Found, redirect.StatusCode);
        Assert.Equal("docs/", redirect.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void Only_the_web_contracts_are_schemas()
    {
        var names = document.GetProperty("components").GetProperty("schemas").EnumerateObject().Select(schema => schema.Name);

        Assert.DoesNotContain(names, name => name.StartsWith("IForm", StringComparison.Ordinal));
    }

    private static JsonElement Ok(JsonElement paths, string path) =>
        paths.GetProperty(path).GetProperty("get").GetProperty("responses").GetProperty("200").GetProperty("content");

    private JsonElement Resolve(JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var reference))
        {
            return schema;
        }

        var name = reference.GetString()!.Split('/')[^1];
        return document.GetProperty("components").GetProperty("schemas").GetProperty(name);
    }
}
