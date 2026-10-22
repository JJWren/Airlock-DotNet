# U2 Application — Domain Entities (Application types)

**Stage**: CONSTRUCTION — U2 Application — Functional Design (Comprehensive depth)

The Application layer adds no business entities of its own; the Domain (U1) owns those. This document defines the types
the Application adds around them: what use cases return, the reservation, the active slot, events and log entries, the
records that cross the ports, correlation results and options. Signatures are in
`inception/application-design/component-methods.md`; the rules that use these types are in `business-rules.md` (AR-nn).

## Outcomes

`Outcome<T>` holds either a value or a `Refusal`, never both; its factories are on the static `Outcome` class (analyzer rule CA1000). Expected refusals are values, not exceptions.
`Refusal(Code, Message)` carries a message written for the person who sees it: shown inline in the UI, and as the
ProblemDetails `detail` in the API.

| `RefusalCode` | HTTP | Raised by | Messages |
|---|---|---|---|
| `ScanInProgress` | 409 | `StartScan.ReserveAsync` | "A scan is already in progress" |
| `NotReady` | 503 | `StartScan.ReserveAsync` | "NVD API key not configured"; the SBOM tool's own detail, such as "Syft not found at /opt/airlock/bin/syft"; both reasons joined with "; " |
| `InvalidTarget` | 400 | `ScanReservation.StartAsync` | "Choose a directory or upload at least one artifact"; "Choose a directory or upload artifacts, not both"; "Target path is not valid"; "Target path must be absolute"; "Target path does not exist"; "Target path is a file, not a directory"; "Path is outside the allowed scan roots" |
| `TooManyArtifacts` | 400 | `ScanReservation.AddArtifactAsync` | "At most 25 files per scan" (the configured number) |
| `TooLarge` | 413 | `ScanReservation.AddArtifactAsync` | "Uploads are limited to 2 GB per scan" (the configured size, in GB or MB) |
| `ReservationStopped` | 409 | `ScanReservation` | "Emergency Stop ended the upload before the scan started" — **new** (AR-28) |
| `ScanNotFound` | 404 | `CancelScan`, `RenderScanReport` | "Scan not found or no longer retained" |
| `ScanNotFinished` | 409 | `RenderScanReport` | "The scan is still running" |
| `ScanAlreadyFinished` | 409 | `CancelScan` | "The scan has already finished" |
| `ScanHasNoResults` | 409 | `RenderScanReport` | "The scan ended without results" — **new** (AR-34) |

## Reservation

`ScanReservation` holds the active slot from `StartScan.ReserveAsync` until the scan starts or the reservation ends.

| Field | Meaning |
|---|---|
| `Id` | The `ScanId`, allocated at reservation; the scan keeps it |
| Staged artifacts | Count and total bytes so far, checked against the limits as files arrive |
| State | Open → Started, or Open → Ended (refused, abandoned or stopped) |

Invariants: at most one reservation or running scan holds the slot at any time; after a refusal every later call
returns the same refusal; disposing a reservation that has not started releases the slot and deletes its staging area
(AR-03); disposing a started one does nothing, because the scan now owns the slot.

## Active slot

`ActiveScanState(ScanId? ScanId, bool ReceivingUpload)`:

| State | `ScanId` | `ReceivingUpload` |
|---|---|---|
| Idle | none | false |
| Reserved, no upload yet | the id | false |
| Receiving an upload | the id | true — from the first file until `StartAsync` or the end of the reservation |
| Running | the id | false |

## Events and log entries

| `ScanEvent` | Published when | Carries | A slow subscriber may lose it |
|---|---|---|---|
| `ScanSnapshot` | First item of every subscription | The scan's latest version | Never |
| `PhaseChanged` | The scan enters a phase | `ScanPhase` | Never |
| `PackagesCorrelated` | Progress is recorded | `ScanProgress` (n of total) | Yes — only the latest matters |
| `ScanLogged` | A log entry is written inside the scan's pipeline | `LiveLogEntry` | Yes, oldest first |
| `ScanFinished` | The final status is recorded; always the last item | `ScanStatus` | Never |

