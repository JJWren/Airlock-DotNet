# U2 Application — Tech Stack Decisions

**Stage**: CONSTRUCTION — U2 Application — NFR Requirements (Standard depth)

The solution-wide stack is U1's (`construction/u1-domain/nfr-requirements/tech-stack-decisions.md`). Exact package
versions are resolved to the latest stable releases at Code Generation and recorded in `Directory.Packages.props`.

## Airlock.Application

| Item | Decision |
|---|---|
| References | `Airlock.Domain`; `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions` (10.0.x) — the "allowed abstractions" of architecture rule 2 |
| Use cases | Sealed classes in `Airlock.Application.UseCases`, called directly; no mediator (N7) |
| Composition | `AddAirlockApplication(IServiceCollection)` registers the use cases, services and options validation. The ports are registered by U3's `AddAirlockInfrastructure`; the host (U4) calls both |
| Options | `AirlockOptions`, bound from the `Airlock` section by the host. An `IValidateOptions<AirlockOptions>` in the Application checks every value, and `ValidateOnStart` makes an invalid value stop startup with a message naming the option (E13) |
| Time | `TimeProvider` (base class library), injected everywhere; the scan limit is a `CancellationTokenSource` created with the `TimeProvider` |
| Concurrency | `Parallel.ForEachAsync` with `MaxDegreeOfParallelism` for correlation; `System.Threading.Channels` for each subscriber's bounded buffer; a small lock or `Interlocked` for the active slot, never held across an `await` |
| Logging | `ILogger<T>` with `[LoggerMessage]` source-generated methods, one per catalogue line, with stable event ids; `BeginScope` carries the scan id (U3's Live Log provider reads it) |
| Shutdown | An `IHostedService` whose `StopAsync` cancels running scans and open reservations and waits for their cleanup within the host's shutdown timeout (AR-30) |
| Errors | Expected refusals are `Outcome<T>` values; unexpected exceptions are caught only at the pipeline boundary (AR-31) and at each cleanup step (U2-NFR-11) |
| Not here | No HTTP, no retries and no rate limiter — those live behind the catalog port in U3 (`Microsoft.Extensions.Http.Resilience`, `System.Threading.RateLimiting`) |

## Test projects

| Project | Packages | Notes |
|---|---|---|
| `Airlock.Application.Tests` (new) | `xunit.v3`, `Microsoft.Testing.Extensions.CodeCoverage`, `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`), `Microsoft.Extensions.Diagnostics.Testing` (`FakeLogger`) | A hand-written in-memory fake for every port, each able to block until cancelled and to fail on demand |
| `Airlock.Architecture.Tests` | adds a reference to `Airlock.Application` | Rules 2, 4 and 6 and the Application clock rule, each with a positive control |

NSubstitute (named in NFR-09) is not needed in U2: hand-written fakes cover every port and stay readable as the
worked-example tables grow. It remains available for the later units.
