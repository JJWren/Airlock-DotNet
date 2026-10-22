# U1 Domain — Code Generation Summary

**Stage**: CONSTRUCTION — U1 Domain — Code Generation, Part 2 (Generation)
**Plan**: `construction/plans/u1-domain-code-generation-plan.md` (approved 2026-10-22T00:48:01Z)
**Completed**: 2026-10-22T01:17:45Z

## Result

| Check | Target | Result |
|---|---|---|
| Build, warnings as errors (E1) | 0 warnings | 0 warnings, 0 errors |
| Domain tests (E2) | all pass | 203 of 203 pass (94 test methods, theory rows counted separately) |
| Architecture tests (E2) | all pass | 15 of 15 pass (8 test methods) |
| Line coverage of `Airlock.Domain` (E3) | 95 % | 100 % of hand-written code (564 of 564 lines); 98.9 % with the generated `Cve` regex code (624 of 631) |
| Branch coverage of `Airlock.Domain` (E3) | 90 % | 100 % of hand-written code (250 of 250); 97.6 % with the generated regex code (279 of 286) |
| Worked examples are test rows (E4) | every example | every example (table below) |
| Architecture rules fail on violations | — | A temporary probe that broke every rule made all 7 matching tests fail; probe deleted |

Run them with:

```bash
dotnet build Airlock.slnx
```

```bash
dotnet test --coverage --coverage-output-format cobertura
```

The Cobertura files land in `TestResults/` at the repository root, which `.gitignore` excludes.

## Files

### Repository scaffold (A1–A6)

| File | Content |
|---|---|
| `global.json` | SDK 10.0.303, `rollForward: latestFeature`; `test.runner` `Microsoft.Testing.Platform` |
| `Directory.Build.props` | `net10.0`, nullable, implicit usings, warnings as errors, `AnalysisLevel` latest-recommended, `EnforceCodeStyleInBuild`, `Deterministic` |
| `Directory.Packages.props` | Central Package Management: `xunit.v3` 4.0.2, `Microsoft.Testing.Extensions.CodeCoverage` 18.12.0, `TngTech.ArchUnitNET.xUnitV3` 0.13.4 |
| `.editorconfig` | `dotnet new editorconfig`, file-scoped namespaces as a warning; under `tests/`, CA1707 (underscores in test names) and CA1515 (public test classes) off |
| `.gitignore`, `.gitattributes` | `dotnet new gitignore`; `* text=auto`, `*.cs diff=csharp`, `*.sh eol=lf` |
| `LICENSE`, `README.md` | MIT, "Copyright (c) 2026 Joshua Mykitta" (Q-CG-1); purpose, status and links |
| `Airlock.slnx` | `/src/` and `/tests/` solution folders |

### `src/Airlock.Domain` (B1–B6) — no package or project references

| Folder | Types |
|---|---|
| `Text/` | `DomainLimits` (public length limits), `DomainText` (internal — the single text guard) |
| `Targets/` | `Package`, `PackageVersion`, `Artifact`, `ArtifactKind`, `ManifestCatalog`, `ScanTarget`, `DirectoryTarget`, `ArtifactsTarget` |
| `Intelligence/` | `Severity`, `SeverityScale`, `SeverityThreshold`, `CvssVersion`, `CvssSource`, `CvssScore`, `Cve`, `CpeSource`, `CandidateCpe`, `ProductName` |
| `Results/` | `MatchMethod`, `CpeMatch`, `ProductMatch`, `Finding`, `FindingSelection`, `VulnerablePackage`, `UnverifiedReason`, `UnverifiedPackage`, `ScanSummary` |
| `Scanning/` | `ScanId`, `ScanStatus`, `ScanPhase`, `ScanProgress`, `ScanWarningCode`, `ScanWarning`, `ScanFailureCode`, `ScanFailure`, `Scan` |

### `tests/Airlock.Domain.Tests` (C1–C3)

| File | Covers |
|---|---|
| `A.cs` | Test-data builders, read as `A.Package(...)`, `A.Cve(...)`, `A.Finding(...)` |
| `Intelligence/CvssScoreSelectionTests.cs` | BR-01 to BR-03: preferred score, severity bands, labels, threshold |
| `Results/FindingSelectionTests.cs` | BR-04, BR-05, BR-07: threshold, unscored CVEs, duplicates, order |
| `Results/VulnerablePackageTests.cs` | BR-06, BR-07: Risk is the highest severity |
| `Intelligence/CandidateCpeRankingTests.cs` | BR-08: dictionary first, name tiers, Syft's order, cap of three |
| `Intelligence/ProductNameTests.cs` | BR-09 |
| `Targets/PackageVersionTests.cs` | BR-10 |
| `Targets/PackageIdentityTests.cs` | BR-11 |
| `Targets/ManifestCatalogTests.cs` | BR-12, and plain file names for artifacts |
| `Results/ScanSummaryTests.cs` | BR-13 |
| `Scanning/ScanStateMachineTests.cs` | BR-14 to BR-16: every allowed and disallowed transition |
| `Intelligence/CveTests.cs` | BR-17 and CVE validation |
| `Results/UnverifiedPackageTests.cs` | BR-18 |
| `SmallValueTests.cs` | `ScanId`, `ScanProgress`, `ScanWarning`, `ScanFailure`, scan targets, match methods |
| `Text/DomainTextTests.cs` | U1-NFR-04: every limit at its boundary; control and bidirectional characters |
| `ImmutabilityTests.cs` | U1-NFR-02: no public setters (`init` included) and no public fields on any exported type |
| `CultureIndependenceTests.cs` | U1-NFR-03: normalization, identity and labels under `tr-TR` |

