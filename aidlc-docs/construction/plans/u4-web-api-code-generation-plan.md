# U4 Web API — Code Generation Plan

**Stage**: CONSTRUCTION — U4 Web API — Code Generation, Part 1 (Planning) · **Depth**: Standard
**Started**: 2026-10-24T18:12:56Z

## Inputs

- `construction/u4-web-api/functional-design/` (WR-01 to WR-23, the problem catalogue, the worked examples, the
  rule-to-test map)
- `construction/u4-web-api/nfr-requirements/`, `nfr-design/` and `infrastructure-design/`
- `inception/application-design/component-methods.md` (Web API section) and `component-dependency.md` (rule 5)
- The U2 and U3 code on `feature/u3-infrastructure`: the use cases, `Outcome<T>` and `RefusalCode`, the events,
  `AirlockOptions`, `AddAirlockApplication`, `AddAirlockInfrastructure`, `AddAirlockEnvironmentVariables`,
  `StagingSweepService`, `ExternalText`
- U3's recorded NVD answers (the descriptions for the worked example) and the scratchpad's Syft v1.55.0

## Ground rules

- Code lives at the repository root (`src/`, `tests/`, `docs/`); `aidlc-docs/construction/u4-web-api/code/` gets a
  summary only.
- Nothing is committed during generation. At the completion checkpoint I propose two commits — the U4 design
  documents with the earlier documents they amended, then the U4 code — on the branch chosen in Q-CG4-1, and make them
  only if you say so. Nothing is pushed.
- Package versions, checked on nuget.org on 2026-10-24: `Microsoft.AspNetCore.OpenApi` 10.0.13 (it brings
  `Microsoft.OpenApi` 2.12.2), `Scalar.AspNetCore` 2.18.3 and, for the tests, `Microsoft.AspNetCore.Mvc.Testing`
  10.0.13 (it brings `Microsoft.AspNetCore.TestHost` and some `Microsoft.Extensions` packages). With their
  dependencies, restoring downloads up to 20 packages, about 8.5 MB, from nuget.org: `Scalar.AspNetCore` 4.7 MB,
  `Microsoft.OpenApi` 0.7 MB, `System.Diagnostics.EventLog` 0.5 MB, and 17 of under 0.3 MB each. .NET 10's package
  pruning may skip the `Microsoft.Extensions` ones the ASP.NET Core shared framework already provides. Everything else
  is in the local cache.
- Every file must build with warnings as errors. Analyzer findings are fixed, not suppressed; the only exceptions are
  the test-project ones U1 set (CA1707, CA1515). A name an analyzer forces to change is updated in the design documents
  in step H2.
- No test touches the network; the Kestrel tests bind a free loopback port. Before the commit, the staged files are
  checked for keys, cookies and personal paths.

## Design refinements made while planning

Each keeps the approved behaviour and is recorded in the design documents in step H2.

1. **Two small methods on U2's `ScanReservation`**: `RefuseBothTargets()` and `RefuseUnreadableTargetPath()`. The
   reader refuses "not both" as soon as both kinds of target have arrived, and an unreadable `targetPath` at once
   (WR-07), with the Application's own messages, recorded and logged like every other refusal (U2 AR-03), so the Web
   repeats none of them.
2. **The configuration move lives in `Program`**: it adds U3's source and moves it in front of the command line; U3's
   extension is unchanged.
3. **JSON line ends are `\n`** on every system, so the API answers the same bytes on Windows and Linux and the golden
   file of the worked example holds on both.
4. **Version 2.0.0** goes into `Directory.Build.props` (FD decision 17); the NVD client's `User-Agent` follows it.
5. **Until U5 there is no page at `/`**: `launchSettings.json` opens `/docs`, and U5 moves it to the UI.

## Changes made during generation

