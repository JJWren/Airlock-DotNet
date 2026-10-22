# U2 Application — Business Rules

**Stage**: CONSTRUCTION — U2 Application — Functional Design (Comprehensive depth)

The Application rules (AR) decide how a scan is started, built, correlated, watched, stopped and kept. They use the
Domain rules (BR, U1) and reach the outside world only through ports. "Trace" names the requirement, decision, story or
S0 finding behind a rule; worked examples and the test map are in `business-logic-model.md`.

## Starting a scan

**AR-01 — Readiness.** A scan can be reserved only when an NVD API key is configured and the SBOM tool is available.
Otherwise the reservation is refused with `NotReady`, giving every reason that applies — "NVD API key not configured",
and the tool's own detail such as "Syft not found at /opt/airlock/bin/syft" — joined with "; ". Readiness is checked
before the slot. *Trace*: FR-02, FR-23; Q8; E04; defect 7.

**AR-02 — One active slot.** A reservation claims the server's single active slot atomically. While the slot is held —
by a reservation, with or without an upload in progress, or by a running scan — every other reservation is refused with
`ScanInProgress` ("A scan is already in progress"), and the holder is unaffected. The slot is held until the reservation
ends or the scan's cleanup finishes (AR-32). *Trace*: FR-02, FR-04; Q9; E04; defect 15.

**AR-03 — Reservation lifetime.** The scan id is allocated at reservation and kept by the scan. A reservation that ends
without starting — refused, abandoned by its client, or stopped (AR-28) — releases the slot, deletes its staging area
and records no scan. After a refusal, the reservation answers every later call with the same refusal.
*Trace*: services.md (lifecycle steps 1–3); E02.

**AR-04 — One kind of target.** A scan needs exactly one kind of target: a directory path, or at least one uploaded
artifact. Neither is refused with `InvalidTarget` "Choose a directory or upload at least one artifact"; both with
`InvalidTarget` "Choose a directory or upload artifacts, not both". A blank path counts as no path. The UI sends the
artifacts when any are staged, otherwise the path. *Trace*: FR-01, FR-02; Q-U2-4; E04.

**AR-05 — Directory targets.** The path must be text the Domain accepts — at most 4,096 characters, with no control or bidirectional formatting characters ("Target path is not valid") — and absolute by the host's rules ("Target path must be absolute"). It is normalized to a full
path, with `.` and `..` segments resolved, and that full path is what the scan records and Syft scans. It must name an
existing directory ("Target path does not exist", or "Target path is a file, not a directory"). When
`AllowedScanRoots` is set, the path with its symbolic links followed must lie within one of the roots, also with their
links followed, comparing whole path segments with the host's case rules ("Path is outside the allowed scan roots").
An empty `AllowedScanRoots` allows any directory. *Trace*: FR-02; E01, E13; decision 4.

**AR-06 — Upload limits.** A scan accepts at most `Limits.MaxArtifactsPerScan` files (25) and
`Limits.MaxBytesPerScan` bytes in total (2 GiB). The count is checked before a file is accepted: the 26th is refused
with `TooManyArtifacts` ("At most 25 files per scan"). The byte limit is enforced while each file streams in, so an
oversized upload stops at the limit with `TooLarge` ("Uploads are limited to 2 GB per scan"). A refused file is not
kept. *Trace*: FR-02; N3; E02.

**AR-07 — Artifact names.** Each file is stored under a sanitized plain file name: its last path segment, with
characters that are invalid on Windows or Linux replaced by `_`; an empty or dots-only result becomes `artifact`.
Nothing can be written outside the scan's staging area, so `../../evil.txt` is stored as `evil.txt`. Two files with the
same name are both kept and both scanned under that name. Whether a file is a manifest follows from its sanitized name
(BR-12). *Trace*: FR-03; E02; decision 5; defect 5.

## Building the SBOM

**AR-08 — Runs.** A directory target is scanned in one Syft run over the directory. An artifacts target is scanned in
one run over all its manifests together, each kept under its own name, when there are any; then in one run per other
artifact, in upload order. Runs execute one at a time. *Trace*: FR-05; D4; S0 S3; E01, E02.

**AR-09 — A failed run fails the scan.** When a run fails — a non-zero Syft exit, output that cannot be read, or the
15-minute run limit — the scan fails at once with that failure, whose reason names the directory or file, and the
remaining runs are not started. No result is produced, so a Syft error can never read as "0 packages, clean".
*Trace*: FR-06; E01; decision 1; defect 6.

