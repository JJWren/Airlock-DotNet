# U4 Web API — Infrastructure Design

**Stage**: CONSTRUCTION — U4 Web API — Infrastructure Design (Minimal depth)

How the host is reached, configured, observed and stopped. Airlock needs no cloud resources: one process, one port,
local disk, and outbound HTTPS to NVD. The container itself — image, user, volumes, compose — is U6's; this document
fixes what the application expects of it. The topology is in `deployment-architecture.md`.

## Port and listening addresses (Q-U4-I1)

| How Airlock runs | Address | Set by |
|---|---|---|
| `dotnet run --project src/Airlock.Web` | `http://localhost:8080`, Development, opening the browser there | `Properties/launchSettings.json` (`applicationUrl`) |
| The published application started directly | `http://localhost:8080`, Production | `"Urls": "http://localhost:8080"` in `appsettings.json` |
| The container | `http://+:8080`, every interface inside the container, so the port can be published | `ASPNETCORE_URLS` in the image (U6); an environment variable outranks `appsettings.json`. Without it the container would listen on its own loopback and be unreachable, so U6's smoke test checks it |
| The host machine, for the container | `127.0.0.1:8080` by default, movable | The compose file (U6), through a port variable |

Locally Airlock listens on loopback only, as Uvicorn does by default, so a server without authentication is not
reachable from the network unless an operator decides otherwise (`deployment-architecture.md`).

## Kestrel

| Setting | Value | From |
|---|---|---|
| Protocol | HTTP/1.1 without TLS; no HTTPS redirection, no HSTS | U4-NFR-06 |
| `AddServerHeader` | false | U4-NFR-06 |
| `Limits.MaxRequestBodySize` | 1 MiB; the submission raises its own to `Limits.MaxBytesPerScan` + 1 MiB | U4-NFR-03 |
| Minimum request and response data rates | ASP.NET Core's defaults: 240 bytes a second after a 5-second grace | U4-NFR-04 |
| Request-header and keep-alive timeouts | ASP.NET Core's defaults: 30 and 130 seconds | U4-NFR-04 |
| Header and request-line sizes | ASP.NET Core's defaults: 32 KiB of headers, 8 KiB per request line | — |

## Configuration sources, lowest to highest

| # | Source | Example | Notes |
|---|---|---|---|
| 1 | The options' defaults in code | `Limits.MaxArtifactsPerScan` = 25 | One source of truth; `appsettings.json` repeats none of them, and the README (U6) lists every setting with its default and range |
| 2 | `appsettings.json` | `Urls`, `AllowedHosts`, `Logging`, and from U5 `DisableStaticAssetNotFoundRuntimeFallback` | Shipped with the application. The last keeps the static files' development fallback, which a run from the build output would add, from turning the API's 405 into 404 (U5) |
| 3 | `appsettings.{Environment}.json` | — | Not needed today; nothing differs between environments |
| 4 | User-secrets, Development only | `Airlock:Nvd:ApiKey` | Where a developer keeps the NVD key: `dotnet user-secrets set Airlock:Nvd:ApiKey <key> --project src/Airlock.Web` |
| 5 | Environment variables | `Airlock__Limits__MaxArtifactsPerScan=10`, `ASPNETCORE_URLS`, `AllowedHosts` | .NET's own convention, `__` for `:` |
| 6 | The original's variable names | `NVD_API_KEY`, `SYFT_BINARY_PATH`, `LOG_LEVEL` | U3's `AddAirlockEnvironmentVariables` (IR-23); `AirlockHost` moves its source in front of the command line, because it appends at the end. `WebApplicationBuilder` holds two command-line sources — the host's, early, and the application's, last — so `ConfigurationOrder` puts it in front of the last one |
| 7 | The command line | `--Airlock:Limits:MaxArtifactsPerScan=10` | Wins over everything |

So the documented names win over the `Airlock__*` spellings, and a value given on the command line wins over both.
Every value is validated at startup (U2, U3); an invalid one stops Airlock with a message naming it (E13).

The settings an operator is most likely to touch:

| Setting | Configuration key | Environment variable |
|---|---|---|
| NVD API key | `Airlock:Nvd:ApiKey` | `NVD_API_KEY` |
| Syft binary | `Airlock:Syft:BinaryPath` | `SYFT_BINARY_PATH` |
| Console verbosity | `Logging:LogLevel:Default` | `LOG_LEVEL` |
| Listening address | `Urls` | `ASPNETCORE_URLS` |
| Host names answered | `AllowedHosts` | `AllowedHosts` |
| Directories a scan may target | `Airlock:AllowedScanRoots:0`, `:1`, … | `Airlock__AllowedScanRoots__0`, … |
| Staging root | `Airlock:Staging:Root` | `Airlock__Staging__Root` |
| Limits, timeouts, retention | `Airlock:Limits:…`, `Airlock:Timeouts:…`, `Airlock:Retention:…` | `Airlock__Limits__…`, … |

## Environments

| | Development | Production |
|---|---|---|
| Chosen by | `launchSettings.json` (`ASPNETCORE_ENVIRONMENT=Development`) | The default for every other start, the container included |
| User-secrets | Read (`UserSecretsId` in `Airlock.Web.csproj`) | Not read |
| Errors | API errors are problems in both (U4-NFR-06); the UI shows the developer exception page | API errors are problems; the UI shows its own error page (U5) |

## Health-check wiring

No ASP.NET Core health-check middleware is added. `GET /api/v2/system/health` (WR-17) serves the status pill, CI
jobs and any container health check: it answers 200 whenever the process serves requests, and its body says `ONLINE`
or `DEGRADED` with the checks. A container check therefore asks `http://localhost:8080/api/v2/system/health` and
expects 200 (U6); readiness to scan is the body's `status`.

## Logging

The console provider with the simple formatter, one line per entry, UTC timestamps (`yyyy-MM-ddTHH:mm:ss.fffZ`), the
default level Information and `Microsoft.AspNetCore` at Warning — all in `appsettings.json`, with `LOG_LEVEL` moving
the default (IR-18). The Live Log provider (U3) runs beside it. There are no log files: in the container the console is
what `docker logs` shows.

## Secrets and data protection

- **The NVD key** comes from user-secrets in development and from `NVD_API_KEY` in the container (a compose `.env`
  file, U6). It is never written to an `appsettings` file in the repository, never logged (U3-NFR-07) and never
  answered (U4-NFR-06).
- **Data protection keys**, which Blazor uses for its circuits and antiforgery tokens, stay where ASP.NET Core puts
  them: the user's profile folder locally, the application user's home folder in the container, or memory when
  neither is writable, with a warning at startup. Airlock keeps no sessions or logins, so nothing depends on the keys
  surviving a restart; an open tab simply reloads.

## Shutdown

Ctrl+C or `SIGTERM` starts a graceful stop: the event streams end within a second (U4-NFR-05), the Application cancels
running scans and waits for their cleanup — Syft's process tree killed, staging deleted (U2 AR-30, U3-NFR-04) — and
the staging-root lock is released (U4-NFR-09). The host's shutdown timeout stays at its 30-second default, and the stop
normally takes a few seconds, inside Docker's default 10-second grace period.
