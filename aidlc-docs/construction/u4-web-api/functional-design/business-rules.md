# U4 Web API — Business Rules

**Stage**: CONSTRUCTION — U4 Web API — Functional Design (Standard depth)

The Web API rules (WR) say how `/api/v2` carries the Application's use cases over HTTP. They decide nothing about a
scan: every decision stays with the Application (AR rules, U2), and the endpoints only read requests, call use cases
and write answers. "Trace" names the requirement, decision, story or plan decision behind a rule; the types are in
`domain-entities.md`, worked requests and the test map in `business-logic-model.md`.

## The contract

**WR-01 — Endpoints.**

| Method and path | Use case | Answers |
|---|---|---|
| `POST /api/v2/scans` | `StartScan`, then the `ScanReservation` it returns | 202 `ScanAcceptedDto`; 400, 403, 409, 413, 415, 503 |
| `GET /api/v2/scans/{id}` | `GetScan` | 200 `ScanDto`; 404 |
| `GET /api/v2/scans/{id}/events` | `GetScan`, then `WatchScan` | 200 event stream; 404 |
| `GET /api/v2/scans/{id}/report.pdf` | `RenderScanReport` | 200 PDF; 404, 409 |
| `POST /api/v2/scans/{id}/cancel` | `CancelScan` | 200 or 202 `ScanDto`; 403, 404, 409 |
| `POST /api/v2/system/stop` | `EmergencyStop` | 200 `EmergencyStopDto`; 403 |
| `GET /api/v2/system/health` | `GetSystemHealth` | 200 `SystemHealthDto` |
| `GET /api/v2/logs/stream` | `WatchLiveLog` | 200 event stream |

An `{id}` is a scan id in its 36-character form, in either case. Anything else — another GUID format, the empty GUID,
text that is no GUID — answers 404 `scan-not-found`, like a scan that is no longer retained. Only the submission reads
a body; a body sent to cancel or stop is ignored. Every endpoint can also answer 500 (WR-05).
*Trace*: FR-20; N1; ADR-0003; component-methods.md.

**WR-02 — JSON.** Properties are camelCase and named after the glossary; every enumeration value is written in
capitals with underscores (Q-U4-1); times are UTC in RFC 3339 with milliseconds and `Z`; every property is present,
`null` when it does not apply and `[]` when a list is empty. HTTP bodies are `application/json; charset=utf-8`, indented
by two spaces like the original's Export JSON, with text written as it is, escaping only what JSON requires. Every
answer under `/api` carries `Cache-Control: no-store`, so no browser or proxy shows a stale status.
*Trace*: FR-20, FR-22; Q10, Q15, Q-U4-1; plan decisions 1 to 3, 14 and 18; the original's
`JSON.stringify(result, null, 2)`.

**WR-03 — Refusals and their statuses.** Each refusal of a use case answers with its own status and problem kind;
`detail` is the refusal's message, unchanged.

| `RefusalCode` | Status | Problem |
|---|---|---|
| `ScanInProgress` | 409 | `scan-in-progress` |
| `NotReady` | 503 | `not-ready` |
| `InvalidTarget` | 400 | `invalid-target` |
| `TooManyArtifacts` | 400 | `too-many-artifacts` |
| `TooLarge` | 413 | `too-large` |
| `ReservationStopped` | 409 | `upload-stopped` |
| `ScanNotFound` | 404 | `scan-not-found` |
| `ScanNotFinished` | 409 | `scan-not-finished` |
| `ScanAlreadyFinished` | 409 | `scan-already-finished` |
| `ScanHasNoResults` | 409 | `scan-has-no-results` |

*Trace*: services.md (outcomes and errors); D6; Q-U4-2; E03, E04.

**WR-04 — Every error is a problem.** Every error answer under `/api` is an `application/problem+json` body of the
catalogue in `domain-entities.md`, with the members `type`, `title`, `status`, `detail` and `traceId` and no others.
That includes the answers given before an endpoint runs: an address under `/api` that is no endpoint (404
`not-found`), a method an endpoint does not accept (405 `method-not-allowed`, with an `Allow` header), and a body cut
off by the server's own size limit (413 `too-large`). `type` is the problem's URL in the repository's
`docs/api/problems.md` (Q-U4-2). *Trace*: FR-20; D6; Q-U4-2; plan decision 13.

