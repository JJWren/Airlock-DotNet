using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace Airlock.Web.Api.Docs;

/// <summary>The OpenAPI document at <c>/openapi/v2.json</c> and Scalar at <c>/docs</c>, in every environment (WR-22).</summary>
internal static class ApiDocs
{
    public const string DocumentName = "v2";

    public static IServiceCollection AddAirlockOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(DocumentName, options =>
        {
            // Schemas are named after the glossary: Scan, Finding, ScanStatus — without the C# suffixes.
            options.CreateSchemaReferenceId = typeInfo => SchemaName(OpenApiOptions.CreateDefaultSchemaReferenceId(typeInfo));
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Airlock API",
                    Version = DocumentName,
                    Description = "Software composition analysis: Syft SBOMs correlated with NVD CVEs. Problems are RFC 9457 ProblemDetails whose type URLs are described in docs/api/problems.md.",
                };
                return Task.CompletedTask;
            });

            // WR-07: the submission's body is read part by part, so no parameter describes it. Accepts() metadata would
            // describe it too, but routing would then answer a body of another type with 415 itself, before the
            // cross-site check and without the Web's log line (WR-06).
            options.AddOperationTransformer((operation, _, _) =>
            {
                if (operation.OperationId == "CreateScan")
                {
                    operation.RequestBody = SubmissionBody();
                }

                return Task.CompletedTask;
            });
        });

    public static WebApplication MapAirlockDocs(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapOpenApi();
        app.MapScalarApiReference("/docs", Configure);
        return app;
    }

    // U4-NFR-07: everything from Airlock itself, nothing sent anywhere: no font CDN, no telemetry, no AI agent, and no
    // developer toolbar, whose Share and Deploy buttons upload the document to Scalar's service and which Scalar shows
    // on localhost; the assets stay bundled and no proxy is set, as by default.
    internal static void Configure(ScalarOptions options)
    {
        options.Title = "Airlock API";
        options.DefaultFonts = false;
        options.Telemetry = false;
        options.DisableAgent();
        options.HideDeveloperTools();
        options.AddDocument(DocumentName);
    }

    private static OpenApiRequestBody SubmissionBody() => new()
    {
        Required = true,
        Description = "Either targetPath or one or more artifacts, not both. Field names are compared ignoring case.",
        Content = new Dictionary<string, OpenApiMediaType>
        {
            ["multipart/form-data"] = new()
            {
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Properties = new Dictionary<string, IOpenApiSchema>
                    {
                        ["targetPath"] = new OpenApiSchema
                        {
                            Type = JsonSchemaType.String,
                            Description = "A directory on the Airlock host, as a full path.",
                        },
                        ["artifacts"] = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Array,
                            Description = "The artifacts to scan — manifests, archives, package files, container image archives or binaries — at most 25 files and 2 GB by default. Also accepted as artifacts[].",
                            Items = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" },
                        },
                    },
                },
            },
        },
    };

    internal static string? SchemaName(string? name)
    {
        if (name is null)
        {
            return null;
        }

        foreach (var suffix in (ReadOnlySpan<string>)["Dto", "Value"])
        {
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
            {
                return name[..^suffix.Length];
            }
        }

        return name;
    }
}
