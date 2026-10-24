# U3 Infrastructure — Code Generation Plan

**Stage**: CONSTRUCTION — U3 Infrastructure — Code Generation, Part 1 (Planning) · **Depth**: Comprehensive
**Started**: 2026-10-24T00:50:42Z · **Part 2 completed**: 2026-10-24T02:07:29Z (`u3-infrastructure/code/code-generation-summary.md`)

## Inputs

- `construction/u3-infrastructure/functional-design/` (IR-01 to IR-26, mapping tables, worked examples, rule-to-test map)
- `construction/u3-infrastructure/nfr-requirements/` and `construction/u3-infrastructure/nfr-design/`
- `inception/application-design/component-methods.md` (Infrastructure section) and `services.md`
- The U2 code on `feature/u2-application`: the ports, the options, `ScanLogRouter`, `ScanLogScope`, `ExternalText`
- The recorded S0 outputs in the scratchpad (`s0-syft-out`, `s0-nvd`) and its copy of Syft v1.55.0 (`s0-syft-bin`)

## Ground rules

- Code lives at the repository root (`src/`, `tests/`, `scripts/`); `aidlc-docs/construction/u3-infrastructure/code/`
  gets a summary only.
- Nothing is committed during generation. At the completion checkpoint I propose two commits — the U3 design
  documents with the earlier documents they amended, then the U3 code — on the branch chosen in Q-CG3-1, and make them
  only if you say so. Nothing is pushed.
- Package versions, checked on nuget.org on 2026-10-24:
  - `Microsoft.Extensions.Http`, `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Configuration`,
    `Microsoft.Extensions.Configuration.Binder` and `Microsoft.Extensions.Options.ConfigurationExtensions` 10.0.13, as
    in U2;
  - `Polly.Core` and `Polly.RateLimiting` 8.8.0; `QuestPDF` 2026.9.1; `PdfPig` 0.1.16 (tests only).
  - With their dependencies, restoring downloads up to 7 packages, about 65 MB, from nuget.org: QuestPDF 48.6 MB (it
    carries native rendering libraries for every platform), PdfPig 16.3 MB, Polly.Core 0.8 MB, and four under 0.4 MB
    each (Microsoft.Extensions.Http, Microsoft.Extensions.Diagnostics, Polly.RateLimiting and
    System.Threading.RateLimiting, which may be skipped because .NET 10 ships it). Everything else is in the local
    cache.
- Syft v1.55.0 is still the latest release (published 2026-10-19), so the pin stands. The integration tests use the
  scratchpad's copy through `SYFT_BINARY_PATH`; nothing new is downloaded.
- Every file must build with warnings as errors. Analyzer findings are fixed, not suppressed; the only exceptions are
  the test-project ones U1 set (CA1707, CA1515). A name an analyzer forces to change is updated in the design documents
  in step I2.
- Fixtures: the recorded Syft outputs with local paths replaced and Syft's configuration block removed; the recorded
  NVD answers renamed after what they answer, their headers trimmed to the status line, `Content-Type`, `Retry-After`
  and `message`. Before the commit, every fixture is checked for keys, cookies and personal paths.

## Design refinements made while planning

Each keeps the approved behaviour and is recorded in the design documents in step I2.

1. **PdfPig's package id is `PdfPig`**; `UglyToad.PdfPig` is its namespace. The NFR design documents are corrected.
2. **`Microsoft.Extensions.Configuration` is referenced directly** for the `ConfigurationProvider` base class behind
   `AddAirlockEnvironmentVariables`. It is already in the local cache.
3. **An Infrastructure clock rule** joins rules 3 and 4: no clock reads, stopwatches, timers or untimed delays in
   Infrastructure code, as for the Domain and the Application, since every wait there runs on the `TimeProvider`.
4. **The integration tests build their own inputs** — a `requirements.txt`, a wheel, a NuGet package and a minimal
   image archive — and scan Syft's own binary (S10), so no sample binary is committed.
