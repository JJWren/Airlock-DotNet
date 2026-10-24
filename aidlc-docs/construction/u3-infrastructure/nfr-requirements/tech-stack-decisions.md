# U3 Infrastructure — Tech Stack Decisions

**Stage**: CONSTRUCTION — U3 Infrastructure — NFR Requirements (Standard depth)

The solution-wide stack is U1's and the Application's is U2's. Exact package versions are resolved to the latest
stable releases at Code Generation and recorded in `Directory.Packages.props`.

## Airlock.Infrastructure

| Item | Decision |
|---|---|
| References | `Airlock.Application`, `Airlock.Domain`; `Microsoft.Extensions.Http` (`HttpClient` from the factory), `Polly.Core` and `Polly.RateLimiting` (Polly v8 retry, rate-limiter and timeout strategies), `Microsoft.Extensions.Logging` (the Live Log provider's filter rules), `Microsoft.Extensions.Configuration` (the environment variables' provider), `Microsoft.Extensions.Options.ConfigurationExtensions` and `Microsoft.Extensions.Configuration.Binder` (binding), `Microsoft.Extensions.Hosting.Abstractions` (the staging sweep), `QuestPDF` (Community licence) |
| From the base class library | `System.Threading.RateLimiting` (the `RateLimiter` type), `System.Text.Json` with source-generated contexts for the Syft and NVD formats, `System.Diagnostics.Process`, `System.IO.Compression` for the `.nuspec`, `System.Threading.Channels` for the Live Log |
| NVD pipeline | One resilience pipeline that `NvdApiClient` runs around each whole page — sending, reading and parsing — outermost first: the retry, the limiter, the per-attempt timeout. Every attempt, retries included, takes its turn, and waiting for a turn never counts against an attempt's timeout. The catalog bounds each lookup — every page and attempt — with its own 2-minute deadline. No circuit breaker: the Application's outage breaker (U2 AR-18) decides when NVD is unavailable |
| Rate limiter | Airlock's own `RateLimiter` on `TimeProvider`, starting requests at least `Nvd:Window` / `Nvd:RequestsPerWindow` apart. The built-in `SlidingWindowRateLimiter` is not used: it hands a segment's permits back less than a full window after the last of them was taken, so some 30-second spans can hold more than 50 requests while NVD counts a rolling 30 seconds, and its replenishment timer runs on real time |
| Time | `TimeProvider` everywhere — the limiter, the Polly pipeline, the lookup deadline and the Syft time limit — so tests run them on a fake clock |
| Syft | Started with `ProcessStartInfo.ArgumentList`, `UseShellExecute = false`, the NVD key removed from its environment and its temporary folder set to the run's own; standard output parsed as a stream while standard error is read alongside; killed with `Process.Kill(entireProcessTree: true)` |
| PDF | QuestPDF's fluent API; the licence set in `AddAirlockInfrastructure` |
| Configuration | `AddAirlockInfrastructure(IServiceCollection, IConfiguration)` registers the adapters, the options and their validation; a configuration extension, `AddAirlockEnvironmentVariables(IConfigurationBuilder)`, maps `NVD_API_KEY` and `SYFT_BINARY_PATH`, and the host (U4) adds it (IR-23) |
| Logging | `[LoggerMessage]` methods in `Airlock.Infrastructure.*` categories, which the Live Log captures; the HTTP client factory's own logging removed for the NVD client, with headers redacted for any logger added later |
| Install scripts | `scripts/install-tools.ps1` (Windows PowerShell 5.1 and PowerShell 7) and `scripts/install-tools.sh` (POSIX shell with `curl`, and `sha256sum` or `shasum`) |

## Test projects

| Project | Packages | Notes |
|---|---|---|
| `Airlock.Infrastructure.Tests` (new) | `xunit.v3`, `Microsoft.Testing.Extensions.CodeCoverage`, `Microsoft.Extensions.TimeProvider.Testing`, `Microsoft.Extensions.Diagnostics.Testing`, `PdfPig` (reads the PDF's text) | Recorded S0 outputs as fixture files — Syft JSON from the S0 samples and NVD's responses, made without a key, with their headers trimmed to the ones the tests read; a fake `HttpMessageHandler`; a small stand-in process for the process-control tests; real-Syft tests marked `Category=Integration` |
| `Airlock.Infrastructure.StandIn` (new, in `tests/`) | none | A small console program that plays Syft for the process tests; not a test project |
| `Airlock.Architecture.Tests` | adds a reference to `Airlock.Infrastructure` | Rules 3 and 4, each with a positive control |

The recorded fixtures are copied from the S0 scratchpad outputs; the binaries and image archives they came from are
not committed.

## Amendments

**U3 NFR Design (2026-10-23).** `Microsoft.Extensions.Http.Resilience` gave way to `Polly.Core` and
`Polly.RateLimiting`. Its HTTP handler ends an attempt when the response headers arrive, so the body — read as it
streams (U3-NFR-05) — would sit outside the attempt timeout and the retries (U3-NFR-02). The strategies and their
order are unchanged. Also added: `Microsoft.Extensions.Logging` and `Microsoft.Extensions.Hosting.Abstractions` to the
project, and PdfPig and `Airlock.Infrastructure.StandIn` to the tests (`nfr-design/nfr-design-patterns.md`).

**U3 Code Generation (2026-10-24).** PdfPig's package id is `PdfPig` (`UglyToad.PdfPig` is its namespace).
`Microsoft.Extensions.Configuration` is referenced directly, for the configuration provider behind
`AddAirlockEnvironmentVariables`. The HTTP client factory's own logging is removed for the NVD client
(`code/code-generation-summary.md`, deviation 1). Versions: Microsoft.Extensions.* 10.0.13, Polly.Core and
Polly.RateLimiting 8.8.0, QuestPDF 2026.9.1, PdfPig 0.1.16.
