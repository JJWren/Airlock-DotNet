# U3 Infrastructure — Business Rules

**Stage**: CONSTRUCTION — U3 Infrastructure — Functional Design (Comprehensive depth)

The Infrastructure rules (IR) say how each adapter meets its port. They build only valid Domain values and leave every
decision about a scan to the Application (AR rules, U2). "Trace" names the requirement, decision, story or S0 finding
behind a rule; worked examples and the test map are in `business-logic-model.md`.

## Syft

**IR-01 — Invocation.** A run is `<syft> scan <source> -o json -q`, started without a shell, with the arguments as a
list and `SYFT_CHECK_FOR_APP_UPDATE=false`. A directory source is `dir:<path>`; an artifact is its path alone, so Syft
detects archives and container image archives itself. Syft's temporary files go to a folder of the run's own under
`Staging:Root`, deleted after the run, and its environment does not carry the NVD key.
*Trace*: FR-05; S0 S7, S9, S13; decision 5; U3 NFR Design.

**IR-02 — Run outcomes.**

| What happened | Answer |
|---|---|
| Exit 0 and readable output | `SbomProduced` (IR-03) |
| A non-zero exit | `SbomFailed`, `SyftFailed`: "Syft exited with code n: …" with the end of its error output, cleaned (U2 AR-25) |
| Still running at `Timeouts.SyftRun` | The process tree is killed; `SbomFailed`, `SyftTimedOut`: "Syft did not finish within 15 minutes" |
| Output that is not Syft JSON, or a package Airlock cannot represent | `SbomFailed`, `SyftOutputUnreadable`: "Syft's output could not be read: …" or "Syft's output holds a package Airlock cannot represent: …" |
| The binary cannot be started | `SbomFailed`, `SyftFailed`: "Syft could not be started: …" |
| The scan's token is cancelled | The process tree is killed; `OperationCanceledException` |

Every failure names its input: an artifact run's failure carries the artifact's name as its input, and the Application
names a directory run's input — the target path, or the manifests' names (U2 AR-09) — so no reason shows a staging
path. *Trace*: FR-06, FR-16; Q-U3-3; defects 3, 6; U3 Code Generation.

**IR-03 — Mapping the output.** Each artifact becomes a `Package` (type, name, version as Syft reports them) with its
candidate CPEs in Syft's order, `nvd-cpe-dictionary` mapped to `Dictionary` and every other source to `Generated`.
`source.type` `image` sets `IsContainerImage`. When the Domain rejects a value — control characters, or over its
limits — the whole output is unreadable (IR-02, Q-U3-3); nothing is cleaned or dropped silently.
*Trace*: FR-05, FR-06; S0 S12; U1-NFR-04; Q-U3-3.

**IR-04 — NuGet packages.** For an artifact whose name ends with `.nupkg`, the generator reads the `.nuspec` at the
root of the archive and adds the package it names — type `nuget`, its `id` and `version` — without candidate CPEs,
alongside the assemblies Syft found. A missing or unreadable `.nuspec` adds nothing and is logged.
*Trace*: FR-05; Q-S0-3; S0 S6; E02.

**IR-05 — Tool status.** Syft is available when the resolved binary exists and `syft version` exits 0 within
10 seconds; the version comes from its output. The status is cached for `Syft:StatusCacheDuration`. Details read
"Syft 1.55.0 at <path>", "Syft not found at <path>" or "Syft at <path> did not run: …".
*Trace*: FR-23; N6; E04, E12.

**IR-06 — Finding Syft.** The configured `Syft:BinaryPath`, relative paths taken from the application's folder;
without one, `syft` on the path, then `./bin/syft` (`syft.exe` on Windows) next to the application. *Trace*: decision 7;
NFR-06.

## NVD

**IR-07 — Requests.** A CPE lookup is `GET <BaseUrl>?cpeName=<cpe>&noRejected`; a product match is
`GET <BaseUrl>?virtualMatchString=cpe:2.3:a:*:<product>:<version>:*:*:*:*:*:*:*&noRejected`, with the product and
version escaped as CPE 2.3 requires. The key travels in the `apiKey` header. Results come in pages of 2,000:
`startIndex` advances until `totalResults` is reached. Every request passes the application-wide limiter
(`Nvd:RequestsPerWindow` per `Nvd:Window`). *Trace*: FR-09, FR-10; S0 N10, N12; decisions 2, 4; NFR-04; defect 9.

