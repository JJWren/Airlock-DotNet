# U2 Application — Code Generation Plan

**Stage**: CONSTRUCTION — U2 Application — Code Generation, Part 1 (Planning) · **Depth**: Comprehensive
**Started**: 2026-10-22T13:13:52Z · **Part 2 completed**: 2026-10-22T13:55:22Z (`u2-application/code/code-generation-summary.md`)

## Inputs

- `construction/u2-application/functional-design/` (Application types, AR-01 to AR-35, logic model, rule-to-test map)
- `construction/u2-application/nfr-requirements/` and `construction/u2-application/nfr-design/`
- `inception/application-design/component-methods.md` (Application section) and `services.md`
- The U1 code on `feature/u1-domain`

## Ground rules

- Code lives at the repository root (`src/`, `tests/`); `aidlc-docs/construction/u2-application/code/` gets a summary
  only.
- Nothing is committed during generation. At the completion checkpoint I propose the commits — the U2 design documents,
  then the U2 code — on the branch chosen in Q-CG2-1, and make them only if you say so. Nothing is pushed.
- Package versions, checked on nuget.org on 2026-10-22: `Microsoft.Extensions.Logging.Abstractions`,
  `Microsoft.Extensions.Options`, `Microsoft.Extensions.DependencyInjection.Abstractions` and
  `Microsoft.Extensions.Hosting.Abstractions` 10.0.13; `Microsoft.Extensions.TimeProvider.Testing` and
  `Microsoft.Extensions.Diagnostics.Testing` 10.11.0. With their dependencies, restoring downloads 18 packages,
  4.5 MB in total, from nuget.org; none is in the local cache.
- Every file must build with warnings as errors. Analyzer findings are fixed, not suppressed; the only exceptions are
  the test-project ones U1 set (CA1707, CA1515). If an analyzer forces a rename of a designed name, the design documents
  are updated in step G2.

## Design refinements made while planning

Found while laying out the code; each keeps the approved behaviour and is recorded in the design documents in step G2.

1. **Retention policy in the Application.** `IScanRegistry.Prune(now)` becomes `List()` and `Remove(id)`, and a
   `RetentionPolicy` in the Application decides what to remove. AR-33 is then tested in U2, and U3's registry is a
   plain store.
2. **The byte limit as a value.** `IStagingArea.AddAsync` returns `ArtifactStaged` or `ArtifactTooLarge` instead of
   signalling the limit with an exception, in line with "expected refusals are values".
3. **Scan-scoped log lines are routed, not subscribed.** U3's Live Log provider hands every entry that carries a scan
   id to the Application's `ScanLogRouter`, which publishes it on that scan's hub as it is written. A hub that
   subscribed to the Live Log could miss a scan's first lines; `ILiveLog` keeps only the global subscription.
4. **The hub owns the latest version.** `ScanEventHub` takes each new scan version together with its event, under its
   own short lock, so a new subscriber's snapshot and the events after it never overlap or leave a gap. This replaces
   the copy-on-write wording of U2-NFR-07; the pipeline still never waits for a subscriber.
5. **The startup warning about missing scan roots** lives in the hosted service, now named `ScanLifetimeService`
   (start: warn; stop: cancel the running scans and wait for their cleanup).

## Steps

### A. Branch and packages
- [x] A1 Branch as chosen in Q-CG2-1; the uncommitted U2 design documents come along
- [x] A2 `Directory.Packages.props` — the six packages above

### B. Domain amendment (Q-U2-3)
- [x] B1 `ScanWarningCode.NotProductMatched`, with a test row in `SmallValueTests`

### C. `src/Airlock.Application`
- [x] C1 `Airlock.Application.csproj` — `Airlock.Domain` and the four abstraction packages; `InternalsVisibleTo`
      `Airlock.Application.Tests`; added to `Airlock.slnx`
