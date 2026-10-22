# U1 Domain — Code Generation Plan

**Stage**: CONSTRUCTION — U1 Domain — Code Generation, Part 1 (Planning) · **Depth**: Standard
**Started**: 2026-10-22T00:39:25Z · **Part 2 completed**: 2026-10-22T01:17:45Z (`u1-domain/code/code-generation-summary.md`)

## Inputs

- `construction/u1-domain/functional-design/` (entities, BR-01 to BR-18, worked examples, rule-to-test map)
- `construction/u1-domain/nfr-requirements/` and `construction/u1-domain/nfr-design/`
- `inception/application-design/component-methods.md` (Domain section)

## Ground rules

- Code lives at the repository root (`src/`, `tests/`); `aidlc-docs/construction/u1-domain/code/` gets a summary only.
- Nothing is committed during generation. At the completion checkpoint I propose the commits — the inception and
  U1 documents as the first commit on `main`, then `feature/u1-domain` with the U1 code — and make them only if you
  say so.
- Package versions, checked on nuget.org on 2026-10-22: `xunit.v3` 4.0.2 (Microsoft Testing Platform v2),
  `Microsoft.Testing.Extensions.CodeCoverage` 18.12.0, `TngTech.ArchUnitNET.xUnitV3` 0.13.4. Not used:
  `xunit.runner.visualstudio` and `Microsoft.NET.Test.Sdk` — Visual Studio 18.6 and `dotnet test` on .NET 10 run
  the Microsoft Testing Platform natively.
- Every file must build with warnings as errors. Analyzer findings are fixed, not suppressed; the one exception is
  CA1707 (underscores) for test method names. If an analyzer forces a rename of a designed name, the design documents
  are updated in step F2.

## Steps

### A. Scaffold
- [x] A1 `global.json` — SDK 10.0.303, `rollForward: latestFeature`, test runner `Microsoft.Testing.Platform`
- [x] A2 `Directory.Build.props` — `net10.0`, nullable, implicit usings, warnings as errors, `AnalysisLevel`
      latest-recommended, `EnforceCodeStyleInBuild`, `Deterministic`
- [x] A3 `Directory.Packages.props` — Central Package Management with the versions above
- [x] A4 `.editorconfig` (from `dotnet new editorconfig`, plus file-scoped namespaces and CA1707 off under `tests/`),
      `.gitignore` (from `dotnet new gitignore`), `.gitattributes` (`* text=auto`, `*.sh text eol=lf`)
- [x] A5 `LICENSE` (MIT; copyright line per Q-CG-1) and a minimal `README.md` (purpose, status, links to the
      glossary, ADRs and AIDLC documents)
- [x] A6 `Airlock.slnx` with `src` and `tests` solution folders

### B. `src/Airlock.Domain`
- [x] B1 `Airlock.Domain.csproj` — no references; `InternalsVisibleTo` `Airlock.Domain.Tests`
- [x] B2 `Text/` — `DomainLimits`, `DomainText`
- [x] B3 `Targets/` — `Package`, `PackageVersion`, `Artifact` + `ArtifactKind`, `ManifestCatalog`, `ScanTarget` +
      `DirectoryTarget` + `ArtifactsTarget`
- [x] B4 `Intelligence/` — `Severity`, `SeverityScale`, `SeverityThreshold`, `CvssVersion`, `CvssSource`, `CvssScore`,
      `Cve`, `CpeSource`, `CandidateCpe`, `ProductName`
- [x] B5 `Results/` — `MatchMethod` + `CpeMatch` + `ProductMatch`, `Finding`, `FindingSelection`, `VulnerablePackage`,
      `UnverifiedReason`, `UnverifiedPackage`, `ScanSummary`
- [x] B6 `Scanning/` — `ScanId`, `ScanStatus`, `ScanPhase`, `ScanProgress`, `ScanWarningCode`, `ScanWarning`,
      `ScanFailureCode`, `ScanFailure`, `Scan`

### C. `tests/Airlock.Domain.Tests`
- [x] C1 Project file — `xunit.v3`, coverage extension; references `Airlock.Domain`
- [x] C2 Rule tests (rule-to-test map): `CvssScoreSelectionTests`, `FindingSelectionTests`, `VulnerablePackageTests`,
      `CandidateCpeRankingTests`, `ProductNameTests`, `PackageVersionTests`, `PackageIdentityTests`,
      `ManifestCatalogTests`, `ScanSummaryTests`, `ScanStateMachineTests`, `CveTests`, `UnverifiedPackageTests`;
      added: `SmallValueTests` and the `A` builders (summary, deviations 4 and 5)
- [x] C3 NFR tests: `DomainTextTests` (limits, control and bidirectional characters), `ImmutabilityTests` (no
      public setters, `init` included), `CultureIndependenceTests` (`tr-TR`)

### D. `tests/Airlock.Architecture.Tests`
- [x] D1 Project file — `xunit.v3`, `TngTech.ArchUnitNET.xUnitV3`; references `Airlock.Domain`
- [x] D2 `DomainDependencyRules` — architecture rule 1
- [x] D3 `DomainDeterminismRules` — no clock, environment, file, network, process or console use

### E. Verify
- [x] E1 `dotnet restore` and `dotnet build` — zero warnings
- [x] E2 `dotnet test` — every test passes; record the counts
- [x] E3 Coverage — line and branch coverage of `Airlock.Domain` against 95 % / 90 % (U1-NFR-06)
- [x] E4 Confirm the worked examples (pyyaml ranking, CVE-2023-29530 selection, the 87 / 2 / 12 summary) are test rows

### F. Documentation
- [x] F1 `aidlc-docs/construction/u1-domain/code/code-generation-summary.md` — files, test counts, coverage,
      deviations
- [x] F2 Update `component-methods.md` and the U1 design documents for any name changed during implementation
- [x] F3 Tick this plan; update `aidlc-state.md` and `audit.md`

## Questions

### Q-CG-1 — Copyright line in `LICENSE`
The original reads "Copyright (c) 2026 Joshua Mykitta, Francisco Encinas".

A) Keep the original line unchanged
B) "Copyright (c) 2026 Joshua Mykitta"
C) Other

[Answer]: B

### Plan approval
A) Request Changes
B) Approve and generate

[Answer]: B (approved 2026-10-22T00:48:01Z)