**IR-08 — Answers.**

| NVD's answer | Lookup result |
|---|---|
| 200 with `totalResults` 0 | `CatalogNoRecords` — also for a CPE NVD does not know (S0 N1) |
| 200 with records | `CatalogFound` with every page's records |
| 404 for a malformed `cpeName` | `CatalogNoRecords`, logged as a warning (S0 N2) |
| 429, 403, 5xx, a timeout or a network error | Retried with backoff; after the last attempt `CatalogFailed` ("NVD returned 503", "NVD did not answer in time", …) |
| Any other status, or a body that cannot be read | `CatalogFailed` without a retry |

A `Retry-After` of 0 is no usable delay, so the retries use their own minimum backoff (S0 N9).
*Trace*: FR-09, FR-11; S0 N1, N2, N9; NFR-04; ADR-0002.

**IR-09 — Mapping records.** Each record becomes a `Cve` as the field table in `domain-entities.md` says: its id, its
English description (control characters except line feeds and tabs removed, cut to 16,384 characters), one
`CvssScore` per metric entry with its version and source, and the affected products from its vulnerable CPE matches.
A record the Domain rejects makes the whole lookup `CatalogFailed` — the package is then Unverified rather than judged
on part of its records (decision 3). A record still marked `Rejected` is dropped (decision 2).
*Trace*: FR-12, FR-13; Q-S0-1; S0 N5, N6, N7; defect 11.

**IR-10 — Versions sent to NVD.** Product matches send `PackageVersion.ForNvd(package)`; CPE lookups send
`CandidateCpe.ForNvd(package)`, the candidate with its version part rewritten the same way, so a Go module's `v0.23.3`
is asked for as `0.23.3` both ways. The match keeps the CPE as asked. *Trace*: FR-10; decision 1; S0 design notes.

## Staging

**IR-11 — Layout.** Each scan's area is `<Staging:Root>/<scan id>/`, with `manifests/<n>/<name>` and
`artifacts/<n>/<name>`, where `n` is the file's position in the upload. On Linux the folders are created readable by
the Airlock user only. Every folder Airlock creates under the root carries a `.airlock` marker file.
*Trace*: FR-03, FR-05; decision 6; U3 NFR Design.

**IR-12 — Storing a file.** The name arrives sanitized (U2 AR-07); the area still checks that the resolved path lies
inside its own folder and refuses anything else. The content is copied with a byte count; once it passes the bytes
left for the scan, the partial file and its folder are deleted and the answer is `ArtifactTooLarge`. Python wheels and
NuGet packages are stored with an added `.zip` suffix; the `Artifact` keeps the uploaded name. *Trace*: FR-02, FR-03,
FR-05; Q-S0-3; defect 5.

**IR-13 — Deleting.** Disposing an area deletes its folder and everything in it. If a file is still locked — Syft
being killed on Windows — the deletion is retried a few times briefly before the failure is reported to the
Application, which logs it (U2 AR-32); read-only files and folders are made writable and the deletion tried again at
once, since they need no waiting. At startup,
marked folders left by an earlier run are deleted and nothing unmarked is touched, so one staging root serves one
Airlock. Before it sweeps, Airlock takes an exclusive lock on `<Staging:Root>/.airlock-lock` and holds it while it
runs, so a second Airlock on the same root stops at startup with "Another Airlock is using the staging root <root>"
instead of deleting the first one's folders (U4-NFR-09, added in U4 NFR Design). *Trace*: FR-03; E02; U3 NFR Design.

## Scan registry

**IR-14 — Store.** The registry holds the latest version of each scan in memory. `Update` replaces it atomically;
`List` returns a snapshot; `Remove` drops one. Retention is the Application's decision (U2 AR-33). Nothing survives a
restart. *Trace*: FR-19; D1.

## Live Log

