# Services

**Stage**: INCEPTION — Application Design (Standard depth)

How the components cooperate at run time: lifetimes, the scan lifecycle, concurrency, cancellation, outcomes and
streams. Values that still need tuning (correlation parallelism, buffer sizes, cache intervals) are fixed in the NFR
Design stages.

## Service catalogue

| Service | Project | DI lifetime | State |
|---|---|---|---|
| Use cases (`StartScan`, `CancelScan`, `EmergencyStop`, `GetScan`, `WatchScan`, `WatchActiveScan`, `RenderScanReport`, `GetSystemHealth`, `WatchLiveLog`) | Application | Singleton | None |
| `ScanCoordinator` | Application | Singleton | Active slot, each running scan's cancellation source and event hub |
| `ScanPipeline`, `PackageCorrelator` | Application | Singleton | None — a run's state lives in its `Scan` and `ScanEventHub` |
| `ScanEventHub` | Application | One per scan, owned by `ScanCoordinator` | Subscribers, latest snapshot |
| `ScanLogRouter` | Application | Singleton | The event hub of each running scan, by scan id |
| `ScanLifetimeService` | Application | Hosted service | None — warns about missing scan roots at start, stops scans at shutdown |
| `InMemoryScanRegistry` | Infrastructure | Singleton | Latest version of each scan |
| `LiveLogBroadcaster`, `LiveLogLoggerProvider` | Infrastructure | Singleton | Subscribers |
| `SyftSbomGenerator`, `DiskArtifactStaging`, `QuestPdfScanReportRenderer`, `HostFileSystem` | Infrastructure | Singleton | Syft tool status, cached briefly |
| `NvdApiClient` | Infrastructure | Singleton; asks the factory for the named HttpClient `Airlock.Nvd` on each request | Shared pacer (singleton) |
| `ScanSession`, `AirlockJs` | Web | Scoped — one per circuit (browser tab) | The tab's inputs and upload, the active slot, the scan it follows, terminal lines, health, time zone, notice (U5) |

## Scan lifecycle

1. **Reserve** — `POST /api/v2/scans` calls `StartScan.ReserveAsync` *before* reading the body. The NVD key must be
   configured and Syft available (otherwise NotReady → 503), and `ScanCoordinator.TryReserve` must claim the single
   active slot (otherwise ScanInProgress → 409). From here every tab sees an active scan receiving its upload, so a
   2 GB upload is never accepted only to be rejected afterwards.
2. **Stage** — `MultipartScanReader` streams each `artifacts` section into the reservation's staging area; count and
   byte limits are enforced while streaming (TooManyArtifacts → 400, TooLarge → 413). If the request fails or the
   client disconnects, disposing the reservation releases the slot and deletes the staging area; no scan is recorded.
3. **Start** — `ScanReservation.StartAsync` validates the target (the directory exists and lies inside
   `AllowedScanRoots` when that is set, or at least one artifact was staged), records a Running `Scan` and hands it
   to `ScanCoordinator.Launch`, which starts `ScanPipeline.RunAsync` on a background task. The API answers 202 with
   the id; in the UI, `airlock.js` returns the same id and the page starts following the scan in-process.
4. **Run** — the pipeline is the only writer of its scan. It enters each phase, runs Syft (all manifests in one
   `dir:` run and each other artifact in its own run, or the directory in one run), merges and de-duplicates
   packages, correlates them through `PackageCorrelator`, builds the summary and finishes. Every transition goes
   through `IScanRegistry.Update` and is published on the scan's `ScanEventHub`.
5. **End** — Completed, or Incomplete when any package is unverified; Failed on a Syft failure, unparseable output or
   the 60-minute limit; Cancelled on cancel, Emergency Stop or server shutdown. In every case, in this order, the final
   status is recorded and `ScanFinished` published, the event hub completes, the staging area is deleted, the active
   slot is released and retention pruning runs (U2 AR-32).

## Concurrency

- One active scan at a time, enforced by `ScanCoordinator` with an atomic claim of the slot.
- A scan has a single writer (its pipeline). Readers get immutable `Scan` instances, so nothing is locked while a
  page renders or an endpoint serializes.
- Correlation processes packages in parallel up to `Correlation.MaxParallelism`. All NVD traffic passes one app-wide
  pacer (one request start every 600 ms, so at most 50 in any 30 s), so parallelism bounds in-flight work, not the
  request rate.
- When `Correlation.OutageThreshold` lookups fail in a row, the scan stops calling NVD and every package not yet decided
  is Unverified with "NVD unavailable" (U2 AR-18), so an outage ends the scan Incomplete within minutes.
- `ScanEventHub` and `LiveLogBroadcaster` give every subscriber its own bounded buffer. A slow subscriber loses its
  oldest log and progress items; snapshot and finished events are never dropped.

## Cancellation and time limits

