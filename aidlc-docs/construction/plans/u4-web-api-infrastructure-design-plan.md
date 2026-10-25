# U4 Web API — Infrastructure Design Plan

**Stage**: CONSTRUCTION — U4 Web API — Infrastructure Design (Minimal depth)
**Started**: 2026-10-24T16:09:22Z

## Inputs

- `construction/u4-web-api/nfr-design/` (the Kestrel settings, the pipeline, the constraints handed to U6) and
  `nfr-requirements/` (U4-NFR-03, U4-NFR-06, U4-NFR-10)
- `construction/u4-web-api/functional-design/business-rules.md` (WR-17 health, WR-20 host names, WR-23 composition)
- U3's `AddAirlockEnvironmentVariables`, which appends its source to the configuration, and U3's options
  (`Airlock:Nvd`, `Airlock:Syft`, `Airlock:Staging`)
- The original's deployment (`inception/reverse-engineering/architecture.md`): Uvicorn on port 8000 for the API and
  its `/docs`, the Vite dev server on port 3000 for the UI, both published by docker-compose

## Steps

- [x] Resolve the open question below (answered: A, after a check of the local Docker stack)
- [x] `construction/u4-web-api/infrastructure-design/infrastructure-design.md` — the port and listening addresses, the
      Kestrel settings, the configuration sources and their precedence, the environments, health-check wiring,
      logging, secrets and data protection, shutdown
- [x] `construction/u4-web-api/infrastructure-design/deployment-architecture.md` — the local and container topologies
      (validated Mermaid, with a text alternative), outbound connections, disk, and what is handed to U6
- [x] Present for approval (approved 2026-10-24T18:12:56Z)

## Question

### Q-U4-I1 — Airlock's port
The original served its API (and `/docs`) on port 8000 and its UI on port 3000. The rebuild serves everything from one
host on one port. .NET's container images listen on 8080 unless told otherwise.

A) 8080 everywhere, the port .NET's container images already use, so `http://localhost:8080` is the address locally
   and from Docker alike (Recommended)
B) 8000, the original API's port, where its `/docs` lived
C) 3000, where the original UI was
D) Other

[Answer]: A, once nothing else uses it. The owner asked for a check of the local Docker stack first: on 2026-10-24 no
compose file or running container there published host port 8080, and nothing on this machine listened on 8080, 8000
or 3000. The stack's own .NET services also listen on 8080 inside their containers and publish other host ports, so
the compose file (U6) publishes Airlock's port through a variable that can move it.

## Decisions taken in this design (object at the gate if any is wrong)

1. **Loopback when run locally**: `dotnet run` and the published app listen on `http://localhost:<port>` only, as
   Uvicorn does by default, so nothing on the network reaches a server without authentication. Inside the container
   Airlock listens on all of the container's interfaces, which publishing needs; U6 publishes the port on the host's
   loopback only by default.
2. **Configuration precedence**, lowest to highest: the options' defaults in code; `appsettings.json`;
   `appsettings.{Environment}.json`; user-secrets (Development only); environment variables such as
   `Airlock__Limits__MaxArtifactsPerScan`; the original's names `NVD_API_KEY`, `SYFT_BINARY_PATH` and `LOG_LEVEL`;
   the command line. The host inserts U3's source just before the command line, so the documented names win over
   `Airlock__*` variables but the command line still wins over everything.
3. **The Airlock defaults stay in code**, not in `appsettings.json`, so there is one source of truth; the README (U6)
   lists every setting with its default and range.
4. **No ASP.NET Core health-check middleware**: `GET /api/v2/system/health` serves the status pill, CI and any
   container check. It answers 200 whenever the process serves requests, and its body says `ONLINE` or `DEGRADED`.
5. **Console logs carry UTC timestamps** (simple formatter); console verbosity follows `LOG_LEVEL`, with
   `Microsoft.AspNetCore` at Warning.
6. **Development** comes from `launchSettings.json`, which `dotnet run` uses and which opens the browser at Airlock's
   address; every other start is Production. Development adds user-secrets, where the NVD key goes
   (`dotnet user-secrets set Airlock:Nvd:ApiKey …`).
7. **Data protection keys** (Blazor uses them for its circuits and antiforgery) stay where ASP.NET Core puts them by
   default; nothing depends on them surviving a restart, because Airlock keeps no sessions or logins.
8. **A graceful stop takes under 10 seconds** — scans cancelled, Syft killed, staging deleted, streams ended — which
   fits Docker's default stop grace period.