**IR-15 — Capture.** The Live Log provider captures entries from Airlock's own categories (`Airlock.*`) at Debug and
above. An entry holds its time in UTC, level, category, message — the formatted message only, never exception details
— and the scan id from the `ScanId` scope entry, if any. *Trace*: FR-18; NFR-03; U2 AR-24.

**IR-16 — Routing.** Every entry goes to the broadcaster; an entry with a scan id also goes to the Application's
`ScanLogRouter`, which puts it on that scan's event stream. *Trace*: FR-17, FR-18; U2 refinement 3.

**IR-17 — Subscribers.** A new subscriber first receives the last 200 entries, oldest first, then live entries. Each
subscriber has its own buffer of 1,000 entries that drops the oldest when full; publishing never waits, and every
subscriber receives every entry it can hold. *Trace*: FR-18; Q-U3-1; defect 4.

**IR-18 — Console independence.** `LOG_LEVEL` sets the console's minimum level only; the Live Log always captures
Airlock's Debug entries. *Trace*: E13; NFR-07.

## Host file system

**IR-19 — Host rules.** Absolute means rooted on the host (`/uploads` counts on Windows too). Full paths resolve `.`
and `..`. Following links walks the path segment by segment and replaces each symbolic link with its target, giving up
after 40 links. A path lies within a root when, segment by segment, it starts with it — ignoring case on Windows and
macOS, exactly on Linux. *Trace*: U2 AR-05; NFR-06.

## PDF Scan Report

**IR-20 — Layout.** A4 portrait. Title "Airlock Scan Report"; the scan's id, target, status, start and finish in UTC
(labelled), and duration; the summary table (total, vulnerable, clean and unverified packages; critical and high
findings); the findings table (package, version, match method, CVE id with its NVD link, severity in words, score with
its CVSS version, description); the Unverified packages with their reasons; the warnings; and on every page a footer
with the page number and when the report was rendered. Severity is always written out, never shown by colour alone.
*Trace*: FR-21; Q-U3-2; NFR-12; E11.

**IR-21 — Licence and fonts.** QuestPDF runs under its Community licence, set once at startup. The report uses
QuestPDF's built-in fonts and no emoji. *Trace*: Q13; NFR-10.

## Configuration

**IR-22 — Options.** `Nvd:BaseUrl` must be an absolute https URL; `Nvd:RequestsPerWindow` and `Nvd:Window` positive;
`Staging:Root` a full path; `Syft:BinaryPath` not blank when given. A missing NVD key is not an error — Airlock starts
DEGRADED. An invalid value stops startup with a message naming the option. *Trace*: FR-02, FR-23; E13.

**IR-23 — The original variable names.** `NVD_API_KEY` and `SYFT_BINARY_PATH` map onto `Airlock:Nvd:ApiKey` and
`Airlock:Syft:BinaryPath` through a configuration extension the host (U4) adds; `LOG_LEVEL` sets the console level and
accepts the original's names (`DEBUG`, `INFO`, `WARNING`, `ERROR`, `CRITICAL`) as well as .NET's, in any case.
*Trace*: requirements §4 (configuration); Q17.

**IR-24 — The key stays secret.** The key appears only in the `apiKey` header — never in a URL, a log line, an
exception message, a health detail or Syft's environment. The HTTP client factory's own logging is off for the NVD
client: its formatted messages redact headers, but the structured state it gives every logging provider holds the raw
values. Headers stay redacted for any logger added later. *Trace*: NFR-03; E13; decision 8; U3 Code Generation.

## Install scripts and architecture

**IR-25 — Install scripts.** `scripts/install-tools.ps1` and `scripts/install-tools.sh` download Syft v1.55.0 for the
machine's operating system and architecture from the project's GitHub releases, verify it against the release's
SHA-256 checksums, and put the binary in `./bin`. They do nothing when `./bin` already holds v1.55.0, and stop with a
message on any failure. *Trace*: NFR-06; Q18.

**IR-26 — Architecture rules 3 and 4.** Infrastructure never depends on Web (rule 3), and the ports are implemented
here and only here (rule 4). *Trace*: NFR-02; component-dependency.md.
