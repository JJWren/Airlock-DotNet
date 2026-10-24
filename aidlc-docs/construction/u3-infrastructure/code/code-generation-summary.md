# U3 Infrastructure — Code Generation Summary

**Stage**: CONSTRUCTION — U3 Infrastructure — Code Generation, Part 2 (Generation)
**Plan**: `construction/plans/u3-infrastructure-code-generation-plan.md` (approved 2026-10-24T01:01:40Z)
**Completed**: 2026-10-24T02:07:29Z · **Branch**: `feature/u3-infrastructure`, from `main` fast-forwarded to the U2 commit

## Result

| Check | Target | Result |
|---|---|---|
| Build, warnings as errors (H1) | 0 warnings | 0 warnings, 0 errors |
| All tests, with Syft v1.55.0 through `SYFT_BINARY_PATH` (H2) | all pass | 730 of 733; 3 skipped (below) |
| Infrastructure tests | all pass | 268 of 271, the 6 real-Syft integration tests included |
| Domain tests | all pass | 211 of 211: U1's 203 and 8 new for `CandidateCpe.ForNvd` |
| Application tests | all pass | 191 of 191: U2's 190, 1 changed and 1 new for the Go-module CPE |
| Architecture tests | all pass | 60 of 60: U2's 37 and 23 new |
| Line coverage of `Airlock.Infrastructure` (H3) | 85 % | 95.7 % (1,497 of 1,565 lines) |
| Branch coverage of `Airlock.Infrastructure` (H3) | 80 % | 87.2 % (492 of 564 branches) |
| U1 and U2 still at theirs (H3) | Domain 100 %, Application 90 % and 85 % | Domain 100 % and 100 %; Application 97.2 % and 95.2 % |
| Worked examples are test rows (H4) | every example | every example the Infrastructure owns (table below) |
| New architecture rules fail on violations (H5) | — | A temporary probe — a second port implementation, an `Airlock.Web` dependency, an ASP.NET Core reference, `DateTime.UtcNow` and `Stopwatch` — broke the 6 targeted rules and only those; it was deleted |
| One live NVD lookup without a key (H6) | pyyaml 5.3's two CVEs | CVE-2020-1747 and CVE-2020-14343, in 1.7 seconds |
| Install scripts (H7) | no download when `bin/` holds v1.55.0 | Windows PowerShell 5.1: "Syft 1.55.0 is already in …", exit 0; `install-tools.sh` passes `sh -n`; PowerShell 7 is not installed here; both download paths are for Build and Test |

Coverage excludes the source-generated files (the JSON contexts and the logging methods), as for U1 and U2. The
uncovered lines are defensive: catch blocks for a killed process whose pipes break, a staging folder Windows refuses
to enumerate, and the branches for the other operating system.

The 3 skipped tests: the live NVD test, which runs only with `AIRLOCK_LIVE_NVD=1`; the owner-only permissions check,
which runs only on Linux and macOS; and the relative symbolic link, which needs the right to create symbolic links on
Windows. The other link tests fall back to directory junctions there.

Run them with:

```bash
dotnet build Airlock.slnx
```

```bash
dotnet test --coverage --coverage-output-format cobertura
```

The integration tests need Syft: set `SYFT_BINARY_PATH`, or run `scripts/install-tools.ps1` (or `.sh`) and point
`SYFT_BINARY_PATH` at the repository's `bin/`. The live NVD test also needs `AIRLOCK_LIVE_NVD=1`.

## Files

### `src/Airlock.Infrastructure` — 37 files