**WR-05 — Nothing internal in an answer.** An exception nobody expected answers 500 `unexpected-error`, "Unexpected
error — see the server log"; the exception itself goes to the console log only (U2 AR-31). No answer carries
exception text, a stack trace, a staging path, a configuration value or the NVD key. The two exceptions are a
directory the client asked to scan and the Syft location in a readiness reason, which the stories require (E01, E04,
E12). *Trace*: NFR-03; services.md (outcomes); unit-of-work.md (U4 tests).

## Submitting a scan

**WR-06 — Order of checks.** `POST /api/v2/scans` runs these steps in order and stops at the first that fails:

1. Requests from other websites are refused (WR-19): 403.
2. The content type must be `multipart/form-data`: anything else is 415 `unsupported-media-type`, "Scans are submitted
   as multipart/form-data"; a multipart type without a boundary is 400 `invalid-request`.
3. `StartScan.ReserveAsync` claims the active slot before any of the body is read: 503 `not-ready` with every reason
   that applies, or 409 `scan-in-progress` (U2 AR-01, AR-02). A client that sent `Expect: 100-continue`, as curl does
   for large uploads, receives the refusal before it sends its files.
4. The parts are read as WR-07 says, each artifact streamed into the reservation.
5. `ScanReservation.StartAsync` checks the target and starts the scan: 400 `invalid-target`, or 409 `upload-stopped`
   when Emergency Stop came first.
6. 202 (WR-09).

A refusal after step 3 disposes the reservation: the slot is free again, the staging area is deleted and no scan is
recorded (U2 AR-03). *Trace*: FR-02; services.md (lifecycle steps 1 to 3); E02, E04; plan decisions 8 and 9.

**WR-07 — The parts.** The part names are compared ignoring case.

- **`targetPath`**, at most once. Its value is read as UTF-8, at most 16 KiB; a longer value, or one that is not
  UTF-8, is refused with `invalid-target`, "Target path is not valid". A second `targetPath` is 400 `invalid-request`,
  "targetPath may be given only once".
- **`artifacts`**, or **`artifacts[]`**, any number of times. Each part streams into `ScanReservation.AddArtifactAsync`
  under the file name it carries, which the Application sanitizes (U2 AR-07); the limits apply while it streams
  (WR-08). A part without a file name is 400 `invalid-request`, "Each artifact needs a file name" — except an empty
  part with an empty file name, which is what a browser sends for an empty file input: it is skipped.
- **Anything else** is 400 `invalid-request`: "Unknown form field 'files'; expected 'targetPath' or 'artifacts'". The
  name comes from the client, so it is shown on one line, without control characters and cut to 64 characters, the
  way U2 AR-25 treats outside text. A script written for the original's `target_path` and `files[]` learns what to
  change.
- **Both kinds of target**: as soon as a non-blank `targetPath` and an artifact have both arrived, the request is
  refused with `invalid-target`, "Choose a directory or upload artifacts, not both" (U2 AR-04), and the rest of the
  body is not read.
- **Neither**: no `targetPath` and no artifact is refused by `StartAsync` with `invalid-target`, "Choose a directory or
  upload at least one artifact".
- **A broken body** — a missing final boundary, a truncated part, part headers beyond the reader's limits — is 400
  `invalid-request`, "The request body is not valid multipart/form-data".
- **A client that disconnects**, or a request that is aborted, stops the reading; the reservation is disposed and no
  answer is sent.

*Trace*: FR-02, FR-03, FR-20; Q-U2-4; E02; plan decision 7.

**WR-08 — Limits.** The 26th artifact is refused with 400 `too-many-artifacts` before its content is read; an upload
that passes `Limits.MaxBytesPerScan` in total is refused with 413 `too-large` as soon as it does, and the reading stops
(U2 AR-06). The server's own request-body limit lies above `Limits.MaxBytesPerScan` by an allowance for the multipart
framing, which U4 NFR Design fixes, so these refusals normally come first; a body that reaches the server's limit is
answered with `too-large` as well. A client that is still sending when a refusal comes may see the connection close
before it reads the answer — common with HTTP/1.1 clients and browsers — so the UI checks the count and the total size
before it uploads (U5). *Trace*: FR-02; N3; NFR-05; E02; U2 NFR Design (`Limits.MaxBytesPerScan`).