Recorded as deviations 1 to 12 in `construction/u4-web-api/code/code-generation-summary.md` and in the design
documents (H2). In short: the composition root is `AirlockHost`, and refinement 2's move lives in its
`ConfigurationOrder`, in front of the last command-line source. U2 gains `Refusals.ScanNotFound`, a shutdown that
cancels in `StoppingAsync`, and an event hub that removes a subscriber leaving right after its snapshot.
`EventStreamResult` cancels its source when it ends. The submission declares no `Accepts`, and Scalar's developer
toolbar is hidden. The tests that need a failed start build `AirlockHost` themselves. Some names changed (summary,
deviation 11).

## Steps

### A. Branch and packages
- [x] A1 Branch as chosen in Q-CG4-1; the uncommitted U4 design documents come along
- [x] A2 `Directory.Packages.props` — a Web group (`Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`) and
      `Microsoft.AspNetCore.Mvc.Testing` under Tests; `<Version>2.0.0</Version>` in `Directory.Build.props`

### B. Amendments to U2 and U3
- [x] B1 `ScanReservation.RefuseBothTargets()` and `RefuseUnreadableTargetPath()` (refinement 1); rows in
      `ScanReservationTests`
- [x] B2 `StagingSweepService` takes the exclusive lock on `<Staging:Root>/.airlock-lock` before it sweeps and releases
      it when the host stops (U4-NFR-09, IR-13); tests in `Airlock.Infrastructure.Tests`

### C. `src/Airlock.Web`
- [x] C1 `Airlock.Web.csproj` — the Web SDK, the three Airlock projects, the packages, a `UserSecretsId`,
      `InternalsVisibleTo` `Airlock.Web.Tests`; added to `Airlock.slnx`
- [x] C2 `appsettings.json` (`Urls`, `AllowedHosts`, `Logging`) and `Properties/launchSettings.json`
      (`http://localhost:8080`, Development, opening `/docs`)
- [x] C3 `Program` — configuration sources, Kestrel, options and layers, JSON, problems, OpenAPI, the `/api` branch,
      the endpoints and Scalar, in the order of `logical-components.md`
- [x] C4 `Api/Contracts` — the DTOs, the contract enumerations with `[JsonStringEnumMemberName]`, `ApiJsonContext`,
      `ApiJson` (indented with `\n` and the relaxed encoder for HTTP bodies, compact for events)
- [x] C5 `Api/Mapping` — `ScanDtoMapper` and `LogEntryDtoMapper`, with exhaustive switches over the Domain's values
- [x] C6 `Api/Problems/ApiProblems` — the catalogue, `ForRefusal`, the Web's own problems, `CustomizeProblemDetails`
- [x] C7 `Api/Uploads` — `MultipartScanReader` (WR-06 to WR-08) and `IdleTimeoutStream` (U4-NFR-04)
- [x] C8 `Api/Streams/EventStreamResult` (WR-12, WR-13, WR-18; U4-NFR-05)
- [x] C9 `Api/Security` — `CrossSiteRequestFilter` (WR-19) and `ApiResponseHeaders` (U4-NFR-06)
- [x] C10 `Api` — `ApiEndpoints.MapAirlockApi`, `ScanEndpoints`, `SystemEndpoints`, `LogEndpoints` (WR-01 to WR-18), with
      their OpenAPI metadata
- [x] C11 `Api/Docs` — `OpenApiTransformers` (schema names, the multipart body, problem answers, the streams) and
      `ScalarSetup` (U4-NFR-07)
- [x] C12 `Logging/WebLog` (U4-NFR-11)

### D. In the repository
- [x] D1 `docs/api/problems.md` — every problem kind under its anchor, with its status, title, meaning and what a
      client can do (Q-U4-2)

### E. `tests/Airlock.Web.Tests`
- [x] E1 Project file — `xunit.v3`, `Microsoft.AspNetCore.Mvc.Testing`, the coverage extension,
      `Microsoft.Extensions.TimeProvider.Testing`, `Microsoft.Extensions.Diagnostics.Testing`; references
      `Airlock.Web`; added to `Airlock.slnx`