- [x] C2 `Outcomes/` — `Outcome<T>`, `Refusal`, `RefusalCode`
- [x] C3 `Events/` — `ScanEvent` and its five records, `LiveLogEntry`, `ActiveScanState`
- [x] C4 `Ports/` — the seven ports and their records, with refinements 1 and 2 and `IHostFileSystem.IsAbsolute`
- [x] C5 `Options/` — `AirlockOptions` and its four groups, `AirlockOptionsValidator` (every range of the values table)
- [x] C6 `Logging/` — `ScanLog` (the catalogue as `[LoggerMessage]` methods), `ScanLogScope`, `ScanLogRouter`,
      `ExternalText`
- [x] C7 `Correlation/` — `PackageCorrelation` and its three records, `NvdOutageBreaker`, `PackageCorrelator`
- [x] C8 `Scanning/` — `EventQueue`, `ScanEventHub`, `ScanRun`, `StopCause`, `PackageMerge`, `RetentionPolicy`,
      `ScanPipeline`, `ScanCoordinator`
- [x] C9 `UseCases/` — `StartScan`, `ScanReservation`, `CancelScan`, `EmergencyStop`, `GetScan`, `WatchScan`,
      `WatchActiveScan`, `RenderScanReport`, `GetSystemHealth`, `WatchLiveLog` and their result types
- [x] C10 `AddAirlockApplication` and `ScanLifetimeService`

### D. `tests/Airlock.Application.Tests`
- [x] D1 Project file — `xunit.v3`, the coverage extension, `Microsoft.Extensions.TimeProvider.Testing`,
      `Microsoft.Extensions.Diagnostics.Testing`; references `Airlock.Application`; added to `Airlock.slnx`
- [x] D2 `Fakes/` — a fake for every port (answers from a table, can block until cancelled, can throw, records calls;
      the host file system in Windows and Linux modes) and a `ScanHarness` that wires the Application to them
- [x] D3 Rule tests (rule-to-test map): `StartScanTests`, `ScanTargetTests`, `SbomGenerationTests`,
      `PackageCorrelatorTests`, `NvdOutageTests`, `ScanResultTests`, `ScanEventStreamTests`, `StoppingScanTests`,
      `RetentionTests`, `RenderScanReportTests`, `GetSystemHealthTests`
- [x] D4 NFR tests: `SlotStressTests`, `CorrelationOracleTests` (seeded), `FaultInjectionTests`,
      `SlowSubscriberTests`, `LogCatalogueTests`, `OptionsValidationTests`, `ExternalTextTests`,
      `PerformanceSmokeTests`

### E. `tests/Airlock.Architecture.Tests`
- [x] E1 Reference `Airlock.Application`
- [x] E2 `ApplicationDependencyRules` (rule 2), `PortRules` (rule 4, Application half), `UseCaseRules` (rule 6),
      `ApplicationDeterminismRules` (the clock rule) — each with a positive control

### F. Verify
- [x] F1 `dotnet build` — zero warnings
- [x] F2 `dotnet test` — every test passes; record the counts
- [x] F3 Coverage of `Airlock.Application` against 90 % of lines and 85 % of branches (U2-NFR-13)
- [x] F4 Confirm the worked examples of the business logic model are test rows
- [x] F5 A temporary probe shows each new architecture rule fails on a violation; then it is deleted

### G. Documentation
- [x] G1 `aidlc-docs/construction/u2-application/code/code-generation-summary.md` — files, test counts, coverage,
      deviations
- [x] G2 Update `component-methods.md`, `services.md` and the U2 design documents for refinements 1 to 5 and any name
      changed during implementation
- [x] G3 Tick this plan; update `aidlc-state.md` and `audit.md`

## Questions

### Q-CG2-1 — Branch for U2
U1 is approved and committed on `feature/u1-domain`; `main` holds only the documents commit. U2 builds on the U1 code.

A) Fast-forward `main` to `feature/u1-domain`, then branch `feature/u2-application` from `main`
B) Branch `feature/u2-application` from `feature/u1-domain`; `main` stays where it is
C) Keep working on `feature/u1-domain`
D) Other

[Answer]: A

### Plan approval
A) Request Changes
B) Approve and generate (restores the 18 packages, 4.5 MB, from nuget.org)

[Answer]: B (approved 2026-10-22T13:18:38Z)