**WR-09 — Accepted.** 202 Accepted with `Location: /api/v2/scans/{id}` and `ScanAcceptedDto` (`RUNNING`). From here
the scan no longer depends on the request: a client that disconnects does not stop it.
*Trace*: FR-20; E03; services.md (cancellation); plan decision 16; defect 14.

## Reading a scan

**WR-10 — The scan resource.** `GET /api/v2/scans/{id}` answers 200 with the scan's latest version (`ScanDto`), or 404
`scan-not-found` for an unknown, pruned or malformed id; pruning runs first (U2 AR-33). While `RUNNING` the resource
holds the status, the phase, the progress once correlation has begun, the target, the start time and any warnings so
far. A `COMPLETED` or `INCOMPLETE` scan adds the finish time, the duration, the summary and both lists; a `FAILED` scan
its failure; a `CANCELLED` scan its cancellation reason. *Trace*: FR-19, FR-20; N1; E03.

**WR-11 — The JSON export.** Reports' Export JSON is a link to `GET /api/v2/scans/{id}`, so the saved file and the API
body are the same bytes; the UI names the file `airlock_audit_<finish time>.json`, like the original's (U5).
*Trace*: FR-22; ADR-0001; E11.

## Following a scan

**WR-12 — A scan's events.** `GET /api/v2/scans/{id}/events` answers 404 `scan-not-found` for an unknown, pruned or
malformed id before any stream begins. Otherwise it answers 200 `text/event-stream` with `WatchScan`'s events:
`snapshot` first, then `phase`, `progress` and `log` in the order they happen, and `finished` last, after which the
server ends the response. A finished scan's stream is its `snapshot` and `finished` at once. A stream that ends
without `finished` means the scan was pruned as the stream began. *Trace*: FR-17; E03; U2 AR-23.

**WR-13 — Event delivery.** Each event is an `event:` line naming it and one `data:` line of compact JSON. There are no
`id:` or `retry:` fields: a client that reconnects receives a fresh snapshot instead of a replay. A stream that has
been silent for 15 seconds receives a `: keep-alive` comment line, which clients ignore (U4-NFR-05). A browser's
`EventSource` reconnects by itself when a stream ends, so it must be closed on `finished`. A slow client may lose its
oldest `log` and `progress` events, never `snapshot`, `phase` or `finished` (U2 AR-23). A client that closes its
stream changes nothing for the scan. *Trace*: FR-17; E03, E08; plan decision 12; U4-NFR-05.

## The report

**WR-14 — The PDF.** `GET /api/v2/scans/{id}/report.pdf` answers 200 `application/pdf` with
`Content-Disposition: attachment; filename="airlock_audit_<finish time>.pdf"` (U2 AR-34); 404 `scan-not-found`; 409
`scan-not-finished` while the scan runs; 409 `scan-has-no-results` for a `FAILED` or `CANCELLED` scan. Reports' Export
PDF links here, so the UI and the API give the same report from one implementation (E11).
*Trace*: FR-21; ADR-0001; E11; defect 18 (the original's server PDF endpoint, which its UI never used).

## Stopping a scan

**WR-15 — Cancel.** `POST /api/v2/scans/{id}/cancel` answers 404 `scan-not-found` or 409 `scan-already-finished` (U2
AR-27). Otherwise `CancelScan` signals the cancellation and waits up to `Timeouts.CancelGracePeriod` (5 seconds). When
the scan has finished by then — `CANCELLED` with "Cancelled by request", or another status if it finished first — the
answer is 200 with its `ScanDto`; when it is still running, 202 with its `ScanDto` and a `Location`, and the scan
records `CANCELLED` shortly after. *Trace*: FR-16; E09; plan decision 10.

**WR-16 — Emergency Stop.** `POST /api/v2/system/stop` cancels whatever holds the active slot, whoever started it, and
answers 200 at once: `TERMINATED` with the id of what held the slot, or `IDLE` with `null` (U2 AR-28). A running scan
then ends `CANCELLED` with "Emergency Stop"; an upload in progress is answered 409 `upload-stopped`, and its id never
becomes a scan. *Trace*: FR-16; Q9; E09; defect 3.

## Health and the Live Log

**WR-17 — Health.** `GET /api/v2/system/health` answers 200 with `SystemHealthDto` whether Airlock is `ONLINE` or
`DEGRADED`; the checks are `GetSystemHealth`'s, reported as they are (U2 AR-35; U3's constraint on U4). It never calls
NVD, and Syft's status is cached for `Airlock:Syft:StatusCacheDuration` (30 seconds). `version` is Airlock's version,
2.0.0, as the original reported its own. *Trace*: FR-23; N6; E12; plan decisions 11 and 17.