**AR-10 — Runs without packages.** A run that succeeds but finds no packages adds a `NoPackagesIdentified` warning
naming its input — the file name for an artifact run, every manifest name for the manifests run, the path for a
directory target ("No packages identified in notes.docx"). The scan carries on. *Trace*: FR-06; E02.

**AR-11 — Container images.** Each run that Syft identified as a container image adds a
`DistributionBackportsNotConsidered` warning naming that file: versions are checked as the image reports them, so a fix
the distribution backported can still be listed. *Trace*: FR-06; S0 N13; E02.

**AR-12 — Package merge.** Packages from all runs are merged by identity — type, name ignoring case, version (BR-11).
The first occurrence's spelling is kept, and the candidate CPE lists of all occurrences are combined in run order before
ranking (BR-08). `totalPackages` is the number of merged packages. *Trace*: FR-07; D5; E02; decision 6; defect 13.

## Correlation

**AR-13 — Unknown versions.** A package whose version is unknown (BR-10) is Unverified with the reason "version
unknown" and is never looked up. *Trace*: FR-11; Q-S0-4; E06.

**AR-14 — CPE stage.** The package's ranked candidates (at most three, BR-08) are looked up one after another:

| Lookup answer | Next step |
|---|---|
| Records | CPE match on this candidate; the stage ends (AR-16) |
| No records — the CPE is unknown, malformed, or has no CVEs | The next candidate; after the last one, the product stage (AR-15) |
| Failed, after the catalog's retries | Unverified with that failure; later candidates and the product stage are not tried |

*Trace*: FR-08, FR-09, FR-11; Q-U2-1; S0 N1, N2; defects 8, 12.

**AR-15 — Product stage.** Reached only when no candidate returned records, including when a package has no candidates
at all. The product name comes from BR-09:

| Product name | Lookup answer | Result |
|---|---|---|
| Withheld — too generic or too short | not asked | Clean with no match method, listed in the scan's `NotProductMatched` warning |
| A name | Records | Product match (AR-16) |
| A name | No records | Clean, with the Product match method |
| A name | Failed, after the catalog's retries | Unverified with that failure |

*Trace*: FR-10, FR-11; Q-S0-2, Q-U2-3; ADR-0006; defects 1, 10, 12.

**AR-16 — Findings from a match.** The matched records' findings are selected by BR-04, BR-05 and BR-07. With at least
one finding the package is vulnerable, with its match method; with none it is clean, with that method. No product match
follows a CPE match. The matched CVEs that NVD has not scored go into the package's `CvesWithoutSeverity` warning
(BR-05), whatever its verdict. *Trace*: FR-09, FR-10, FR-12; E05; defect 12.

**AR-17 — Parallel correlation.** Packages are correlated in parallel, at most `Correlation.MaxParallelism` at a time,
and every NVD request shares the application-wide rate limit. A package's result never depends on the order or timing of
the others, except through AR-18. *Trace*: FR-08; NFR-04, NFR-05.

**AR-18 — NVD outage.** The scan counts failed lookups in a row, in the order they complete; any lookup that answers,
with records or without, resets the count. When the count reaches `Correlation.OutageThreshold`, the scan stops calling
NVD: lookups not yet sent are not sent, lookups already in flight finish, and every package not yet decided is
Unverified with the reason "NVD unavailable — not checked after n failed lookups in a row". The scan then finishes as
Incomplete. *Trace*: FR-11; Q-U2-2; ADR-0002.

## Results

**AR-19 — Warnings.** A finished scan's warnings come in this order: the run warnings (AR-10, AR-11) in run order; then
one `NotProductMatched` warning when any package was not product-matched, listing up to 20 of them as "name version" in
package order and then "and k more"; then one `CvesWithoutSeverity` warning per package with unscored CVEs, in package
order, naming the package and the CVE ids. Package order is name, then version, then type, ignoring case.
*Trace*: FR-06, FR-12; Q-U1-1, Q-U2-3.

**AR-20 — Summary and status.** After correlation the scan is finished with its total, vulnerable and unverified
packages (BR-13): Completed when no package is unverified, Incomplete otherwise (BR-14). Clean packages, including those
not product-matched, are counted but not listed. *Trace*: FR-14, FR-15; ADR-0002.

## Watching a scan

**AR-21 — One writer, every change published.** The pipeline is the only writer of its scan. Each transition is recorded
in the scan registry first and then published on the scan's event hub, so a subscriber never sees an event that the
registry does not yet reflect. Readers receive immutable versions. *Trace*: FR-17; services.md (concurrency).

