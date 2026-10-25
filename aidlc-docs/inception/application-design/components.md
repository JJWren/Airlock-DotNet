# Components

**Stage**: INCEPTION — Application Design (Standard depth)
**Inputs**: requirements.md, stories.md (E01–E15), GLOSSARY.md, ADR-0001…0005, approved target layout

Four production projects with inward-only dependencies (ADR-0004). Type names follow GLOSSARY.md. This document says
what each component is responsible for; `component-methods.md` gives their signatures, `services.md` the
orchestration, and `component-dependency.md` how they connect. Detailed rules and algorithms are settled in each
unit's Functional Design.

## Airlock.Domain — model and rules

No dependencies beyond the base class library. Every type is immutable.

| Component | Kind | Responsibility |
|---|---|---|
| `Scan` | Aggregate (immutable record) | Identity, scan target, status, current phase, progress, timestamps, results, warnings, failure. Each transition returns a new `Scan` and rejects illegal moves; `Finish` decides Completed or Incomplete. |
| `ScanId` | Value | Opaque identifier (GUID). |
| `ScanStatus`, `ScanPhase` | Values | Running / Completed / Incomplete / Failed / Cancelled; SbomGeneration / Correlation / Summary (uploads are staged before a scan exists). |
| `ScanTarget` | Value (closed hierarchy) | `DirectoryTarget(path)` or `ArtifactsTarget(artifacts)`. |
| `Artifact`, `ArtifactKind` | Value | An uploaded file: sanitized name, size, and kind (Manifest or Other). |
| `ManifestCatalog` | Rule | Recognizes manifest file names (`requirements*.txt`, `package-lock.json`, `poetry.lock`, `go.mod`, …). |
| `Package` | Value | Identity by type (exact), name (ignoring case) and version (exact). |
| `PackageVersion` | Rule | Whether a version is known; the version sent to NVD (Go modules drop a leading `v`). |
| `CandidateCpe`, `CpeSource` | Value + rule | A CPE and where it came from (Dictionary or Generated); orders candidates dictionary-first, then generated ones whose vendor and product equal the package name, and keeps at most three. |
| `Cve` | Value | Id, description, NVD URL, every CVSS score NVD published, affected products. |
| `CvssScore`, `CvssVersion`, `CvssSource` | Value + rule | Base score, severity, version and source (NVD Primary or CNA Secondary); picks the preferred score: v3.1 → v3.0 → v4.0 → v2.0, NVD's own score first within a version. |
| `Severity`, `SeverityScale`, `SeverityThreshold` | Value + rules | Ordered levels; severity derived from a score when NVD gives none; the threshold admits HIGH and CRITICAL. |
| `ProductName` | Rule | Derives the product name used for a product match; withholds names too generic to identify a product (ADR-0006, BR-09). |
| `MatchMethod` | Value (closed hierarchy) | `CpeMatch(cpe)` or `ProductMatch(product)`. |
| `Finding`, `FindingSelection` | Values + rule | One CVE at or above the threshold, with its selected CVSS score; selecting findings from matched CVEs also yields the unscored CVE ids. |
| `VulnerablePackage` | Value | Package, match method, findings; exposes the highest severity. |
| `UnverifiedPackage`, `UnverifiedReason` | Value | Package, reason (`LookupFailed` or `VersionUnknown`) and detail. |
| `ScanSummary` | Value | Totals; built only through a factory that enforces total = clean + vulnerable + unverified. |
| `ScanProgress`, `ScanWarning`, `ScanFailure` | Values | Correlated-of-total counter; non-fatal note with a code (`NoPackagesIdentified`, `DistributionBackportsNotConsidered`, `CvesWithoutSeverity`, `NotProductMatched`); fatal reason with a code — each optionally naming the input. |

## Airlock.Application — use cases, orchestration, ports

May depend on Domain and on `Microsoft.Extensions.*.Abstractions` (logging, options, dependency injection) only.

**Use cases** (plain classes, decision N7)

