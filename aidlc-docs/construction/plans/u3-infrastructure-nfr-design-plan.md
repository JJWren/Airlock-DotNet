# U3 Infrastructure — NFR Design Plan

**Stage**: CONSTRUCTION — U3 Infrastructure — NFR Design (Standard depth)
**Started**: 2026-10-23T14:21:08Z

## Inputs

- `construction/u3-infrastructure/nfr-requirements/` (U3-NFR-01 to U3-NFR-14, tech-stack decisions)
- `construction/u3-infrastructure/functional-design/` (IR-01 to IR-26 and the rule-to-test map)
- `construction/u2-application/nfr-design/logical-components.md` (the constraints handed to U3) and the Application's
  ports in `src/Airlock.Application/Ports`
- `inception/application-design/component-methods.md`, `component-dependency.md` and `services.md` (Infrastructure)

## Steps

- [x] Choose a pattern for each U3 NFR, and the values left to this stage →
      `construction/u3-infrastructure/nfr-design/nfr-design-patterns.md`
- [x] Lay out the Infrastructure's namespaces, registrations and connections (validated Mermaid, with text
      alternatives), the checks and the constraints handed to U4 and U6 →
      `construction/u3-infrastructure/nfr-design/logical-components.md`
- [x] Feed back the refinements found while designing into the functional design (IR-01, IR-11, IR-13, IR-22, IR-23,
      the options and the staging layout), `component-methods.md`, `components.md`, `component-dependency.md`,
      `services.md`, `unit-of-work.md` and both tech-stack documents
- [x] Present for approval (approved 2026-10-24T00:50:42Z)

No open questions. The choices made here are listed for review at the checkpoint:

1. **NVD resilience runs inside `NvdApiClient`** on `Polly.Core` and `Polly.RateLimiting` — the same Polly v8
   strategies — instead of `Microsoft.Extensions.Http.Resilience`'s HTTP handler. A handler's attempt ends when the
   headers arrive, so the body, read as it streams (U3-NFR-05), would sit outside the attempt timeout and the retries
   (U3-NFR-02).
2. **NVD numbers**: 5 attempts; backoff 2 seconds doubling up to 30, ±20 % jitter, or a `Retry-After` of 1 to 60
   seconds; a 30-second attempt timeout; the 2-minute lookup deadline. An NVD that refuses at once ends a scan in about
   a minute, one that never answers in about 4 (U2-NFR-05: 5).
3. **The pacer** queues requests in arrival order, one start every 600 ms; a cancelled waiter gives up its turn.
4. `Syft:StatusCacheDuration` defaults to 30 seconds (1 second to 1 hour); `Staging:Root` must be a full path.
5. **Syft's temporary files** go to a folder of the run's own under the staging root, deleted after the run, so a
   killed Syft leaves nothing behind; Syft's environment loses the NVD key by name and by value.
6. **Staging folders carry a `.airlock` marker**; at startup, marked folders left by an earlier run are deleted, and
   nothing unmarked is touched. Deletion makes read-only files and folders writable before trying again.
7. **Hostile NuGet packages**: at most 65,534 archive entries (65,535 means ZIP64) and 1 MB of `.nuspec`; XML read
   with DTDs prohibited.
8. **`LOG_LEVEL`** accepts the original's names (`DEBUG`, `INFO`, `WARNING`, `ERROR`, `CRITICAL`) as well as .NET's.
9. **Log levels**: each NVD request at Trace, since the Application already logs each lookup at Debug; retries and
   malformed CPEs at Warning.
10. **Tests**: a small stand-in program plays Syft for the process tests, and PdfPig (package `PdfPig`) reads the PDF's text
    in the smoke test.