**AR-22 — Progress.** Entering correlation records 0 of total; each decided package — including the version-unknown
and outage-Unverified ones — adds one; the last brings it to the total. Every change publishes `PackagesCorrelated`.
*Trace*: FR-17; E08.

**AR-23 — Snapshot first, finished last.** A subscription to a scan yields a `ScanSnapshot` of its latest version, then
its later events in order, and ends with `ScanFinished`. Subscribing to a finished scan yields its snapshot and
`ScanFinished`; to an unknown or pruned scan, nothing. A slow subscriber may lose its oldest log and progress events,
never the snapshot, a phase change or the finished event. *Trace*: FR-17; E03; decision 9.

**AR-24 — Scan-scoped log lines.** Every log entry written inside a scan's pipeline carries the scan id: it is
published on that scan's hub as `ScanLogged` and also appears in the global Live Log. *Trace*: FR-17, FR-18.

**AR-25 — Text from outside Airlock.** Text that comes from outside Airlock — Syft's error output, NVD error responses —
is reduced to one line, stripped of control characters and cut to 512 characters before it appears in a log line, a
failure reason or an Unverified reason. *Trace*: NFR-03; FR-06.

**AR-26 — Active slot changes.** Every change of the active slot — reserved, receiving an upload, running, released — is
pushed to every watcher, so each tab enables Execute and Emergency Stop correctly. *Trace*: FR-04, FR-25.

## Stopping a scan

**AR-27 — Cancel a scan.** Cancelling a running scan signals its cancellation and waits, for at most
`Timeouts.CancelGracePeriod`, for the pipeline to record Cancelled ("Cancelled by request"); the use case returns the
scan as it then stands. An unknown or pruned scan is refused with `ScanNotFound`, a finished one with
`ScanAlreadyFinished`. Cancelling kills the Syft process tree and abandons pending NVD lookups; no partial result is
kept (BR-16). *Trace*: FR-16; E09; defect 3.

**AR-28 — Emergency Stop.** Emergency Stop cancels whatever holds the active slot, whoever started it. A running scan
ends Cancelled ("Emergency Stop"). A reservation receiving an upload ends at once: its upload is refused with
`ReservationStopped` and nothing is recorded (AR-03). The result is TERMINATED with the scan id, or IDLE when nothing
held the slot. *Trace*: FR-16; Q9; E09; decision 3; defect 3.

**AR-29 — Time limit.** A scan still running `Timeouts.Scan` (60 minutes) after it started fails with `ScanTimedOut`,
"Scan exceeded the 60-minute limit". Syft is killed and lookups are abandoned as on cancel; no partial result is kept.
*Trace*: FR-15; N3; E06.

**AR-30 — Server shutdown.** Stopping the host cancels every running scan with "Server shutting down" and ends every
open reservation. *Trace*: services.md (cancellation).

**AR-31 — Unexpected errors.** An exception the pipeline does not expect fails the scan with `Unexpected`, "Unexpected
error — see the server log". The full error goes to the console log only. *Trace*: services.md (outcomes).

**AR-32 — Cleanup on every exit.** However a scan ends, in this order: its final status is recorded and `ScanFinished`
published; the event hub completes; the staging area is deleted; the slot is released; retention pruning runs.
*Trace*: FR-03, FR-04, FR-19; E02.

## After a scan

**AR-33 — Retention.** Pruning removes the finished scans whose finish time is more than `Retention.Duration`
(24 hours) ago, then, while more than `Retention.MaxScans` (20) finished scans remain, the one that finished earliest.
A running scan is never pruned. Pruning runs after every scan ends and before every lookup by id.
*Trace*: FR-19; N1; E03.

**AR-34 — Report export.** An unknown or pruned scan is refused with `ScanNotFound`; a running one with
`ScanNotFinished`; a Failed or Cancelled one with `ScanHasNoResults` ("The scan ended without results"). A Completed or
Incomplete scan's report is the PDF of FR-21, named `airlock_audit_<finish time in UTC as yyyy-MM-ddTHH-mm-ss-fffZ>.pdf`
like the original's export, with the content type `application/pdf`. *Trace*: FR-21; E11; decision 8.

**AR-35 — Health.** Two checks: "Syft", passing when the SBOM tool is available, with its version as the detail and
otherwise its reason; and "NVD API key", passing when a key is configured. The status is ONLINE when both pass and
DEGRADED otherwise. Health never calls NVD. *Trace*: FR-23; N6; E12.