- [x] E2 Helpers — `AirlockWebFactory`, fakes for `ISbomGenerator` and `IVulnerabilityCatalog`, scan builders, a
      multipart builder, an event-stream reader, a generated source stream, temporary folders
- [x] E3 Fixture — the worked example's finished scan as a golden file, its descriptions in full from U3's recorded NVD
      answers
- [x] E4 Rule tests (the rule-to-test map): `ScanResourceTests`, `JsonContractTests`, `ProblemDetailsTests`,
      `ScanSubmissionTests`, `ScanEventStreamTests`, `ReportEndpointTests`, `StopEndpointTests`,
      `HealthEndpointTests`, `LiveLogStreamTests`, `CrossSiteTests`, `OpenApiDocumentTests`, `HostCompositionTests`
- [x] E5 NFR checks (`logical-components.md`): `StreamingUploadTests`, `BodyLimitTests` (on Kestrel),
      `StalledUploadTests`, `KeepAliveAndShutdownTests`, `ResponseHeaderTests`, `SecretHygieneTests`,
      `OfflineDocsTests`, `PerformanceSmokeTests`, `LeakTests`

### F. `tests/Airlock.Architecture.Tests`
- [x] F1 Reference `Airlock.Web`
- [x] F2 `WebRules` — rule 5 for the API, with its positive control (`Program`)

### G. Verify
- [x] G1 `dotnet build` — zero warnings
- [x] G2 `dotnet test` with `SYFT_BINARY_PATH` set to the scratchpad's Syft — every test passes; record the counts
- [x] G3 Coverage of `Airlock.Web` against 90 % of lines and 85 % of branches (U4-NFR-14); U1 to U3 still at theirs
- [x] G4 Confirm the worked examples of the logic model are test rows
- [x] G5 A temporary probe shows rule 5 fails on a violation; then it is deleted
- [x] G6 Run the host with `dotnet run` (port 8080 checked free first) and the scratchpad's Syft: health, a submission
      refused 503 without an NVD key, a cross-site `POST` refused 403, an unknown host refused 400, `/openapi/v2.json`,
      and `/docs` in the browser pane with its network log showing no request to another host. A full scan needs an
      NVD key and waits for Build and Test

### H. Documentation
- [x] H1 `aidlc-docs/construction/u4-web-api/code/code-generation-summary.md` — files, test counts, coverage,
      deviations and the constraints handed to U5 and U6
- [x] H2 Update the design documents for refinements 1 to 5 and any name changed during implementation
- [x] H3 Tick this plan; update `aidlc-state.md` and `audit.md`

## Questions

### Q-CG4-1 — Branch for U4
U3 is approved and committed on `feature/u3-infrastructure`; `main` holds U2. U4 builds on the U3 code and amends U2
and U3 slightly (B1, B2).

A) Fast-forward `main` to `feature/u3-infrastructure`, then branch `feature/u4-web-api` from `main`, as for U2 and U3
   (Recommended)
B) Branch `feature/u4-web-api` from `feature/u3-infrastructure`; `main` stays at U2
C) Keep working on `feature/u3-infrastructure`
D) Other

[Answer]: A

### Plan approval
A) Request Changes
B) Approve and generate (restores up to 20 packages, about 8.5 MB, from nuget.org)

[Answer]: B (approved 2026-10-24T18:17:30Z)

## Completion

### Q-CG4-2 — Commits
A) Two commits on `feature/u4-web-api`: the design documents, with the earlier documents they amend and ADR-0007, then
   the code; the staged files scanned for keys and personal paths first; nothing pushed (Recommended)
B) One commit with everything
C) Don't commit yet

[Answer]: A

### Q-CG4-3 — `.claude/launch.json`
A) Leave it uncommitted: it is local tooling for the desktop app's preview (Recommended)
B) Commit it with the code

[Answer]: A

### Code generation approval
A) Request Changes
B) Continue to Next Stage

[Answer]: B (approved 2026-10-25T01:22:04Z)