`LiveLogEntry(Timestamp, Level, Category, Message, ScanId?)`: the time in UTC, the log level, the logging category
(Airlock's own categories only), one line of plain text, and the scan id when the entry was written inside a scan.

## Port records

| Record | Meaning |
|---|---|
| `DirectorySource(Path)` | One Syft `dir:` run over a full directory path: the target directory, or the staging area's manifests directory |
| `FileSource(Path, Artifact)` | One Syft run over one staged artifact file |
| `SbomPackage(Package, CandidateCpes)` | A package as Syft reported it, with its candidate CPEs in Syft's order and with their sources |
| `SbomProduced(Packages, IsContainerImage)` | A successful run. **`IsContainerImage` is new**: Syft identified the input as a container image (AR-11) |
| `SbomFailed(Failure)` | A failed run: `ScanFailure` with `SyftFailed`, `SyftTimedOut` or `SyftOutputUnreadable` and the input |
| `SbomToolStatus(Available, Version, Detail)` | Whether the SBOM tool can run, its version, and the reason when it cannot |
| `CatalogFound(Records)` | NVD returned at least one CVE record |
| `CatalogNoRecords` | NVD returned none, or does not know the CPE, or rejected it as malformed (S0 N1, N2) |
| `CatalogFailed(Reason)` | The lookup still failed after the catalog's own retries |
| `StagedArtifact(Artifact, Path)` | An uploaded file, stored under its sanitized name (`ArtifactName`) |
| `ArtifactStaged(Artifact)`, `ArtifactTooLarge` | The result of staging one file: stored, or over the bytes left for the scan |

`IHostFileSystem` answers six questions about the host: `IsAbsolute` (**new**, NFR Design), `DirectoryExists`, `FileExists` (**new**), `GetFullPath`,
`ResolveLinks` (**new**: the path with every symbolic link on the way followed) and `IsWithin` (whole path segments,
with the host's case rules).

## Correlation results

| `PackageCorrelation` | Meaning |
|---|---|
| `CleanCorrelation(Package, Method, UnscoredCveIds)` | No finding. `Method` is the CPE or product match that found none; **null means not product-matched because the name is too generic** (AR-15, Q-U2-3) |
| `VulnerableCorrelation(Result, UnscoredCveIds)` | A `VulnerablePackage` with at least one finding and its match method |
| `UnverifiedCorrelation(Result)` | An `UnverifiedPackage`: `LookupFailed` with the failure or "NVD unavailable …" (AR-18), or `VersionUnknown` |

`UnscoredCveIds` are the matched CVEs that NVD has not scored; they feed the package's `CvesWithoutSeverity` warning.

## Options

| `AirlockOptions` | Default | Rule |
|---|---|---|
| `Limits.MaxArtifactsPerScan` | 25 | AR-06 |
| `Limits.MaxBytesPerScan` | 2 GiB | AR-06 |
| `Timeouts.SyftRun` | 15 minutes (enforced by U3's Syft runner) | AR-09 |
| `Timeouts.Scan` | 60 minutes | AR-29 |
| `Retention.Duration`, `Retention.MaxScans` | 24 hours, 20 | AR-33 |
| `AllowedScanRoots` | empty = unrestricted | AR-05 |
| `Correlation.MaxParallelism` | set in U2 NFR Design | AR-17 |
| `Correlation.OutageThreshold` | failed lookups in a row before NVD counts as unavailable; set in U2 NFR Design | AR-18 |
| `Timeouts.CancelGracePeriod` | how long `CancelScan` waits for the Cancelled status; set in U2 NFR Design | AR-27 |

## Domain amendment (Q-U2-3)

`ScanWarningCode` gains a fourth code, `NotProductMatched`: one warning per scan listing the packages that had no CPE
records and a name too generic to product-match (AR-19). U2 Code Generation adds it to `Airlock.Domain`, with its test;
U1's `domain-entities.md` and `component-methods.md` record it.
