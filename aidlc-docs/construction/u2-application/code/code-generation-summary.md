# U2 Application — Code Generation Summary

**Stage**: CONSTRUCTION — U2 Application — Code Generation, Part 2 (Generation)
**Plan**: `construction/plans/u2-application-code-generation-plan.md` (approved 2026-10-22T13:18:38Z)
**Completed**: 2026-10-22T13:55:22Z · **Branch**: `feature/u2-application`, from `main` fast-forwarded to the U1 commit

## Result

| Check | Target | Result |
|---|---|---|
| Build, warnings as errors (F1) | 0 warnings | 0 warnings, 0 errors |
| Application tests (F2) | all pass | 190 of 190 (99 test methods) |
| Domain tests | all pass | 203 of 203 |
| Architecture tests | all pass | 37 of 37: U1's 15 and 22 new |
| Line coverage of `Airlock.Application` (F3) | 90 % | 97.6 % (1,439 of 1,474 lines) |
| Branch coverage of `Airlock.Application` (F3) | 85 % | 95.7 % (450 of 470 branches) |
| Worked examples are test rows (F4) | every example | every example the Application owns (table below) |
| New architecture rules fail on violations (F5) | — | Temporary probes broke the 6 targeted rules and only those; probes deleted |

The uncovered lines are defensive: races the fakes cannot stage (a reservation stopped between validation and launch,
a run disposed while it is being stopped) and cleanup steps that cannot fail in memory.

Run them with:

```bash
dotnet build Airlock.slnx
```

```bash
dotnet test --coverage --coverage-output-format cobertura
```

## Files

### `src/Airlock.Application` — 33 files, references `Airlock.Domain` and four Microsoft.Extensions abstraction packages

| Folder | Types |
|---|---|
| `Outcomes/` | `Outcome<T>`, `Outcome` (its factories), `Refusal`, `RefusalCode` |
| `Events/` | `ScanEvent`, `ScanSnapshot`, `PhaseChanged`, `PackagesCorrelated`, `ScanLogged`, `ScanFinished`, `LiveLogEntry`, `ActiveScanState` |
| `Ports/` | `ISbomGenerator`, `IVulnerabilityCatalog`, `IArtifactStaging`, `IStagingArea`, `IScanRegistry`, `IScanReportRenderer`, `ILiveLog`, `IHostFileSystem`, and their records |
| `Options/` | `AirlockOptions` and its four groups, `AirlockOptionsValidator` |
| `Logging/` | `ScanLog` (the catalogue, 27 `[LoggerMessage]` methods), `ScanLogScope`, `ScanLogRouter`, `ExternalText` |
| `Correlation/` | `PackageCorrelator`, `NvdOutageBreaker`, `PackageCorrelation` and its three records |
| `Scanning/` | `ScanCoordinator`, `ScanPipeline`, `ScanEventHub`, `ArtifactName`; internal `ScanRun`, `StopCause`, `ReservationTicket`, `PackageMerge`, `RetentionPolicy`, `EventQueue` |
| `UseCases/` | `StartScan`, `ScanReservation`, `CancelScan`, `EmergencyStop`, `GetScan`, `WatchScan`, `WatchActiveScan`, `RenderScanReport`, `GetSystemHealth`, `WatchLiveLog` and their result types |
| root | `ApplicationServiceCollectionExtensions` (`AddAirlockApplication`), internal `ScanLifetimeService` |

### `tests/Airlock.Application.Tests` — 24 files

| File | Covers |
|---|---|
| `Fakes/` | A fake for every port — answers from a table, can block until cancelled, can fail, records calls — plus a Windows/Linux host file system, test streams and a logger provider that routes scan-scoped lines the way U3's will |
| `ScanHarness.cs`, `A.cs` | The Application wired to the fakes, a fake clock and a fake logger; test-data builders |
| `UseCases/StartScanTests.cs` | AR-01 to AR-03, AR-26 |
| `UseCases/ScanTargetTests.cs` | AR-04 to AR-07 in both host modes, the upload limits, `ArtifactName` |
| `Scanning/SbomGenerationTests.cs` | AR-08 to AR-12 |
| `Correlation/PackageCorrelatorTests.cs` | AR-13 to AR-17 |
| `Correlation/NvdOutageTests.cs` | AR-18 |
| `Scanning/ScanResultTests.cs` | AR-19, AR-20 |
| `Scanning/ScanEventStreamTests.cs` | AR-21 to AR-24 |
| `Scanning/StoppingScanTests.cs` | AR-27 to AR-32 |
| `UseCases/ReadingUseCaseTests.cs` | AR-33 to AR-35 and the Live Log use case |
| `Quality/ConcurrencyTests.cs` | U2-NFR-02: 100 concurrent reservations, 50 rounds; Emergency Stop racing the end of a scan |
| `Quality/CorrelationOracleTests.cs` | U2-NFR-06: 1,000 seeded random packages checked against an independent copy of the decision tables |
| `Quality/FaultInjectionTests.cs` | U2-NFR-06, U2-NFR-11: the registry failing at every update, failing every update, failing to add; staging and catalog failures |
| `Quality/SlowSubscriberTests.cs` | U2-NFR-07 |
| `Quality/LogCatalogueTests.cs` | U2-NFR-09: messages, levels, event ids and the scan-id scope |
| `Quality/OptionsAndTextTests.cs` | Every option range at both ends; `ExternalText`; the 10,000-package smoke test; `AddAirlockApplication` |

