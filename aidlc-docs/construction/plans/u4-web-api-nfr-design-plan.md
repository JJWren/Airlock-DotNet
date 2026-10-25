# U4 Web API — NFR Design Plan

**Stage**: CONSTRUCTION — U4 Web API — NFR Design (Standard depth)
**Started**: 2026-10-24T15:32:18Z

## Inputs

- `construction/u4-web-api/nfr-requirements/` (U4-NFR-01 to U4-NFR-15, tech-stack decisions)
- `construction/u4-web-api/functional-design/` (WR-01 to WR-23 and the rule-to-test map)
- `construction/u3-infrastructure/nfr-design/logical-components.md` (the constraints handed to U4), U3's
  `StagingSweepService` and U2's use cases
- `inception/application-design/component-methods.md`, `component-dependency.md` (rule 5) and `services.md`
- Scalar's own source for its ASP.NET Core options (`integrations/dotnet` in github.com/scalar/scalar, whose changelog
  is at 2.18.3, the release U4 uses), read on 2026-10-24:
  - the bundled assets are the default (`BundleUrl` unset);
  - fonts come from Scalar's CDN unless `DefaultFonts` is false;
  - `Telemetry` is on by default and reports whether a request was sent through Scalar's client;
  - `DisableAgent()` turns off Scalar's AI chat, which works on localhost without a key;
  - `ProxyUrl` is null, so no proxy, by default;
  - `OpenApiRoutePattern` defaults to `/openapi/{documentName}.json`.

## Steps

- [x] Choose a pattern for each U4 NFR, and the values left to this stage →
      `construction/u4-web-api/nfr-design/nfr-design-patterns.md`
- [x] Lay out the Web's namespaces, the host's registrations and pipeline, and how the endpoints reach the use cases
      (validated Mermaid, with text alternatives), the checks and the constraints handed to U5 and U6 →
      `construction/u4-web-api/nfr-design/logical-components.md`
- [x] Feed the refinements back: WR-13 (keep-alive comments), U3's IR-13 (the staging-root lock),
      `component-methods.md`, `unit-of-work.md` and the U4 tech-stack document
- [x] Present for approval (approved 2026-10-24T16:09:22Z)

No open questions. The choices made here are listed for review at the checkpoint:

1. **Body limits**: Kestrel's server-wide limit is 1 MiB; the submission raises its own to `Limits.MaxBytesPerScan`
   plus 1 MiB only after the reservation succeeds, so a request refused before its body is read keeps the small limit
   and Kestrel drops the unread body quickly.
2. **Stalled uploads**: the body is read through a stream whose 60-second idle timer, on the `TimeProvider`, restarts
   with every read that returns data; when it fires the reservation is disposed and the connection aborted.
3. **Event streams**: one `IResult` of Airlock's own writes both streams — `event:` and one `data:` line per event, a
   `: keep-alive` comment after 15 seconds of silence, `X-Accel-Buffering: no` so an nginx proxy does not buffer, and
   a flush after each write. When the host starts stopping, a stream ends within a second, a scan's stream normally
   first with `finished`.
4. **Errors**: the exception handler and the status-code pages run in a branch for `/api` only, so API errors are
   problems in every environment and the UI keeps its own error handling. Problem kinds are filled in by
   `CustomizeProblemDetails` from one table in `ApiProblems`.
5. **JSON**: the Web has contract enumerations of its own, each value named with `[JsonStringEnumMemberName]` and
   mapped from the Domain's with exhaustive switches, so renaming a Domain value cannot silently change the API, and
   the OpenAPI document lists the values.
6. **Docs**: Scalar with `DefaultFonts = false`, `Telemetry = false` (a setting the requirements did not name: it is on
   by default) and `DisableAgent()`, its proxy and bundled assets left at their defaults.
7. **One Airlock per staging root**: U3's `StagingSweepService` first takes an exclusive lock on
   `<Staging:Root>/.airlock-lock` and holds it until the host stops; a second Airlock stops at startup with "Another
   Airlock is using the staging root <root>". The operating system releases the lock if Airlock crashes. A small
   amendment to U3's code, made in U4 Code Generation.
8. **Logging**: a `WebLog` catalogue in `Airlock.Web` with event ids 3100 to 3399; client text passes through U2's
   `ExternalText.Clean` before it is logged.
9. **Tests**: one `WebApplicationFactory<Program>` with fakes for Syft and NVD and a temporary staging root per
   factory; `UseKestrel()` only for the body-limit tests; a fake clock for the idle timer and the keep-alive.