| Source | Mechanism | Resulting status |
|---|---|---|
| `POST /scans/{id}/cancel`; Emergency Stop from the UI or `POST /system/stop` | The scan's own cancellation source | Cancelled |
| 60-minute scan limit | Cancellation source linked to a timer | Failed — "Scan exceeded the 60-minute limit" |
| 15-minute Syft run limit | `SyftProcessRunner` timeout | Failed, naming the input |
| Server shutdown | Host stopping token linked into every running scan | Cancelled — "Server shutting down" |
| HTTP request aborted during upload | Request token; the reservation is disposed | No scan recorded |
| Emergency Stop while a reservation receives an upload | The reservation ends; the upload is refused with `ReservationStopped` | No scan recorded |

The pipeline's token is independent of the HTTP request that started it, which ends at 202. Cancelling it kills the
Syft process tree and abandons pending NVD calls; the pipeline then records the resulting status itself.

## Outcomes and errors

Expected refusals are values (`Outcome<T>` carrying a `Refusal`), never exceptions. The API maps them through
`ApiProblems` to the statuses below, each with its own problem type (U4 WR-03); the UI shows the message inline.

| Refusal | HTTP | Typical message |
|---|---|---|
| ScanInProgress | 409 | A scan is already in progress |
| NotReady | 503 | NVD API key not configured · Syft not found at `<path>` |
| InvalidTarget | 400 | Target path does not exist · Path is outside the allowed scan roots · Choose a directory or upload at least one artifact · Choose a directory or upload artifacts, not both · Target path is not valid · Target path must be absolute · Target path is a file, not a directory |
| TooManyArtifacts | 400 | At most 25 files per scan |
| TooLarge | 413 | Uploads are limited to 2 GB per scan |
| ReservationStopped | 409 | Emergency Stop ended the upload before the scan started |
| ScanNotFound | 404 | Scan not found or no longer retained |
| ScanNotFinished | 409 | The scan is still running |
| ScanAlreadyFinished | 409 | The scan has already finished |
| ScanHasNoResults | 409 | The scan ended without results |

Unexpected exceptions become a 500 ProblemDetails with no internal details. Inside a scan they make it Failed with a
generic reason; the full error goes to the console log. The Web adds problems of its own — 400 for a malformed
submission, 403 for a cross-site `POST`, 415 for a body that is not multipart, 404 and 405 for unknown addresses and
methods — from the same catalogue (U4 WR-04, `u4-web-api/functional-design/domain-entities.md`).

## Streams

- **Scan events** — `WatchScan` yields a `ScanSnapshot` first, so late subscribers (including SSE clients that
  connect after the 202) see the current state; then phase, progress and scan-scoped log events; and ends with
  `ScanFinished`. For a finished scan the stream is just its snapshot and the finished event.
- **Live Log** — `LiveLogLoggerProvider` captures entries from Airlock's own logging categories at Debug and above.
  The pipeline wraps its work in a logging scope carrying the scan id (`ScanLogScope`); the provider hands every entry
  that carries one to the Application's `ScanLogRouter`, which publishes it on that scan's hub as it is written, so
  no line is lost before a subscription exists (U2 refinement 3). Console verbosity follows `LOG_LEVEL` independently.
- **Active scan** — `WatchActiveScan` pushes every change of the active slot (reserved, running, released) to all
  tabs, so Execute and Emergency Stop are enabled correctly everywhere. Every tab then follows the scan in the slot
  through `WatchScan`, whoever started it, and the tab that submitted it opens its report when it ends (Q-U5-2, U5
  UR-13 and UR-14).

## Health

`GetSystemHealth` combines `ISbomGenerator.GetStatusAsync` (Syft resolvable and its `syft version`, cached for a
short interval) with `IVulnerabilityCatalog.IsConfigured`. It never calls NVD. The status pill re-reads it every 10
seconds, as the original did. OFFLINE is applied in the browser when the circuit drops, because the server cannot
update a page it is no longer connected to.

## Retention

The Application's `RetentionPolicy` picks the finished scans that finished more than 24 hours ago, then, beyond 20
finished scans, the earliest finished; `IScanRegistry.Remove` drops them (U2 AR-33, refinement 1). Pruning runs
whenever a scan finishes and before lookups by id; running scans are never pruned. Nothing survives a restart.

## Startup and configuration

Options bind from `appsettings.json`, environment variables and, in Development, user-secrets; the original names
`NVD_API_KEY`, `SYFT_BINARY_PATH` and `LOG_LEVEL` map onto the `Airlock:*` options. Invalid values stop startup with a
message naming the option. A missing NVD key or Syft binary does not stop startup; Airlock starts DEGRADED. The NVD
key is never written to any log, including HTTP client logging. Airlock answers only the host names in `AllowedHosts`
(by default `localhost`, `127.0.0.1` and `[::1]`), and the API refuses `POST` requests a browser marks as cross-site
(U4 WR-19, WR-20; ADR-0007).