### `tests/Airlock.Architecture.Tests`

| File | Rules |
|---|---|
| `ApplicationModel.cs` | Loads the Application assembly; shares `CalledMethods` with the Domain model |
| `ApplicationRules.cs` | Rule 2 (referenced assemblies; no Infrastructure, Web or ASP.NET Core namespace), rule 4's Application half (no Application type implements one of the 8 ports), rule 6 (use-case classes sealed and in place), the clock rule (12 banned calls, 3 banned types) — each with a positive control |

### Changed outside the new projects

`Directory.Packages.props` (six packages), `Airlock.slnx` (two projects), `ScanWarningCode.NotProductMatched` in the
Domain with its test row (Q-U2-3), the Architecture.Tests project reference and the shared `CalledMethods` helper.

## Worked examples as test rows (F4)

| Worked example (`business-logic-model.md`) | Test |
|---|---|
| The original's CPE test (requests 2.25.1, CVE-2021-28363) | `PackageCorrelatorTests.The_original_CPE_test_still_finds_its_CVE` |
| The original's keyword-fallback test (flask 0.12.2, CVE-2018-1000656) | `The_original_keyword_fallback_test_is_now_a_product_match` |
| The `mockData.js` false positives | `Description_only_mentions_never_become_findings` |
| pyyaml, flask 2.0.0, minimist, System.Text.Json, cloud.google.com/go/auth, syft UNKNOWN, busybox | One `PackageCorrelatorTests` fact each |
| A failed first candidate (Q-U2-1) | `A_failed_lookup_stops_the_package_as_unverified` |
| 87 packages, 2 vulnerable, 12 failing: 73 clean, Incomplete | `ScanResultTests.The_87_package_example_finishes_Incomplete_with_73_clean` |
| Failures in a row reset by an answer; the outage | `NvdOutageTests` (all three) |
| requirements.txt, an image and notes.docx | `SbomGenerationTests` (run grouping, the two warnings) |
| Duplicate names and `../../evil.txt`; 26 files; the byte limit | `ScanTargetTests` (the byte limit at 1 GiB and 3 MiB rather than 2 GiB) |
| Every row of the targets table | `ScanTargetTests` theories, Linux and Windows |
| Every row of the stopping table | `StoppingScanTests` |

The wheel and NuGet row belongs to U3, which stages them as `.zip` and reads the `.nuspec`.

## Deviations from the design and the plan

1. **`Outcome` factories live on a static `Outcome` class** — analyzer rule CA1000 bars static members on generic types.
2. **The reservation owns its ticket**: `StartScan` creates it and the coordinator only holds it (analyzer rule CA1001).
3. **Watchers are notified inside the coordinator's lock**, through one-item channels that never block, so every
   watcher sees the slot's changes in order; notifying after the lock could leave a watcher on an older state.
4. **A final status that cannot be recorded becomes Failed.** The fault-injection test found that a failed final
   update still published Completed from memory; the pipeline now records Failed instead (U2-NFR-06).
5. **Artifact names are sanitized in the Application** (`ArtifactName`), so AR-07 is tested in U2; U3's staging still
   keeps every file inside the scan's own folder.
6. **`NvdOutageBreaker` is public**, because it is a parameter of the public `PackageCorrelator.CorrelateAsync`.
7. The plan's five refinements are in the code and the documents: retention in the Application, the byte limit as a
   value, scan-scoped log lines routed through `ScanLogRouter`, the hub owning the latest version, and
   `ScanLifetimeService`. `component-methods.md`, `services.md` and the U2 design documents are updated (G2).

## Constraints handed to U3

- **Ports**: implement all eight. In particular:
  - The SBOM generator reports `IsContainerImage`, kills the Syft process tree on cancellation, and returns failures as
    `SbomFailed` with cleaned reasons.
  - The catalog keeps every lookup — retries included — within 2 minutes, answers no records for unknown and malformed
    CPEs, and applies `PackageVersion.ForNvd` to Go-module versions.
  - The staging area stops at `maxBytes` and keeps nothing of an oversized file, and deletes everything on dispose.
  - The registry updates each scan atomically.
- **Live Log provider**: read the `ScanId` scope entry and hand scan-scoped entries to `ScanLogRouter`. Keep at most
  1,000 lines per subscriber, and never put exception details into Live Log messages.
- **Registration**: `AddAirlockInfrastructure` registers the ports; the host (U4) binds the `Airlock` section and calls
  both registration methods.