**WR-18 — The Live Log stream.** `GET /api/v2/logs/stream` answers 200 `text/event-stream` with `log` events: the last
200 lines first, then every line as it is written, each with its `scanId` (U3 IR-17). The stream never ends by itself;
it ends when the client closes it or the server stops. A slow client loses its oldest lines beyond the 1,000 it may
hold. *Trace*: FR-18; Q-U3-1; E08; defect 4.

## Requests from other websites

**WR-19 — Cross-site requests (Q-U4-3).** The three `POST` endpoints refuse with 403 `cross-site-request`, before
anything else happens, a request that

- carries `Sec-Fetch-Site` with any value but `same-origin` or `none` — `same-site` included, which is what another
  port on the same host gets; or
- carries no `Sec-Fetch-Site` but an `Origin` whose host and port differ from the request's `Host`, `Origin: null`
  included.

A request with neither header — curl, CI clients, .NET's `HttpClient` — is accepted, and the UI's own upload is
`same-origin`. The `GET` endpoints are not checked: they change nothing, and no other site can read their answers,
because Airlock sends no CORS headers. *Trace*: NFR-03; Q-U4-3; ADR-0007.

**WR-20 — Host names (Q-U4-3).** Airlock answers only requests whose `Host` is in `AllowedHosts`: by default
`localhost`, `127.0.0.1` and `[::1]`, on any port. An operator adds the names clients use — `localhost;airlock` for a
CI service called `airlock` — or sets `*` to answer every name and accept the DNS rebinding risk. The rule covers the
whole application, API, UI and docs alike, and comes before every other: any other name gets the host filter's plain
400. *Trace*: NFR-03; Q-U4-3; ADR-0007.

**WR-21 — Antiforgery.** The API takes no antiforgery token (NFR-03): WR-19 protects its `POST` endpoints instead. The
Blazor UI keeps antiforgery for its own forms (U5). *Trace*: NFR-03.

## Documentation and composition

**WR-22 — The OpenAPI document.** `/openapi/v2.json` describes every endpoint of WR-01: its summary, its parameters,
the multipart body (`targetPath`, and `artifacts` as binary files), every answer's status and schema — ProblemDetails
for errors — and the two streams as `text/event-stream` with their event names. Enumerations list their values as
WR-02 writes them, and schemas are named after the glossary. Scalar renders the document at `/docs`. Both are served
in every environment, as the original served its docs at `/docs`. *Trace*: FR-20; Q17; plan decision 15;
unit-of-work.md (U4 tests).

**WR-23 — Composition.** `AirlockHost`, the composition root that `Program` calls, is the only Web code that names
Infrastructure types (architecture rule 5; it became its own class in Code Generation, because the architecture tests
cannot read the method the compiler generates for `Program`'s top-level statements). It adds
the original's variable names after the JSON configuration files (`AddAirlockEnvironmentVariables`), binds
`AirlockOptions` from `Airlock`, and calls `AddAirlockApplication` and `AddAirlockInfrastructure` (the U2 and U3
constraints). An invalid option stops startup with a message naming it; a missing NVD key or Syft does not, and Airlock
starts `DEGRADED` (E13). One Airlock runs per staging root (U3). Endpoints call use cases only. The configuration
sources and their precedence, the ports and the server limits are U4 Infrastructure Design.
*Trace*: E13; NFR-02; component-dependency.md (rule 5); U2 and U3 code generation summaries.