| Use case | Serves | Responsibility |
|---|---|---|
| `StartScan` + `ScanReservation` | API (UI submits through it) | Checks readiness and claims the single active slot *before* any upload is read; streams artifacts into staging under the limits; validates the target; starts the scan. |
| `CancelScan` | API, UI | Cancels one scan by id. |
| `EmergencyStop` | API, UI Terminal | Cancels the active scan, whoever started it; reports stopped or idle. |
| `GetScan` | API, UI Reports | Returns the latest version of a retained scan. |
| `WatchScan` | API SSE, UI | Streams a scan's events, starting with a snapshot so late subscribers catch up. |
| `WatchActiveScan` | UI | Tells every tab whether a scan is active (Execute disabled, Emergency Stop enabled). |
| `RenderScanReport` | API (UI downloads through it) | Produces the PDF Scan Report of a finished scan. |
| `GetSystemHealth` | API, UI status pill | Syft availability with version, NVD key configured → Online or Degraded. |
| `WatchLiveLog` | API SSE, UI Terminal | Streams the global Live Log. |

**Services**

| Service | Responsibility |
|---|---|
| `ScanCoordinator` | Owns the single active slot, launches the pipeline on a background task, holds each running scan's cancellation, exposes the active-scan state and each scan's event hub. |
| `ScanPipeline` | Runs one scan's phases: staging done → SBOM generation (manifests together, each other artifact alone) → merge and de-duplicate → correlation → summary; writes every transition to the registry and publishes events. |
| `PackageCorrelator` | Correlates one package: ranked candidate CPEs, then a product match; Unverified on lookup failure or an unknown version. |
| `ScanEventHub` | Per-scan broadcast of events to any number of subscribers, with snapshot-on-subscribe. |

**Ports** (implemented in Infrastructure)

| Port | Purpose |
|---|---|
| `ISbomGenerator` | One SBOM run over a directory or a single file; tool status (availability, version). |
| `IVulnerabilityCatalog` | NVD lookups by CPE and by product (any vendor), each answering Found, NoRecords or Failed; whether a key is configured. |
| `IArtifactStaging`, `IStagingArea` | A per-scan staging folder that accepts streamed files and deletes itself when disposed. |
| `IScanReportRenderer` | Renders a finished scan as a PDF. |
| `IScanRegistry` | Stores the latest version of each scan; atomic updates; retention pruning. |
| `ILiveLog` | Subscriptions to the Live Log, globally or for one scan. |
| `IHostFileSystem` | Directory existence, full paths, and "is this path inside that root" checks. |
| `TimeProvider` (BCL) | Clock for timestamps, durations and retention. |

**Contracts**: `Outcome<T>` with `Refusal` (expected refusals with a code and message), `ScanEvent` family,
`LiveLogEntry`, `ActiveScanState`, `SystemHealth`, `ScanReportDocument`, and `AirlockOptions` (limits, timeouts,
retention, allowed scan roots, correlation parallelism).

## Airlock.Infrastructure — adapters

May depend on Application and Domain plus the adapter libraries (HTTP resilience, QuestPDF, options binding).

| Component | Implements | Responsibility |
|---|---|---|
| `SyftSbomGenerator` (+ `SyftProcessRunner`, `SyftJsonParser`, `NuspecReader`) | `ISbomGenerator` | Runs `syft <source> -o json -q` (with `SYFT_CHECK_FOR_APP_UPDATE=false`) and a per-run timeout; kills the whole process tree on cancellation or timeout; maps Syft JSON to packages with candidate CPEs and their sources; for a NuGet package also reads its `.nuspec` and adds the package it names; `syft version` for health. |
| `NvdVulnerabilityCatalog` (+ `NvdApiClient`, `NvdResponseMapper`) | `IVulnerabilityCatalog` | An HttpClient from the factory to the CVE API 2.0 with retries (own minimum backoff on 429), timeouts, pagination and the shared 50-per-30-seconds limiter; CPE lookups (`cpeName`) and product matches (`virtualMatchString`); maps CVSS metrics with their sources and affected products; unknown or malformed CPEs mean "no records", distinct from "lookup failed". |
| `DiskArtifactStaging` (+ `DiskStagingArea`) | `IArtifactStaging` | One folder per scan (manifests together, other artifacts each in their own folder); `.whl` and `.nupkg` saved with a `.zip` suffix so Syft unpacks them; names sanitized by the Application's `ArtifactName` and checked again; byte limits enforced while streaming; recursive delete on dispose. |
| `QuestPdfScanReportRenderer` | `IScanReportRenderer` | The Scan Report layout (title, summary, findings, Unverified section, warnings). |
| `InMemoryScanRegistry` | `IScanRegistry` | The latest version of each scan; the Application's `RetentionPolicy` decides what to remove. |
| `LiveLogBroadcaster` + `LiveLogLoggerProvider` | `ILiveLog` | Logger provider that captures Airlock's own categories (with the scan id from the logging scope) and fans entries out to bounded per-subscriber buffers, replaying the last 200 to a new subscriber; scan-scoped entries also go to `ScanLogRouter`. |
| `HostFileSystem` | `IHostFileSystem` | Thin wrapper over `System.IO`. |
| `SyftOptions`, `NvdOptions`, `StagingOptions` + validators | — | Bound from configuration, including the original environment-variable names; validated at startup. |
| `AddAirlockInfrastructure` | — | Registers every adapter and option. |