### `tests/Airlock.Architecture.Tests` (D1–D3)

| File | Rules |
|---|---|
| `DomainModel.cs` | Loads the Domain assembly once; selects the types in `Airlock.Domain.*`; finds called methods |
| `DomainDependencyRules.cs` | Architecture rule 1 (U1-NFR-01): only `System.*` assemblies referenced; no dependency on `Microsoft.*` or another `Airlock.*` namespace |
| `DomainDeterminismRules.cs` | U1-NFR-03: no clock read, no `Environment`, `Process` or `Console`, nothing from `System.IO` or `System.Net` |

Each kind of rule has a positive control: a test that a known base class library call or type *is* seen, so a rule
cannot pass by checking nothing.

## Worked examples as test rows (E4)

| Worked example (`business-logic-model.md`) | Test |
|---|---|
| CVE-2023-29530: NVD's v3.1 6.5 MEDIUM over the CNA's 7.5 HIGH; no finding | `CvssScoreSelectionTests.Cve_2023_29530_uses_NVDs_own_medium_score_over_the_CNAs_high_score`; `FindingSelectionTests.The_S0_CVEs_are_findings_only_when_their_preferred_score_is_HIGH_or_above` |
| CVE-2018-1000656: v3.0 7.5 HIGH; a finding | `Cve_2018_1000656_falls_back_to_v30`; the S0 findings test |
| CVE-2026-27205: v3.1 4.3 MEDIUM, not v4.0; no finding | `Cve_2026_27205_prefers_v31_over_v40`; the S0 findings test |
| CVE-2020-1747: NVD's v3.1 9.8 CRITICAL; a finding | `Cve_2020_1747_takes_NVDs_v31_score`; the S0 findings test |
| CVE-2025-60876: no score; listed as unscored | `A_CVE_without_any_score_is_listed_not_counted`; the S0 findings test |
| Illustrative: two CNA scores, 7.5 and 9.1, give 9.1 | `Several_CNA_scores_without_an_NVD_score_use_the_highest`; the S0 findings test |
| pyyaml 5.3: the correct CPE moves from 12th to 1st | `Pyyamls_real_CPE_moves_from_last_to_first` (all 12 guesses in Syft v1.55.0's order, re-checked against Syft on 2026-10-22) |
| log4j-core 2.14.1 keeps all three | `Log4j_core_keeps_all_three_including_NVDs_apache_log4j` |
| requests 2.25.1: the dictionary CPE first | `Dictionary_candidates_come_first` |
| flask 0.12.2: three name-matching guesses | `Flask_tries_the_three_name_matching_guesses` (Syft's order, re-checked) |
| Product names: the eight rows | `ProductNameTests` (six specific names, six generic, short or spaced) |
| Versions: the four rows | `PackageVersionTests`, `UnverifiedPackageTests.An_unknown_version_reads_as_version_unknown` |
| Identity: the three rows | `PackageIdentityTests` |
| 87 packages, 2 vulnerable, 12 unverified: 73 clean, 3 CRITICAL, 1 HIGH, Incomplete | `ScanSummaryTests.The_87_package_example_leaves_73_clean_and_finishes_Incomplete` |
| HIGH then CRITICAL gives Risk CRITICAL | `VulnerablePackageTests.Risk_is_the_highest_severity_whatever_order_the_findings_arrive_in` |

## Deviations from the design and the plan

1. **`CvssVersion` members are `V20`, `V30`, `V31`, `V40`**, not `V2_0` etc. — analyzer rule CA1707 forbids
   underscores. `domain-entities.md` and `component-methods.md` are updated (F2).
2. **`DateTime.Today` is banned too.** The NFR design listed four clock reads; `DateTime.Today` is a fifth.
   `nfr-design-patterns.md` is updated (F2).
3. **The banned characters in `DomainText` are named constants** (`LeftToRightMark`, `FirstIsolate`, …), so the
   source holds no invisible bidirectional characters itself.
4. **Builders are `A.Package(...)` rather than a `using static` helper class.** A static import would make the
   builder methods `Package`, `Cve` and `Finding` ambiguous with the Domain types of the same names.
5. **`SmallValueTests` was added** for the small value types the rule tests do not exercise directly.
6. **Two ArchUnitNET 0.13.4 behaviours, found by the positive controls**, shaped the architecture tests:
   - A call into another assembly is recorded only as a dependency of the calling type, not as a member of the
     referenced type, so `DomainModel.CalledMethods` reads calls from the dependencies.
   - `NotDependOnAny(typeof(...))` throws for a type the Domain never references, so the type rules select types
     by full name, which simply finds nothing when the type is absent.
7. **`Airlock.Architecture.Tests` also references the coverage extension.** Without it, `dotnet test --coverage`
   from the repository root fails on that project with exit code 5 (unknown option).

## Notes for later units

- `ScanId.New()` uses `Guid.CreateVersion7()`, which embeds the current time, as `tech-stack-decisions.md` decided.
  It is the Domain's only use of the clock. Code that needs reproducible identifiers can build `new ScanId(guid)`.
- The architecture rules select `Airlock.Domain.*` namespaces, because the compiler emits its own attribute types
  (for example `Microsoft.CodeAnalysis.EmbeddedAttribute`) into the assembly. Rules for later layers should do the same.
- With `--coverage`, the Architecture.Tests report shows the Domain at 0 %, because those tests read metadata and run
  no Domain code. The Domain figures above come from the Domain.Tests report.