| Folder | Types |
|---|---|
| root | `InfrastructureServiceCollectionExtensions` (`AddAirlockInfrastructure`), `AirlockConfigurationBuilderExtensions` (`AddAirlockEnvironmentVariables`); internal `AirlockEnvironmentVariables` |
| `Options/` | `NvdOptions`, `SyftOptions`, `StagingOptions`; internal validators and `OptionChecks` |
| `Syft/` | `SyftSbomGenerator`; internal `SyftLocator`, `SyftProcessRunner` (`SyftRunRequest`, `SyftRunResult<T>`, `SyftRunEnd`), `SyftEnvironment`, `SyftStatusProbe`, `SyftJsonParser` (`SyftParse`), the `SyftJsonContext` records, `NuspecReader` (`NuspecRead`), `SyftLog` |
| `Nvd/` | `NvdVulnerabilityCatalog`; internal `NvdApiClient`, `NvdQuery`, `CpeMatchString`, `NvdAttempt` and its four records, `NvdRequestPacer`, `NvdPipeline`, `NvdResponseMapper`, the `NvdJsonContext` records, `NvdLog` |
| `Staging/` | `DiskArtifactStaging`; internal `DiskStagingArea`, `StagingFolders`, `StagingSweepService`, `StagingLog` |
| `Scans/` | `InMemoryScanRegistry` |
| `Logging/` | `LiveLogBroadcaster`, `LiveLogLoggerProvider` |
| `Reports/` | `QuestPdfScanReportRenderer`; internal `ScanReportDocument`, `QuestPdfSettings`, `ReportLog` |
| `FileSystem/` | `HostFileSystem` |

### `tests/Airlock.Infrastructure.Tests` — 29 code files and 33 fixtures

| File | Covers |
|---|---|
| `Fixtures/Syft/` | The 11 recorded Syft outputs (S1 to S12), local paths replaced and Syft's configuration block removed |
| `Fixtures/Nvd/` | 11 recorded NVD answers, renamed after what they answer, with their headers trimmed to the status line, `Content-Type`, `Retry-After` and `message` |
| `Support/` | `FakeNvdHandler` and `TestHttpClientFactory`, `StandIn`, `ClockDriver` (moves a fake clock until work is done), `TemporaryFolder`, `Fixtures`, `A` (scans, packages, ZIP archives), `Test` (the token and real-time bounds) |
| `Syft/SyftProcessRunnerTests.cs` | IR-01, IR-02, U3-NFR-04: arguments as a list, the environment, the 4 KB error tail, a full pipe, the tree killed on cancellation and at the limit (defect 3) |
| `Syft/SyftJsonParserTests.cs` | IR-03: every recorded output; candidate order and sources; images; unreadable and unrepresentable output |
| `Syft/NuspecReaderTests.cs` | IR-04, U3-NFR-05, U3-NFR-07: the nuspec, its absence, broken XML, a DTD, a 1 MB nuspec, an archive announcing too many entries |
| `Syft/SyftToolStatusTests.cs` | IR-05, IR-06: found, not found, does not run, does not answer; the cache; the search order |
| `Syft/SyftSbomGeneratorTests.cs` | IR-01 to IR-04 through the stand-in: arguments, every failure, the time limit, cancellation, the temporary folder, NuGet packages |
| `Syft/SyftIntegrationTests.cs` | The real Syft (`Category=Integration`): a requirements file, a wheel, a NuGet package, an image archive the test builds, Syft's own binary, a cancelled run |
| `Nvd/NvdApiClientTests.cs` | IR-07, IR-08: the request, the key header, product matches, 404, 503, 429 with `Retry-After: 0`, broken bodies, other statuses, network errors; CPE escaping |
| `Nvd/NvdRequestPacerTests.cs` | U3-NFR-03: 16 callers, arrival order, a cancelled waiter, the lease contract (defect 9) |
| `Nvd/NvdResponseMapperTests.cs` | IR-09: the metric table, sources, v3.0 and v4.0, SSVC ignored, products, rejected records, derived severities, descriptions (defect 11) |
| `Nvd/NvdVulnerabilityCatalogTests.cs` | IR-07 to IR-10 through the port: the S0 examples, pages, the 2-minute deadline, cancellation, unreadable records |
| `Nvd/NvdResilienceTests.cs` | U3-NFR-02: the delay table, `Retry-After: 7`, stalled bodies, 16 lookups keeping the pace with their retries |
| `Nvd/NvdLiveTests.cs` | The real NVD, opt-in (`Category=Network`) |
| `Staging/DiskArtifactStagingTests.cs` | IR-11 to IR-13: the layout, the byte limit, the `.zip` suffix, escaping names (defect 5), cancellation, disposal, locked files, permissions |
| `Staging/StagingFoldersTests.cs` | U3-NFR-06: read-only entries, the startup sweep and what it leaves alone |
| `Scans/InMemoryScanRegistryTests.cs` | IR-14 |
| `Logging/LiveLogTests.cs` | IR-15 to IR-18: categories, levels, messages only, the scan id, Debug under a Warning console, the replay, two subscribers (defect 4), a subscriber that never reads, the re-entrancy guard |
| `FileSystem/HostFileSystemTests.cs` | IR-19 on real folders, links and junctions |
| `Reports/ScanReportRendererTests.cs` | IR-20, IR-21, read back with PdfPig: every section for Incomplete, Completed, Failed and Cancelled scans; links; unusual characters |
| `Quality/InfrastructureOptionsTests.cs` | IR-22, IR-23: every range at both ends; the original variable names and `LOG_LEVEL` |
| `Quality/SecretHygieneTests.cs` | U3-NFR-07: Trace logging in every category, failure reasons, health details and Syft's environment hold no key |
| `Quality/CompositionTests.cs` | U3-NFR-11: every port resolves, invalid options stop startup naming them, a degraded start, the Live Log's filters, and one scan through the composed Application and Infrastructure (IR-16) |
| `Quality/PerformanceSmokeTests.cs` | U3-NFR-10 at ten times the targets; U3-NFR-05's flat memory, run on its own |

