# U4 Web API — Tech Stack Decisions

**Stage**: CONSTRUCTION — U4 Web API — NFR Requirements (Standard depth)

The solution-wide stack is U1's, the Application's U2's and the Infrastructure's U3's. Exact package versions are
re-checked at Code Generation and recorded in `Directory.Packages.props`. MudBlazor and the UI's own choices arrive
with U5.

## Airlock.Web

| Item | Decision |
|---|---|
| Project | `src/Airlock.Web`, on `Microsoft.NET.Sdk.Web` (the ASP.NET Core 10 shared framework), referencing `Airlock.Infrastructure`, `Airlock.Application` and `Airlock.Domain`, with `Microsoft.AspNetCore.OpenApi` and `Scalar.AspNetCore`. The `Microsoft.Extensions.*` packages the lower projects reference come with the shared framework; .NET 10's package pruning reconciles them, which Code Generation confirms without warnings |
| Endpoints | Minimal APIs in one route group, `/api/v2`, answering with `TypedResults`; `DisableAntiforgery()` (WR-21) and the cross-site filter (WR-19) on the `POST` endpoints |
| JSON | `System.Text.Json` with the web defaults and a source-generated `JsonSerializerContext` for the DTOs. Enumerations are written with `JsonStringEnumConverter` and `JsonNamingPolicy.SnakeCaseUpper` (Q-U4-1), with explicit names where WR-02 differs (`cvssVersion`, the log levels). HTTP bodies are indented; text is written with `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, safe here because the answers are `application/json` with `nosniff` and are never embedded in HTML; event data uses a compact copy of the same options |
| Errors | `AddProblemDetails`, with `ApiProblems` building every problem of the catalogue; `UseExceptionHandler` turns an unexpected exception into the 500 problem without details, ahead of the developer exception page; status-code pages give empty 404 and 405 answers under `/api` their problem bodies |
| Multipart | `MultipartReader` (`Microsoft.AspNetCore.WebUtilities`) over the request body, with `MediaTypeHeaderValue` and `ContentDispositionHeaderValue` (`Microsoft.Net.Http.Headers`), all in the shared framework; no form binding |
| Event streams | A small stream writer of Airlock's own — an `IResult` writing `event:` and `data:` lines, `: keep-alive` comments and a flush after each event — instead of `TypedResults.ServerSentEvents`, whose `SseItem<T>` cannot carry a comment (U4-NFR-05) |
| OpenAPI | `Microsoft.AspNetCore.OpenApi`: `AddOpenApi("v2")` and `MapOpenApi()`, which serves `/openapi/v2.json` as OpenAPI 3.1, with transformers that name the schemas after the glossary and describe the multipart body, the problem answers and the event streams |
| API reference | `Scalar.AspNetCore`: `MapScalarApiReference` at `/docs` with its bundled assets, `DisableDefaultFonts()`, `DisableAgent()` and no request proxy (U4-NFR-07); NFR Design confirms the option names against the 2.18 package |
| Host | `WebApplication.CreateBuilder`. Host filtering through `AllowedHosts`; Kestrel limits in `ConfigureKestrel`, with the submission endpoint raising its own limit through `IHttpMaxRequestBodySizeFeature` before it reads; `AddServerHeader = false`; no HTTPS redirection, no response compression, no CORS |
| Time | The `TimeProvider` the Application registers, for the upload's idle timeout and the keep-alive comments, so tests run them on a fake clock; ASP.NET Core's own timeouts keep its clock |
| Configuration | `appsettings.json` and `appsettings.Development.json`, user-secrets in Development, environment variables, `AddAirlockEnvironmentVariables()` after the JSON files, and the command line; the precedence is fixed in Infrastructure Design |

## Test projects

| Project | Packages | Notes |
|---|---|---|
| `Airlock.Web.Tests` (new) | `xunit.v3`, `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.Testing.Extensions.CodeCoverage`, `Microsoft.Extensions.TimeProvider.Testing`, `Microsoft.Extensions.Diagnostics.Testing` | `WebApplicationFactory<Program>`, using the `Program` class .NET 10 makes public; the test server for most tests and `UseKestrel` for limits and host names; hand-written fakes for `ISbomGenerator` and `IVulnerabilityCatalog`, as in U2 and U3; a temporary staging root per test; the document parsed with `Microsoft.OpenApi`, which comes with the OpenAPI package. bUnit joins in U5 |
| `Airlock.Architecture.Tests` | adds a reference to `Airlock.Web` | Rule 5 for the API, with a positive control |

NSubstitute is still not needed: the hand-written fakes cover both ports the API tests replace.

## Versions on nuget.org (2026-10-24)

`Microsoft.AspNetCore.OpenApi` 10.0.13, `Scalar.AspNetCore` 2.18.3 and `Microsoft.AspNetCore.Mvc.Testing` 10.0.13.
`MudBlazor` 9.11.0 and `bunit` 2.12.5 are for U5.

## Amendments

**U4 NFR Design (2026-10-24).** Scalar's source shows that its telemetry is on by default, so `Telemetry = false`
joins the fonts and the agent; the fonts are switched off with `DefaultFonts = false`. The JSON contract uses
enumerations of the Web's own, each value named with `[JsonStringEnumMemberName]` and mapped from the Domain's, rather
than a naming policy over the Domain's enumerations. The body is read through an `IdleTimeoutStream`, and U3's
`StagingSweepService` gains the staging-root lock (`nfr-design/nfr-design-patterns.md`). No package is added.

**U4 Code Generation (2026-10-24).**
- The submission endpoint declares no `Accepts` metadata: with it, routing answers 415 itself, before the cross-site
  filter and without the Web's log line. The OpenAPI document gets the multipart body from an operation transformer
  instead.
- The two streams and the PDF declare `Produces<Stream>` with their content types, without which the document leaves
  their content out.
- Scalar's developer toolbar, which Scalar shows on localhost, is hidden (`HideDeveloperTools`): its Share and Deploy
  buttons send the document to Scalar's service (U4-NFR-07).
- Scalar answers `/docs` with a redirect to `/docs/`.
- The tests' `WebApplicationFactory` client follows no redirects and keeps no cookies, because its redirect handler
  buffers every request body.
- The tests that need a failed start build `AirlockHost` themselves: the factory's deferred host can report a disposed
  service provider instead of the reason.

No package is added.