## Airlock.Web — host, API, UI

The composition root. Depends on every other project plus ASP.NET Core, MudBlazor, OpenAPI and Scalar.

| Area | Components | Responsibility |
|---|---|---|
| Host | `Program`, `AirlockHost` | `AirlockHost`, the composition root that `Program` calls, wires all layers, logging, options, Kestrel limits, health, OpenAPI + Scalar at `/docs`, Blazor (global Interactive Server). |
| API | `ScanEndpoints`, `SystemEndpoints`, `LogEndpoints` | The `/api/v2` resources (N1); SSE through Airlock's own stream writer, which adds keep-alive comments (U4-NFR-05). |
| API | `MultipartScanReader` | Streams `targetPath` and `artifacts[]` sections into a `ScanReservation` without buffering whole files. |
| API | `ScanDtoMapper`, `ApiProblems` | camelCase DTOs named after the glossary; refusal codes and the Web's own problems → status codes + ProblemDetails, each with its problem type (U4 WR-03, WR-04). |
| API | `CrossSiteRequestFilter` | Refuses `POST` requests a browser marks as cross-site (U4 WR-19); the host filter answers only `AllowedHosts` (WR-20, ADR-0007). |
| UI layout | `App`, `Routes`, `MainLayout`, `NavMenu` | Top bar (brand, inert placeholders), navigation drawer, Execute Audit shortcut, reconnect handling. |
| UI pages | `ControlCenter` (`/`), `ActiveLogs` (`/logs`), `Reports` (`/reports/{id?}`) | The three original views. |
| UI components | `StatusPill`, `ArtifactPicker`, `ScanProgress`, `ElapsedTimer`, `Terminal`, `StopScanDialog`, `SummaryCards`, `UnverifiedBanner`, `InventoryGrid`, `VulnerabilityDetail` | Building blocks of the pages (MudBlazor-based). |
| UI state | `ScanSession` (scoped per circuit) | The tab's directory path, staged-file list, current scan, terminal lines, browser time zone; subscriptions to scan, active-scan and live-log streams. |
| UI interop | `AirlockJs` + `wwwroot/js/airlock.js` | Submit the scan form over HTTP with upload progress; download text (logs); copy to clipboard; browser time zone; connection watcher for OFFLINE. |
| UI theme | `AirlockTheme` | MudTheme with the original palette, Inter and JetBrains Mono, uppercase monospace labels. |

## Tests

| Project | Covers |
|---|---|
| `Airlock.Domain.Tests` | Rules and transitions (CVSS selection, highest severity, summary invariant, status machine). |
| `Airlock.Application.Tests` | Use cases, pipeline and correlator against port fakes; regression tests for defects 3, 6–12, 15. |
| `Airlock.Infrastructure.Tests` | Syft JSON parsing, NVD mapping with a fake HTTP handler, limiter, staging and sanitizer, PDF smoke test; Syft integration tests by category. |
| `Airlock.Web.Tests` | bUnit component tests; API integration tests with WebApplicationFactory. |
| `Airlock.Architecture.Tests` | ArchUnitNET dependency rules (see `component-dependency.md`). |