5. **The live NVD check is opt-in.** One `Category=Network` test asks NVD, without a key, for pyyaml 5.3's CVEs. It
   runs only when `AIRLOCK_LIVE_NVD=1` is set, and once in H6.

## Steps

### A. Branch and packages
- [x] A1 Branch as chosen in Q-CG3-1; the uncommitted U3 design documents come along
- [x] A2 `Directory.Packages.props` — an Infrastructure group with the packages above (`Hosting.Abstractions` is
      already there) and `PdfPig` under Tests

### B. Domain and Application amendment (U3 decision 1)
- [x] B1 `CandidateCpe.ForNvd(Package)` — the version part rewritten as `PackageVersion.ForNvd` gives it, when it is
      the package's reported version; rows in `CandidateCpeTests`
- [x] B2 `PackageCorrelator` looks up `cpe.ForNvd(package)` and records that CPE in the match (IR-10); a Go-module row
      in `PackageCorrelatorTests`

### C. `src/Airlock.Infrastructure`
- [x] C1 `Airlock.Infrastructure.csproj` — `Airlock.Application`, `Airlock.Domain` and the packages;
      `InternalsVisibleTo` `Airlock.Infrastructure.Tests`; added to `Airlock.slnx`
- [x] C2 `Options/` — `NvdOptions`, `SyftOptions`, `StagingOptions` and their validators (every range of the values
      table, in the Application's message form)
- [x] C3 `AirlockConfigurationBuilderExtensions` and `AirlockEnvironmentVariables` — `NVD_API_KEY`,
      `SYFT_BINARY_PATH`, and `LOG_LEVEL` with the original's names (IR-23)
- [x] C4 `FileSystem/HostFileSystem` (IR-19)
- [x] C5 `Scans/InMemoryScanRegistry` (IR-14)
- [x] C6 `Staging/` — `StagingFolders` (marker, owner-only modes, deletion with retries), `DiskArtifactStaging`,
      `DiskStagingArea`, `StagingSweepService`, `StagingLog` (IR-11 to IR-13)
- [x] C7 `Logging/` — `LiveLogBroadcaster`, and `LiveLogLoggerProvider` with its filter rules and re-entrancy guard
      (IR-15 to IR-18)
- [x] C8 `Syft/` — `SyftLocator`, `SyftProcessRunner` (`SyftRunRequest`, `SyftRunResult<T>`), `SyftJsonContext` and
      `SyftJsonParser`, `NuspecReader`, `SyftStatusProbe`, `SyftSbomGenerator`, `SyftLog` (IR-01 to IR-06)
- [x] C9 `Nvd/` — `NvdJsonContext`, `NvdQuery` and `CpeMatchString`, `NvdAttempt` and `NvdPageOutcome`,
      `NvdRequestPacer`, `NvdPipeline`, `NvdApiClient`, `NvdResponseMapper`, `NvdVulnerabilityCatalog`, `NvdLog`
      (IR-07 to IR-10)
- [x] C10 `Reports/` — `ScanReportDocument`, `QuestPdfScanReportRenderer`, `ReportLog` (IR-20, IR-21)
- [x] C11 `InfrastructureServiceCollectionExtensions.AddAirlockInfrastructure` — the registrations of
      `logical-components.md`, the named client `Airlock.Nvd` and the QuestPDF licence

### D. Install scripts (IR-25)
- [x] D1 `scripts/install-tools.ps1` — Windows PowerShell 5.1 and PowerShell 7
- [x] D2 `scripts/install-tools.sh` — POSIX `sh`

### E. `tests/Airlock.Infrastructure.StandIn`
- [x] E1 A console program that plays Syft: it writes a file to standard output, fills standard error, exits with a
      given code, hangs, starts a child of itself and prints its id, or prints a `syft version` text; added to
      `Airlock.slnx`

### F. `tests/Airlock.Infrastructure.Tests`
- [x] F1 Project file — `xunit.v3`, the coverage extension, `Microsoft.Extensions.TimeProvider.Testing`,
      `Microsoft.Extensions.Diagnostics.Testing` and `PdfPig`; references `Airlock.Infrastructure` and the stand-in;
      added to `Airlock.slnx`
- [x] F2 `Fixtures/` — the recorded Syft outputs and NVD answers, cleaned as above
- [x] F3 Helpers — `FakeNvdHandler`, the stand-in's location, temporary folders, a log sink
- [x] F4 Rule tests (the rule-to-test map): `SyftProcessRunnerTests`, `SyftJsonParserTests`, `NuspecReaderTests`,
      `SyftToolStatusTests`, `SyftSbomGeneratorTests`, `NvdApiClientTests`, `NvdRequestPacerTests`,
      `NvdResponseMapperTests`, `NvdVulnerabilityCatalogTests`, `DiskArtifactStagingTests`,
      `InMemoryScanRegistryTests`, `LiveLogTests`, `HostFileSystemTests`, `ScanReportRendererTests`,
      `InfrastructureOptionsTests`
- [x] F5 NFR tests (the checks of `logical-components.md`): `NvdResilienceTests`, `SecretHygieneTests`,
      `StagingFoldersTests`, `CompositionTests`, `PerformanceSmokeTests`
- [x] F6 `SyftIntegrationTests` (`Category=Integration`) and `NvdLiveTests` (`Category=Network`, opt-in)

### G. `tests/Airlock.Architecture.Tests`
- [x] G1 Reference `Airlock.Infrastructure`
- [x] G2 `InfrastructureDependencyRules` (rule 3), the Infrastructure half of `PortRules` (rule 4) and
      `InfrastructureDeterminismRules` (the clock rule), each with a positive control

### H. Verify
- [x] H1 `dotnet build` — zero warnings
- [x] H2 `dotnet test` with `SYFT_BINARY_PATH` set to the scratchpad's Syft — every test passes, the integration tests
      included; record the counts
- [x] H3 Coverage of `Airlock.Infrastructure` against 85 % of lines and 80 % of branches (U3-NFR-13); U1 and U2 still at
      theirs
- [x] H4 Confirm the worked examples of the business logic model are test rows
- [x] H5 A temporary probe shows each new architecture rule fails on a violation; then it is deleted
- [x] H6 One live NVD lookup without a key (`NvdLiveTests`): pyyaml 5.3 answers CVE-2020-1747 and CVE-2020-14343
- [x] H7 The install scripts: `install-tools.ps1`, with the scratchpad's Syft copied into `bin/` (ignored by git), sees
      v1.55.0 and downloads nothing; `install-tools.sh` passes `sh -n`. Their download path is checked in Build and Test

### I. Documentation
- [x] I1 `aidlc-docs/construction/u3-infrastructure/code/code-generation-summary.md` — files, test counts, coverage,
      deviations and the constraints handed to U4 and U6
- [x] I2 Update the design documents for refinements 1 to 5 and any name changed during implementation (the
      rule-to-test map, `component-methods.md`)
- [x] I3 Tick this plan; update `aidlc-state.md` and `audit.md`

## Questions

### Q-CG3-1 — Branch for U3
U2 is approved and committed on `feature/u2-application`; `main` holds U1. U3 builds on the U2 code and amends it (B2).

A) Fast-forward `main` to `feature/u2-application`, then branch `feature/u3-infrastructure` from `main`, as for U2
B) Branch `feature/u3-infrastructure` from `feature/u2-application`; `main` stays at U1
C) Keep working on `feature/u2-application`
D) Other

[Answer]: A

### Plan approval
A) Request Changes
B) Approve and generate (restores up to 7 packages, about 65 MB, from nuget.org)

[Answer]: B (approved 2026-10-24T01:01:40Z)
