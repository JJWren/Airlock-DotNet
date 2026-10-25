# U4 Web API — Domain Entities (API types)

**Stage**: CONSTRUCTION — U4 Web API — Functional Design (Standard depth)

The Web API adds no business entities. It carries the Application's results over HTTP: this document defines the
request it reads, the JSON it writes, the event payloads and the problem catalogue. Rules are WR-nn in
`business-rules.md`; worked requests and responses are in `business-logic-model.md`.

## Conventions (WR-02)

- **Names**: camelCase properties, named after the glossary.
- **Values**: every enumeration value in capitals with underscores (Q-U4-1): `RUNNING`, `SBOM_GENERATION`, `CRITICAL`,
  `LOOKUP_FAILED`, `ONLINE`, `TERMINATED`. The one exception is `cvssVersion`, a version number written `"3.1"`.
- **Times**: UTC, RFC 3339 with milliseconds and `Z` — `2026-10-22T12:00:00.000Z`.
- **Numbers**: counts and sizes are integers; `baseScore` has one decimal place; `durationSeconds` has three.
- **Shape**: every property is always present — `null` when it does not apply, `[]` for an empty list.
- **Layout**: HTTP JSON bodies are indented by two spaces, as the original's Export JSON was; an event's `data` is one
  compact line. Text is written as it is, escaping only what JSON requires, so `Flask's` and the Live Log's emoji are
  not written as `'` or `🚀` — the original's `JSON.stringify` did the same.
- **Schema names** in the OpenAPI document follow the glossary — `Scan`, `Finding`, `UnverifiedPackage` — without the
  `Dto` suffix the C# types carry.

## The submission — `POST /api/v2/scans`, `multipart/form-data`

| Part | Kind | How many | Meaning |
|---|---|---|---|
| `targetPath` | text, UTF-8 | 0 or 1 | A directory on the Airlock host; blank counts as none (U2 AR-04) |
| `artifacts`, or `artifacts[]` | file | 0 to `Limits.MaxArtifactsPerScan` (25) | One uploaded artifact. Its name comes from the part's `Content-Disposition` (`filename*` when present, as .NET's `HttpClient` sends it, otherwise `filename`) and is sanitized by U2 AR-07; the part's own `Content-Type` is ignored, because Syft detects the type |

Part names are compared ignoring case; any other name is refused (WR-07). A scan has either a non-blank `targetPath`
or at least one artifact, never both (U2 AR-04).

```bash
curl -F artifacts=@requirements.txt -F artifacts=@app-image.tar "$AIRLOCK/api/v2/scans"
curl -F targetPath=/uploads/acme-app "$AIRLOCK/api/v2/scans"
```

`$AIRLOCK` stands for the server's address, which U4 Infrastructure Design fixes.

## The submission's answer — `ScanAcceptedDto` (202)

| Property | Type | Meaning |
|---|---|---|
| `id` | string | The scan id, a GUID in its 36-character form (`01a1637b-7e00-7c3a-9f12-6b84d0e5a1c7`) |
| `status` | `RUNNING` | Always `RUNNING` |

The `Location` header carries `/api/v2/scans/{id}`.

## The scan — `ScanDto`

Returned by `GET /api/v2/scans/{id}`, sent as the `snapshot` event, and saved by the UI's Export JSON (FR-22).

