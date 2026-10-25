# Units of Work

**Stage**: INCEPTION — Units Generation (Standard depth)

Six units built in order, preceded by the S0 spikes. Each unit lives on its own branch (`feature/u1-domain` …
`feature/u6-delivery`); commits happen only when the owner asks, and nothing is pushed without approval. Stage depths
per unit are in `../plans/workflow-planning.md`.

**Done for every unit**: `dotnet build -warnaserror` and `dotnet test` green; the architecture rules that apply to the
unit enforced; new public types named after GLOSSARY.md; the unit's Code Generation plan fully ticked; the Code
Generation checkpoint approved.

---

## S0 — Spikes (prelude, not a unit)

- **Purpose**: confirm the external behaviours the design depends on before designing U2 and U3.
- **Questions answered**:
  - Which artifact classes Syft v1.55.0 catalogs as single files: manifest, lock file, `.jar`, `.whl`, `.nupkg`,
    `.zip`, `.tar.gz`, `docker save` tarball, Go binary, .NET assembly — and which candidate-CPE sources it reports.
  - How NVD answers an unknown `cpeName` (empty result or an error status), and what throttling looks like
    (status code, `Retry-After`).
- **Output**: `aidlc-docs/construction/spikes/spike-results.md`. Throwaway scripts stay in the session scratchpad and
  are not committed.
- **Needs**: your permission to download the official Syft v1.55.0 release from `github.com/anchore/syft`
  (checksum-verified). NVD needs only a handful of requests, which work without a key.
- **Done**: every question answered with evidence; any answer that contradicts a requirement brought back to you as a
  question before U1 starts.

## U1 — Domain