### `tests/Airlock.Infrastructure.StandIn` — 1 file

A console program that plays Syft: `scan` writes the output a test scripts, `version`, `hang`, `tree` (a child of its
own), `args`, `env` and `flood`. The test project references it, so it sits next to the tests.

### `scripts/`

`install-tools.ps1` (Windows PowerShell 5.1 and PowerShell 7) and `install-tools.sh` (POSIX `sh`) — IR-25.

### `tests/Airlock.Architecture.Tests`

| File | Rules |
|---|---|
| `InfrastructureModel.cs` | Loads the Infrastructure assembly |
| `InfrastructureRules.cs` | Rule 3 (no Web or ASP.NET Core assembly or namespace), rule 4's Infrastructure half (each of the 8 ports has exactly one implementation, in its adapter's namespace), the clock rule (14 banned calls, 3 banned types) — each with a positive control |

### Changed outside the new projects

- `CandidateCpe.ForNvd` in the Domain, with `CandidateCpeForNvdTests` (U3 decision 1).
- `PackageCorrelator` asks for `cpe.ForNvd(package)`, with one new and one changed test row.
- `Directory.Packages.props` (nine packages), `Airlock.slnx` (three projects), and the Architecture.Tests project
  reference.

## Worked examples as test rows (H4)

| Worked example (`business-logic-model.md`) | Test |
|---|---|
| `requirements.txt` (S1): 4 python packages | `SyftJsonParserTests.A_requirements_file_gives_its_four_python_packages`; `SyftIntegrationTests` with the real Syft |
| `package-lock.json` (S2): 3 npm packages, its own project included | `A_lock_file_lists_its_own_project_with_its_dependencies` |
| `docker save alpine:3.20` (S9): an image, 14 apk packages, busybox 1.36.1-r31 | `An_image_archive_is_marked_and_keeps_distribution_revisions`; `An_image_archive_is_recognized_as_a_container_image` with the real Syft |
| Syft's own Go binary (S10): 281 packages, `syft` UNKNOWN, 10 without candidates | `Syfts_own_binary_keeps_versions_and_candidates_as_reported`; `Syfts_own_binary_gives_its_Go_modules_with_their_leading_v` |
| A .NET assembly (S11): System.Text.Json 10.0.11, no candidates | `A_dotnet_assembly_has_no_candidate_CPEs` |
| A wheel stored as `.whl.zip` (S5): pyyaml 5.3 | The `whl-as-zip` row of `Every_recorded_output_maps_to_its_packages`; `A_wheel_staged_as_zip_gives_its_package` |
| A NuGet package stored as `.nupkg.zip` (S6): the assemblies plus the `nuget` package | `A_NuGet_package_adds_the_package_its_nuspec_names_alongside_the_assemblies`; `A_NuGet_package_gives_the_package_its_nuspec_names` |
| pyyaml's candidates (S12) | `Candidate_CPEs_keep_Syfts_order_and_their_source` |
| An unknown CPE (N1), a malformed CPE (N2), pyyaml 5.3 (N3) | `NvdVulnerabilityCatalogTests`: one fact each |
| CVE-2023-29530 (N5), CVE-2026-27205 (N6), `ssvcV203` (N7) | `NvdResponseMapperTests`: one fact each |
| Throttling without a key (N9) | `A_429_with_Retry_After_0_is_retried_after_the_own_minimum_backoff` |
| A product match for minimist 1.2.5 (N12) | `A_product_match_finds_minimists_CVE_from_any_vendor` |
| A Go-module CPE `…:v0.23.3:…` (decision 1) | `CandidateCpeForNvdTests`; `A_Go_module_is_asked_for_and_matched_without_its_leading_v`; `A_Go_modules_product_match_drops_its_leading_v` |
| The process and HTTP outcomes table | `SyftSbomGeneratorTests` (exit 1, the time limit) and `NvdApiClientTests` (200 with 0 results, 404, 429 then 200, 503 throughout) |
| The mapping tables, ESC and an over-long version included | `SyftJsonParserTests` and `NvdResponseMapperTests` |
| Defects 3, 4, 5, 9 and 11 | The process tree, the two subscribers, the escaping names, the pace and the metric table, as listed above |

