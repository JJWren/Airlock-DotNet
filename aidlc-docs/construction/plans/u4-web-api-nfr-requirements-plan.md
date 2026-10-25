# U4 Web API — NFR Requirements Plan

**Stage**: CONSTRUCTION — U4 Web API — NFR Requirements (Standard depth)
**Started**: 2026-10-24T03:48:44Z

## Inputs

- `inception/requirements/requirements.md` (NFR-01 to NFR-03 and NFR-05 to NFR-09)
- `construction/u4-web-api/functional-design/` (WR-01 to WR-23, the rule-to-test map, and what the design leaves to
  U4 NFR and Infrastructure Design)
- The constraints U2 and U3 hand to U4 (their code generation summaries); U3-NFR-05 (uploads copied in fixed-size
  buffers) and U3-NFR-11 (startup)
- `inception/application-design/component-dependency.md` (architecture rule 5)
- Framework facts checked on 2026-10-24:
  - ASP.NET Core 10 generates the public `Program` class that tests need.
  - `WebApplicationFactory` can run a real Kestrel server (`UseKestrel`).
  - OpenAPI 3.1 is the default document version.
  - `SseItem<T>` carries data, an event type, an id and a retry interval, but no comment line.
  - Scalar serves its assets from the application by default, loads fonts from a CDN unless `DisableDefaultFonts()`
    is called, and has an AI agent that `DisableAgent()` turns off.
- Versions on nuget.org on 2026-10-24: `Microsoft.AspNetCore.OpenApi` 10.0.13, `Scalar.AspNetCore` 2.18.3 and
  `Microsoft.AspNetCore.Mvc.Testing` 10.0.13; `MudBlazor` 9.11.0 and `bunit` 2.12.5 arrive with U5.

## Steps

- [x] Identify the quality requirements that are specific to the host and the API
- [x] `construction/u4-web-api/nfr-requirements/nfr-requirements.md` (U4-NFR-01 to U4-NFR-15)
- [x] `construction/u4-web-api/nfr-requirements/tech-stack-decisions.md`
- [x] Bring `unit-of-work.md` and `components.md` in line (the event-stream writer)
- [x] Present for approval (approved 2026-10-24T15:32:18Z)

No open questions: every item follows from approved requirements, the U4 functional design and the U2 and U3
constraints. The new targets it sets are listed for review at the checkpoint:

1. **Stalled uploads**: an upload that delivers no bytes for 60 seconds is ended as if its client had disconnected, so
   a half-open connection cannot hold the single active slot.
2. **Keep-alive comments**: an event stream that has been silent for 15 seconds receives a `: keep-alive` comment
   line, which clients ignore, so proxies keep it open while Syft works on a large image and a vanished client is
   noticed. `SseItem<T>` cannot carry a comment, so Airlock writes its streams with a small writer of its own instead
   of `TypedResults.ServerSentEvents`.
3. **Shutdown**: stopping the server ends every open stream within a second, so shutdown never waits for clients.
4. **Body limits**: `POST /api/v2/scans` accepts `Limits.MaxBytesPerScan` plus at most 1 MiB of multipart framing;
   every other request keeps a 1 MiB limit.
5. **One Airlock per staging root**: a second Airlock started on a staging root that a running one holds stops at
   startup with a message naming the root.
6. **Offline docs**: `/docs` makes no request to any other host — Scalar's bundled assets, its default fonts, its AI
   agent and its request proxy off.
7. **Headers**: API answers carry `X-Content-Type-Options: nosniff` (their JSON is written unescaped) and no `Server`
   header; no CORS headers are ever sent; nothing is compressed.
8. **Plain HTTP**: Airlock serves HTTP, as the original did; TLS is a reverse proxy's job, which the README explains
   (U6). `localhost` counts as a secure context, so the UI's clipboard works.
9. **Performance**: refusals made before the body is read answer within 100 ms once Syft's status is cached; a
   500-finding scan's resource within 250 ms; health within 50 ms from the cache; an event reaches its client within
   100 ms; a 256 MiB upload is staged with under 32 MB allocated.
10. **Coverage** of `Airlock.Web` at the end of U4: at least 90 % of lines and 85 % of branches, as for the
    Application.