- **Purpose**: the solution scaffold and the domain model with its rules.
- **Scope**: `Airlock.slnx`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`,
  `.gitignore`, `LICENSE` (MIT), minimal `README.md`; `src/Airlock.Domain`; `tests/Airlock.Domain.Tests`;
  `tests/Airlock.Architecture.Tests` (rule 1).
- **Delivers**: every Domain type in `component-methods.md` — the `Scan` state machine, `ScanTarget`, `Artifact` and
  `ManifestCatalog`, `Package`, `CandidateCpe` ordering (dictionary first, then name-equal guesses), `Cve`, `CvssScore`
  selection (version precedence, NVD's own score first), `Severity` and the threshold, `ProductName` (the product
  name for a product match, withheld when too generic), `MatchMethod`, `Finding`, `VulnerablePackage` (highest severity),
  `UnverifiedPackage`, `ScanSummary` (invariant), `ScanProgress`, `ScanWarning`, `ScanFailure`.
- **Tests**: one test class per rule and per transition, including illegal transitions.
- **Provides**: the domain contract every other unit compiles against.

## U2 — Application

- **Purpose**: the use cases and the scan orchestration, written against ports.
- **Scope**: `src/Airlock.Application`; `tests/Airlock.Application.Tests`; architecture rules 2, 4 and 6.
- **Delivers**: `StartScan` with `ScanReservation`, `CancelScan`, `EmergencyStop`, `GetScan`, `WatchScan`,
  `WatchActiveScan`, `RenderScanReport`, `GetSystemHealth`, `WatchLiveLog`; `ScanCoordinator`, `ScanPipeline`,
  `PackageCorrelator`, `ScanEventHub`; `Outcome` and `Refusal`; the `ScanEvent` family and `LiveLogEntry`; the ports;
  `AirlockOptions`; `AddAirlockApplication`.
- **Tests**: against in-memory fakes of every port. The original's pytest scenarios (CPE hit, keyword fallback) and a
  regression test per defect the unit owns: cancellation during correlation (3), Syft failure is never clean (6),
  missing key refuses the scan (7), NVD failure gives Unverified and Incomplete (8), product matching never turns
  description-only mentions such as the GeoServer and Laminas CVEs into findings (10), no product match after a CPE
  match (12), a second scan is refused (15); plus unknown versions becoming Unverified, the image-scan backports
  warning, the 60-minute limit and snapshot-first event streams.
- **Provides**: frozen port contracts for U3 and use-case signatures for U4 and U5.

## U3 — Infrastructure

- **Purpose**: the adapters behind every port.
- **Scope**: `src/Airlock.Infrastructure`; `tests/Airlock.Infrastructure.Tests`; `scripts/install-tools.ps1`,
  `scripts/install-tools.sh` (Syft v1.55.0 into `./bin`, checksum-verified); architecture rule 3.
- **Delivers**: `SyftSbomGenerator` (process runner with tree kill and timeout, JSON parser, `.nuspec` reader),
  `NvdVulnerabilityCatalog` (an HttpClient from the factory with retries, timeouts, pagination and the 50-per-30-seconds limiter;
  CPE lookups and product matches; response mapper with CVSS metrics, their sources and affected products),
  `DiskArtifactStaging` with the `.zip` suffix for wheels and NuGet packages (names sanitized by the Application),
  `QuestPdfScanReportRenderer`,
  `InMemoryScanRegistry` (a plain store), `LiveLogBroadcaster` and `LiveLogLoggerProvider`, `HostFileSystem`, options with
  validation and environment-variable mapping, `AddAirlockInfrastructure`.
- **Tests**: Syft JSON parsing from recorded fixtures (the S0 outputs); Syft integration tests
  (`Category=Integration`, need the binary) including wheels and NuGet packages; the `.nuspec` reader; NVD mapping
  through a fake HTTP handler with the recorded S0 responses (CPE hit, unknown CPE, malformed CPE 404, product match,
  503 → failed lookup, 429 with `Retry-After: 0` retried with a minimum backoff); limiter compliance (defect 9); CVSS
  metric mapping with Primary and Secondary sources (defect 11); sanitizer against path traversal (defect 5);
  process-tree kill (defect 3); registry retention; Live Log reaching two subscribers (defect 4); the NVD key never
  appearing in logs; a PDF smoke test.
- **Provides**: registrations for the host in U4.

## U4 — Web API

- **Purpose**: the host and the `/api/v2` surface.
- **Scope**: `src/Airlock.Web` (Program, `Api/*`, `appsettings*.json`); `tests/Airlock.Web.Tests` (API integration);
  architecture rule 5 for endpoints; the staging-root lock in U3's `StagingSweepService` (U4-NFR-09).
- **Delivers**: the composition root; `ScanEndpoints`, `SystemEndpoints`, `LogEndpoints`; `MultipartScanReader`
  (streaming, no whole-file buffering); `ScanDtoMapper` (camelCase, glossary names); `ApiProblems` (the problem
  catalogue) and `docs/api/problems.md`; `CrossSiteRequestFilter` and the `AllowedHosts` default (ADR-0007); SSE
  through Airlock's own stream writer with keep-alive comments (U4-NFR-05); health endpoint; OpenAPI document with
  Scalar at `/docs`; Kestrel and per-endpoint request limits; configuration sources (appsettings, environment,
  user-secrets).
- **Tests**: WebApplicationFactory with faked ports: 202 with `Location`, 409 busy, 503 not ready, 400 and 413
  limits, snapshot-first SSE, report 409 while running, 404 after retention, ProblemDetails without internal
  details, an upload streamed without buffering in memory, cross-site `POST` requests and unknown host names refused,
  the OpenAPI document listing every endpoint (the rule-to-test map of U4's functional design).
- **Provides**: the endpoints U5 calls for submission and downloads.

## U5 — Web UI

- **Purpose**: the Blazor UI on MudBlazor, themed to the original look.
- **Scope**: `src/Airlock.Web` (`Components/*`, `State/ScanSession`, `Interop/AirlockJs`, `Theme/AirlockTheme`,
  `wwwroot/js/airlock.js`); `tests/Airlock.Web.Tests` (bUnit); architecture rule 5 for components.
- **Delivers**: `MainLayout`, `NavMenu`, the ControlCenter, ActiveLogs and Reports pages, the components
  (`StatusPill`, `ArtifactPicker`, `ScanProgress`, `ElapsedTimer`, `Terminal`, `StopScanDialog`, `SummaryCards`,
  `UnverifiedBanner`, `InventoryGrid`, `VulnerabilityDetail`), the theme and the JavaScript module (submit with upload
  progress, download text, copy, browser time zone, connection watcher).
- **Tests**: bUnit — Risk shows the highest severity (defect 2), Method labels product matches (defect 1), filter by
  package or CVE, selection by package identity, Unverified banner, inert placeholders (defect 16), dismissing the
  stop dialog, timer format; then a manual end-to-end pass in the browser pane against a real NVD key.
- **Provides**: the finished application for packaging.

## U6 — Delivery

- **Purpose**: packaging, automation and the final documentation.
- **Scope**: `Dockerfile`, `.dockerignore`, `docker-compose.yml`, `.env.example`, `.github/workflows/ci.yml`,
  `.github/workflows/codeql.yml`, `.github/dependabot.yml`, the complete `README.md`.
- **Delivers**: a multi-stage image (`anchore/syft:v1.55.0` stage, SDK build, ASP.NET runtime, non-root user);
  compose with the `/uploads` mount and a staging volume; CI that restores, builds, tests and builds the image;
  CodeQL for C#; Dependabot for NuGet, Docker and GitHub Actions; README covering setup, configuration, the accepted
  no-authentication risk, licensing (MIT, QuestPDF Community licence) and the S0 findings.
- **Tests**: `docker compose up --build` smoke test (ONLINE with a key, a scan of `/uploads`); workflow files checked
  for syntax.
- **Provides**: the input to Build and Test.