## Deviations from the design and the plan

1. **The HTTP client factory's own logging is off for the NVD client** (`RemoveAllLoggers`). The secret-hygiene test
   found that its formatted messages redact headers but the structured state it gives every logging provider holds the
   raw values, the `apiKey` header among them. A provider that writes structured state — a JSON console formatter or a
   telemetry exporter — would record the key. `NvdLog` records each request without headers instead, and header
   redaction stays configured for any logger added later. The U4 constraint to quiet the factory's categories is
   therefore gone.
2. **Deletion retries at once after making entries writable**, and waits only for files that are still locked. A
   read-only file is not a timing problem, and waiting for it hung the tests that never move their fake clock.
3. **The pacer checks the spacing again after each wait**, because a real timer can end up to a millisecond early.
4. **Syft's failure reasons leave the input out of their text**: the `Input` field names an artifact, and the
   Application names a directory run's input, the target path or the manifests' names. A manifests run's path is the
   staging folder, which a reason should not show.
5. **The NuGet cap is 65,534 entries**: an end record announcing 65,535 (0xFFFF) means ZIP64.
6. **QuestPDF's settings in 2026.9 are `UseSystemFonts` and `ThrowOnMissingTextGlyphs`**, both set to false, and the
   report names its font, Lato, which QuestPDF ships with.
7. **Smaller shapes than designed.**
   - `NvdPageOutcome` folded into `NvdAttempt`.
   - `SyftJsonParser`, `NuspecReader` and `NvdResponseMapper` are static; `NuspecReader.Read` returns `NuspecRead` (a
     package or the reason).
   - `SyftEnvironment` builds every run's environment.
   - `NvdVulnerabilityCatalog` owns the application's one pacer and pipeline, so it is `IDisposable`.
   - The adapters have public constructors of public types only, with internal ones for the tests.
8. **The plan's five refinements are in the code**: PdfPig's id, `Microsoft.Extensions.Configuration`, the
   Infrastructure clock rule, the integration tests' own inputs, and the opt-in live test.
9. **The stand-in reads its script from files and per-run variables**, never from the test process's environment, so
   tests running in parallel cannot affect each other.

## Constraints handed to U4 and U6

- **U4 (host)**:
  - Add `AddAirlockEnvironmentVariables()` after the JSON configuration files.
  - Bind `AirlockOptions` from `Airlock`, then call `AddAirlockApplication` and `AddAirlockInfrastructure`.
  - The health endpoint reports `ISbomGenerator.GetStatusAsync` and `IVulnerabilityCatalog.IsConfigured` as they are.
  - One Airlock runs per staging root.
- **U6 (delivery)**:
  - The image puts Syft v1.55.0 in `bin/` next to the application, and gives the non-root user a writable staging
    root.
  - CI installs Syft with the install script and sets `SYFT_BINARY_PATH`, so the integration tests run on Windows and
    Linux and count towards coverage.
  - The README tells developers to set `SYFT_BINARY_PATH` to the repository's `bin/syft`, or to put Syft on the path.