| Property | Type | Meaning |
|---|---|---|
| `id` | string | The scan id |
| `status` | `RUNNING`, `COMPLETED`, `INCOMPLETE`, `FAILED`, `CANCELLED` | `Scan.Status` |
| `phase` | `SBOM_GENERATION`, `CORRELATION`, `SUMMARY` | The phase the scan is in, or ended in |
| `progress` | `ScanProgressDto` or `null` | From correlation on; `null` before |
| `target` | `ScanTargetDto` | What the scan inspects |
| `startedAt` | time | When the scan started |
| `finishedAt` | time or `null` | When it finished |
| `durationSeconds` | number or `null` | `finishedAt` − `startedAt` |
| `summary` | `ScanSummaryDto` or `null` | `COMPLETED` and `INCOMPLETE` only |
| `vulnerablePackages` | `VulnerablePackageDto[]` | Highest severity first, then by name and version (the Domain's order) |
| `unverifiedPackages` | `UnverifiedPackageDto[]` | By name and version |
| `warnings` | `ScanWarningDto[]` | In the order they arose; a running scan may already have some |
| `failure` | `ScanFailureDto` or `null` | `FAILED` only |
| `cancellationReason` | string or `null` | `CANCELLED` only: "Cancelled by request", "Emergency Stop" or "Server shutting down" |

A `FAILED` or `CANCELLED` scan has no summary and empty lists: no partial result is kept (U1 BR-16).

### Its parts

| Type | Properties |
|---|---|
| `ScanProgressDto` | `correlated` (integer), `total` (integer) — packages decided so far, of all of them |
| `ScanTargetDto` | `kind` (`DIRECTORY` or `ARTIFACTS`); `path` (the full path of a directory target, else `null`); `artifacts` (`ArtifactDto[]`, `[]` for a directory) |
| `ArtifactDto` | `fileName` (the sanitized name, as uploaded — never the staging name with its `.zip` suffix); `sizeBytes` (integer); `kind` (`MANIFEST` or `OTHER`, U1 BR-12) |
| `ScanSummaryDto` | `totalPackages`, `vulnerablePackages`, `cleanPackages`, `unverifiedPackages` (packages); `criticalFindings`, `highFindings` (CVEs) — FR-14 |
| `VulnerablePackageDto` | `type`, `name`, `version` (the package, flat); `highestSeverity` (`CRITICAL` or `HIGH` — the Risk column, U1 BR-06); `matchMethod` (`MatchMethodDto`); `findings` (`FindingDto[]`, highest severity first, U1 BR-07) |
| `MatchMethodDto` | `kind` (`CPE` or `PRODUCT`); `cpe` (the CPE that was asked for, after U3 decision 1, else `null`); `cpeSource` (`DICTIONARY` or `GENERATED`, else `null`); `product` (the product name matched from any vendor, else `null`) |
| `FindingDto` | `cveId`; `severity` (`CRITICAL` or `HIGH`); `baseScore`; `cvssVersion` (`"2.0"`, `"3.0"`, `"3.1"` or `"4.0"`); `scoreSource` (`PRIMARY` for NVD's own score, `SECONDARY` for a CNA's); `nvdUrl`; `description` |
| `UnverifiedPackageDto` | `type`, `name`, `version`; `reason` (`LOOKUP_FAILED` or `VERSION_UNKNOWN`); `detail` ("NVD returned 503", "version unknown", …) |
| `ScanWarningDto` | `code` (`NO_PACKAGES_IDENTIFIED`, `DISTRIBUTION_BACKPORTS_NOT_CONSIDERED`, `CVES_WITHOUT_SEVERITY`, `NOT_PRODUCT_MATCHED`); `message`; `input` (the artifact or package concerned, else `null`) |
| `ScanFailureDto` | `code` (`SYFT_FAILED`, `SYFT_TIMED_OUT`, `SYFT_OUTPUT_UNREADABLE`, `SCAN_TIMED_OUT`, `UNEXPECTED`); `reason`; `input` (the directory or artifact concerned, else `null`) |

A finding carries the score Airlock uses (U1 BR-01, BR-02), not every score NVD lists, and not the affected products
used for matching.

## Emergency Stop's answer — `EmergencyStopDto`

| Property | Type | Meaning |
|---|---|---|
| `status` | `TERMINATED` or `IDLE` | Whether anything held the active slot (U2 AR-28) |
| `scanId` | string or `null` | What held it: a running scan, or an upload's reservation, whose id never becomes a scan |

## Health — `SystemHealthDto`

| Property | Type | Meaning |
|---|---|---|
| `status` | `ONLINE` or `DEGRADED` | ONLINE when every check passes (U2 AR-35) |
| `version` | string | Airlock's version, `2.0.0` |
| `checks` | `HealthCheckDto[]` | `Syft`, then `NVD API key` |
| `HealthCheckDto` | `name`, `passing` (boolean), `detail` | Syft passing: its version (`1.55.0`); failing: the reason ("Syft not found at /opt/airlock/bin/syft"). The key passing: `configured`; failing: "NVD API key not configured" |

## Events — `text/event-stream`

`GET /api/v2/scans/{id}/events`:

| Event | `data` | When |
|---|---|---|
| `snapshot` | `ScanDto` | First, always |
| `phase` | `{ "phase": … }` | The scan entered a phase |
| `progress` | `ScanProgressDto` | A package was decided; a slow client may miss some |
| `log` | `LogEntryDto` without `scanId` | A Live Log line written for this scan; a slow client may miss some |
| `finished` | `{ "status": … }` | Last, always; the server then ends the stream |

`GET /api/v2/logs/stream` sends only `log` events, each a `LogEntryDto` with its `scanId`. On either stream, a
`: keep-alive` comment line arrives after 15 seconds of silence; clients ignore it (WR-13).

| Type | Properties |
|---|---|
| `LogEntryDto` | `timestamp` (time); `level` (`TRACE`, `DEBUG`, `INFO`, `WARNING`, `ERROR` or `CRITICAL` — the names `LOG_LEVEL` accepts, U3 IR-23); `category` (the logging category, such as `Airlock.Application.Scanning.ScanPipeline`); `message`; `scanId` (string, or `null` for a line outside a scan; Live Log stream only) |

On the wire:

```text
event: phase
data: {"phase":"CORRELATION"}

event: progress
data: {"correlated":3,"total":4}

```

## Problems — `application/problem+json` (RFC 9457)

Members: `type`, `title`, `status`, `detail` and `traceId`. `type` is
`https://github.com/JJWren/Airlock-DotNet/blob/main/docs/api/problems.md#<slug>` (Q-U4-2); `docs/api/problems.md`, part
of U4's code, describes each kind and what a client can do about it. `title` is fixed per kind; `detail` says what
happened, in words written for the person who reads it (U2's refusal messages); `traceId` matches the server's log.

| Slug | Status | Title | Raised by | Typical `detail` |
|---|---|---|---|---|
| `scan-in-progress` | 409 | Scan in progress | `ScanInProgress` | A scan is already in progress |
| `not-ready` | 503 | Airlock is not ready | `NotReady` | NVD API key not configured; Syft not found at /opt/airlock/bin/syft |
| `invalid-target` | 400 | Invalid scan target | `InvalidTarget` | Target path does not exist |
| `too-many-artifacts` | 400 | Too many artifacts | `TooManyArtifacts` | At most 25 files per scan |
| `too-large` | 413 | Upload too large | `TooLarge`, and the server's own body limit | Uploads are limited to 2 GB per scan |
| `upload-stopped` | 409 | Upload stopped | `ReservationStopped` | Emergency Stop ended the upload before the scan started |
| `scan-not-found` | 404 | Scan not found | `ScanNotFound`, and an id that is not a scan id | Scan not found or no longer retained |
| `scan-not-finished` | 409 | Scan still running | `ScanNotFinished` | The scan is still running |
| `scan-already-finished` | 409 | Scan already finished | `ScanAlreadyFinished` | The scan has already finished |
| `scan-has-no-results` | 409 | Scan has no results | `ScanHasNoResults` | The scan ended without results |
| `invalid-request` | 400 | Invalid request | The multipart reader (WR-07) | Unknown form field 'files'; expected 'targetPath' or 'artifacts' |
| `unsupported-media-type` | 415 | Unsupported media type | WR-06 | Scans are submitted as multipart/form-data |
| `cross-site-request` | 403 | Cross-site request refused | WR-19 | Requests from other websites are not accepted |
| `not-found` | 404 | Not found | Routing, for an address under `/api` that is no endpoint | No API endpoint at this address |
| `method-not-allowed` | 405 | Method not allowed | Routing | This endpoint does not accept GET |
| `unexpected-error` | 500 | Unexpected error | Anything unexpected | Unexpected error — see the server log |

A request to a host name outside `AllowedHosts` never reaches the API: the host filter answers it with a plain 400
(WR-20).
