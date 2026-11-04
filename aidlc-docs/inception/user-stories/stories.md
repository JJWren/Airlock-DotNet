# User Stories — Airlock .NET Rebuild

**Format** (story plan answers): coarse epics (Q7-B) organized by feature area (Q2-A), each with one user-story
sentence (Q3-A), its capabilities, Gherkin acceptance criteria, a MoSCoW priority (Q5-A) and full traceability
(Q6-A). Only user-visible NFRs appear (Q4-A); NFR-02, NFR-08 and NFR-09 stay in `requirements.md`.

**Priority rules**: Must = parity features, fail-closed behaviour, security hardening and the approved changes;
Should = other fixes; Could = cosmetic. An epic carries the highest priority among its items; an item is tagged only
when its priority is lower than its epic's. Priorities order the work; all approved scope stays in.

**Personas**: Vetting Analyst, Pipeline Integrator, Platform Operator (see `personas.md`).
**Defect numbers** refer to `../reverse-engineering/code-quality-assessment.md`.

---

## Feature area 1 — Scan intake

### E01 — Scan a directory on the Airlock host
**Persona**: Vetting Analyst (Pipeline Integrator through the API) · **Priority**: Must

As a Vetting Analyst, I want to scan a directory that already sits on the Airlock host, so that I can vet a project or
an unpacked delivery without uploading it.

- Local Directory Path field, pre-filled with `/uploads`.
- One Syft `dir:` run over the directory; packages de-duplicated by (type, name, version).
- A missing path, or one outside `AllowedScanRoots` when that setting is used, is refused before the scan starts.
- A Syft error fails the scan and names the path — never "0 packages, clean".
- The same capability through `POST /api/v2/scans` with `targetPath`.

```gherkin
Scenario: Scan an existing directory
  Given Airlock is ONLINE and no scan is active
  And the directory "/uploads/acme-app" exists on the Airlock host
  When I enter "/uploads/acme-app" as the Local Directory Path and execute the scan
  Then a scan of that directory starts
  And its SBOM is built by one Syft run over the directory

Scenario: A mistyped path is refused, not reported clean
  Given no directory "/uploads/acme-ap" exists
  When I execute a scan of "/uploads/acme-ap"
  Then the scan is refused with the reason "Target path does not exist"
  And no result claims that the target is clean

Scenario: A path outside the allowed scan roots is refused
  Given AllowedScanRoots is set to "/uploads"
  When I execute a scan of "/etc"
  Then the scan is refused with the reason "Path is outside the allowed scan roots"

Scenario: Syft fails on the directory
  Given Syft exits with an error while scanning "/uploads/acme-app"
  When the scan ends
  Then its status is Failed
  And the failure reason names "/uploads/acme-app"
```

**Traceability**: FR-01, FR-02, FR-05, FR-06, FR-07, FR-25 · Q4, Q6, D4, D5 · defects 6, 13

### E02 — Upload artifacts for a scan
**Persona**: Vetting Analyst · **Priority**: Must

As a Vetting Analyst, I want to upload the files I was handed — manifests, archives, package files, container image
archives or binaries — so that I can vet software that is not on the Airlock host.

- Multi-file picker with a staged list and per-file remove.
- Any file type is accepted; Syft detects the type (archives are unpacked, image archives are treated as images).
- Python wheels and NuGet packages are unpacked for Syft; for a NuGet package Airlock also reports the package named
  in its `.nuspec`.
- A scan that includes a container image warns that distribution backports are not considered.
- Upload over HTTP with a progress bar; at most 2 GB and 25 files per scan.
- All manifests are scanned together in one Syft run; every other artifact in its own run; packages are merged and
  de-duplicated across files.
- File names are sanitized; the scan's staging folder is deleted when the scan ends.
- A file in which Syft finds no packages adds a warning to the result; it does not fail the scan.

```gherkin
Scenario: Upload a manifest and a container image archive
  Given Airlock is ONLINE and no scan is active
  And I stage "requirements.txt" and "app-image.tar"
  When I execute the scan
  Then an upload progress bar is shown until both files are received
  And "requirements.txt" is scanned in the manifest run and "app-image.tar" in its own run
  And a package found in both files, such as requests 2.25.1, is listed once

Scenario: A hostile file name cannot escape the staging folder
  When I upload a file named "../../evil.txt"
  Then it is stored inside the scan's staging folder as "evil.txt"
  And nothing is written outside that folder

Scenario: Upload limits are enforced
  When I stage 26 files and execute the scan
  Then the scan is refused with the reason "At most 25 files per scan"
  When I submit 2.5 GB of artifacts
  Then the submission is refused as too large

Scenario: A file without recognizable packages produces a warning
  Given I upload "requirements.txt" and "notes.docx"
  When the scan finishes
  Then the result includes the warning "No packages identified in notes.docx"
  And the status is not Failed because of that file

Scenario: Python wheels and NuGet packages are read
  Given I upload "PyYAML-5.3-cp38-cp38-win_amd64.whl" and "Newtonsoft.Json.12.0.1.nupkg"
  When the scan finishes
  Then the inventory lists pyyaml 5.3 from the wheel
  And it lists the NuGet package Newtonsoft.Json 12.0.1 and the assemblies inside it

Scenario: Image scans warn about distribution backports
  Given I upload the container image archive "alpine-3.20.tar"
  When the scan finishes
  Then the result includes a warning that distribution backports are not considered

Scenario: Staging is cleaned up however the scan ends
  Given a scan with uploaded artifacts
  When it ends as Completed, Incomplete, Failed or Cancelled
  Then its staging folder no longer exists
```

**Traceability**: FR-01, FR-03, FR-05, FR-06, FR-07, FR-25, NFR-03, NFR-05 · Q5-C, Q11, N2, N3, D4, D5, Q-S0-3,
S0 N13 · defects 5, 13

### E03 — Automate scans from a CI pipeline
**Persona**: Pipeline Integrator · **Priority**: Must

As a Pipeline Integrator, I want to submit a scan over HTTP and collect its outcome asynchronously, so that a build
can be gated on Airlock without holding a request open for minutes.

- `POST /api/v2/scans` (multipart `targetPath` or `artifacts[]`) returns 202, a `Location` header and `{id, status}`.
- `GET /api/v2/scans/{id}` returns status, timestamps, scan target, summary, `vulnerablePackages[].findings[]`,
  `unverifiedPackages[]`, warnings and any error.
- `GET /api/v2/scans/{id}/events` streams phase changes, correlation progress, scan-scoped log lines and the final
  status as server-sent events.
- `GET /api/v2/scans/{id}/report.pdf` (409 while running) and `POST /api/v2/scans/{id}/cancel`.
- Errors are ProblemDetails; JSON is camelCase and named after the glossary; the OpenAPI document is browsable with
  Scalar at `/docs`.
- Finished scans stay retrievable for 24 hours, at most 20 of them; they are lost on restart.

```gherkin
Scenario: Submit and poll
  Given Airlock is ONLINE and no scan is active
  When I POST "requirements.txt" as artifacts to /api/v2/scans
  Then the response is 202 with a Location of /api/v2/scans/{id}
  When I GET that location while the scan runs
  Then its status is Running
  When the scan has finished
  Then its status is Completed or Incomplete
  And the summary states totalPackages, vulnerablePackages, cleanPackages, unverifiedPackages, criticalFindings and highFindings

Scenario: Follow the event stream
  When I GET /api/v2/scans/{id}/events
  Then I receive phase changes and "packages correlated" progress
  And the stream ends with the scan's final status

Scenario: A second submission while a scan runs
  Given a scan is active
  When I POST to /api/v2/scans
  Then the response is 409 with a ProblemDetails body saying a scan is already in progress

Scenario: The report is not ready yet
  Given scan {id} is Running
  When I GET /api/v2/scans/{id}/report.pdf
  Then the response is 409

Scenario: Retention
  Given scan {id} finished more than 24 hours ago
  When I GET /api/v2/scans/{id}
  Then the response is 404
  Given 20 finished scans are retained
  When another scan finishes
  Then the oldest finished scan is no longer retrievable
```

**Traceability**: FR-17, FR-19, FR-20, FR-22 · Q10, Q17, N1, D6 · defect 14 · ADR-0003

### E04 — Start scans only when they can be completed honestly
**Persona**: Vetting Analyst, Pipeline Integrator · **Priority**: Must

As a Vetting Analyst, I want Airlock to refuse to start a scan it cannot complete honestly, and tell me why, so that
I never mistake a misconfigured run for a clean result.

- One active scan across the whole server; a second start is refused (UI message, API 409).
- A missing NVD key or an unavailable Syft refuses the scan with the reason (API 503); Execute is disabled while
  Airlock is DEGRADED.
- No target, an invalid path or exceeded limits are refused with the reason (API 400 or 413).
- Reasons appear inline on Control Center and as ProblemDetails in the API.

```gherkin
Scenario: A second scan is refused
  Given a scan started from another browser tab is active
  Then Execute reads "Vetting Sequence Active..." and is disabled
  When a submission reaches the server anyway
  Then it is refused with the reason "A scan is already in progress"
  And the active scan is unaffected

Scenario: A missing NVD key blocks scanning
  Given no NVD API key is configured
  Then the status pill shows DEGRADED with the reason "NVD API key not configured"
  And Execute is disabled
  When a client POSTs to /api/v2/scans
  Then the response is 503 with the same reason

Scenario: Syft is unavailable
  Given the Syft binary cannot be found
  Then the status pill shows DEGRADED with a reason naming the configured Syft path
  And a POST to /api/v2/scans returns 503

Scenario: Nothing to scan
  Given the path is empty and no artifacts are staged
  When I execute the scan
  Then it is refused with the reason "Choose a directory or upload at least one artifact"
```

**Traceability**: FR-02, FR-04, FR-23, FR-25, NFR-03 · Q6, Q8, Q9, N3, N6 · defects 7, 15

---

## Feature area 2 — Correlation results

### E05 — Trustworthy correlation
**Persona**: Vetting Analyst · **Priority**: Must

As a Vetting Analyst, I want each package matched to NVD records accurately and labelled with how it was matched, so
that I can act on findings without chasing false positives.

- Candidate CPEs from the official dictionary are tried first, then Syft's guesses ranked so that a vendor and
  product equal to the package name come first; at most three per package. A candidate NVD does not know, or rejects
  as malformed, simply has no records.
- The first candidate CPE with any NVD record is the CPE match; its HIGH and CRITICAL CVEs become findings.
- Only when no candidate CPE returns records, NVD is asked for the CVEs whose configurations name the package's
  product, from any vendor, at its version; such findings carry the Product match method.
- Severity threshold HIGH; score and severity from CVSS v3.1, else v3.0, else v4.0, else v2 — NVD's own score first,
  the reporting CNA's only when NVD has none — with the version shown.
- NVD traffic stays within 50 requests per 30 seconds; throttled or failed requests are retried with backoff.

```gherkin
Scenario: A CPE match with only lower-severity CVEs is clean
  Given the first candidate CPE for flask 2.0.0 returns only MEDIUM CVEs
  When correlation runs
  Then flask 2.0.0 is a clean package with the CPE match method
  And no product match is attempted for it

Scenario: The likeliest candidate CPE is tried first
  Given Syft offers twelve candidate CPEs for pyyaml 5.3, the last of them "cpe:2.3:a:pyyaml:pyyaml:5.3"
  When correlation runs
  Then "cpe:2.3:a:pyyaml:pyyaml:5.3" is tried first
  And pyyaml 5.3 is a CPE match with the findings CVE-2020-1747 and CVE-2020-14343

Scenario: A product match finds what the CPE guesses missed
  Given Syft's only candidate CPE for minimist 1.2.5 returns no NVD record
  When correlation runs
  Then minimist 1.2.5 has the finding CVE-2021-44906 with the match method "Product match"

Scenario: Description-only mentions never become findings
  Given no candidate CPE for requests 2.25.1 returns any NVD record
  When correlation runs
  Then its product match considers only CVEs whose configurations name the product "requests"
  And CVE-2024-36401 (GeoServer) and CVE-2023-29530 (Laminas Diactoros) are not findings

Scenario: NVD's own score wins
  Given CVE-2023-29530's CVSS v3.1 scores are 6.5 MEDIUM from NVD and 7.5 HIGH from the reporting CNA
  Then Airlock uses 6.5 MEDIUM
  And CVE-2023-29530 is not a finding

Scenario: A CVE without any score is listed, not counted
  Given busybox 1.36.1-r31 is matched to CVE-2025-60876, which NVD has not scored yet
  When the scan finishes
  Then CVE-2025-60876 is not a finding
  And the result includes a warning naming busybox 1.36.1-r31 and CVE-2025-60876

Scenario: The CVSS version is chosen and shown
  Given a CVE with a CVSS v3.1 score of 9.8 and a CVSS v2 score of 7.5
  Then its finding shows 9.8 CRITICAL with the label "CVSS v3.1"
  Given a CVE with only a CVSS v2 score of 9.3
  Then its finding shows 9.3 HIGH with the label "CVSS v2.0"

Scenario: NVD rate limit is respected
  Given correlation needs 120 NVD lookups
  When it runs
  Then no more than 50 NVD requests are sent in any 30-second window
```

**Traceability**: FR-08, FR-09, FR-10, FR-12, FR-13, NFR-04 · Q5-D, Q7, Q17, Q-S0-1, Q-S0-2, Q-U1-1, Q-U1-2 ·
defects 1, 9, 10, 11, 12 · ADR-0006

### E06 — Results that never overstate safety
**Persona**: Vetting Analyst, Pipeline Integrator · **Priority**: Must

As a Vetting Analyst, I want packages Airlock could not check to be reported as Unverified and the scan as Incomplete,
so that an outage or throttling never turns into a "clean" verdict.

- A package whose NVD lookups still fail after retries, or whose version is unknown, is Unverified and never counted
  as clean.
- A scan with any Unverified package finishes as Incomplete; the summary, the Reports view and the PDF show the count.
- A Syft failure fails the scan; a scan that exceeds 60 minutes fails with a reason saying so.
- Summary counts add up: total packages = clean + vulnerable + unverified.

```gherkin
Scenario: Some lookups fail
  Given a scan finds 87 packages, 2 of them vulnerable
  And NVD keeps answering 503 for 12 other packages after all retries
  When the scan finishes
  Then its status is Incomplete
  And unverifiedPackages is 12 and cleanPackages is 73
  And the Reports view shows a banner listing the 12 unverified packages

Scenario: Every lookup succeeds
  Given every NVD lookup for a scan succeeds
  When the scan finishes
  Then its status is Completed

Scenario: A package without a version is Unverified
  Given Syft reports the package "syft" with the version "UNKNOWN"
  When the scan finishes
  Then that package is Unverified with the reason "version unknown"
  And the scan's status is Incomplete

Scenario: The scan time limit is reached
  Given a scan is still running after 60 minutes
  Then it stops with status Failed and the reason "Scan exceeded the 60-minute limit"

Scenario: Counts add up
  Given any finished scan
  Then totalPackages equals cleanPackages plus vulnerablePackages plus unverifiedPackages
```

**Traceability**: FR-06, FR-11, FR-14, FR-15, NFR-03, NFR-04 · Q6, Q8, Q15, N3, Q-S0-4 · defects 6, 7, 8 · ADR-0002

---

## Feature area 3 — Monitoring and control

### E07 — Find my way around Airlock
**Persona**: Vetting Analyst · **Priority**: Must

As a Vetting Analyst, I want the familiar Airlock layout in its dark tactical look, so that the rebuild feels like
the tool I know.

- Top bar with the brand; notification, settings and avatar placeholders that do nothing when clicked
  *(Should: today they reload the page and wipe the result)*.
- Navigation to Control Center (`/`), Active Logs (`/logs`) and Reports (`/reports/{id?}`) with the active item
  highlighted, plus the Execute Audit shortcut.
- MudBlazor themed with the original palette, Inter and JetBrains Mono, uppercase monospace labels; the tactical
  on-screen wording is kept.
- Entry animations that actually run *(Could)*.
- Every action reachable by keyboard; severity badges always carry text, not colour alone.

```gherkin
Scenario: Placeholder buttons are inert
  Given a scan result is on screen
  When I click the notifications button
  Then the page does not reload
  And the result is still shown

Scenario: Navigation keeps the scan state
  Given a scan is running from Control Center
  When I open Active Logs and return to Control Center
  Then the scan, its elapsed timer and the staged inputs are unchanged

Scenario: Keyboard operation
  When I move through Control Center with the keyboard
  Then I can reach and activate the path field, the artifact picker, Execute and Emergency Stop

Scenario: Severity is readable without colour
  Then every severity badge shows its text: CRITICAL, HIGH or UNVERIFIED
```

**Traceability**: FR-24, FR-30, NFR-12 · Q4, Q5-A, Q12, Q14, N4, N5 · defects 16, 17 · ADR-0005

### E08 — Watch a scan as it runs
**Persona**: Vetting Analyst · **Priority**: Must

As a Vetting Analyst, I want to see progress, elapsed time and Airlock's live log while a scan runs, so that I know it
is working and can spot problems early.

- Elapsed timer (`mm:ss`, `h:mm:ss` past an hour) on Control Center and Active Logs.
- Current phase and "n of total packages correlated" progress.
- Terminal: docked mini terminal on Control Center and a full one on Active Logs, with minimize, auto-scroll and an
  empty state; cleared when a scan starts.
- The Live Log reaches every open tab; each tab's buffer is bounded (a slow tab drops its oldest lines).
- Export Logs downloads the terminal contents as a `.log` file.
- The Active Logs header shows the real machine name instead of "DC-04" *(Could)*.

```gherkin
Scenario: Every open tab receives the Live Log
  Given two browser tabs are on Active Logs
  When a scan writes a log line
  Then both tabs show that line

Scenario: Correlation progress
  Given a scan with 87 packages
  When 40 packages have been correlated
  Then the progress reads "Correlating 40 of 87"

Scenario: The terminal starts fresh for each scan
  Given the terminal shows lines from a previous scan
  When I execute a new scan
  Then the terminal is cleared before the new scan's lines appear

Scenario: Export the log
  When I choose Export Logs
  Then a file named like "airlock_audit_<timestamp>.log" downloads
  And it contains the lines shown in the terminal
```

**Traceability**: FR-17, FR-18, FR-25, FR-26, FR-27, FR-31, NFR-07 · Q4, Q5-B, Q14, N1 · defects 4, 14

### E09 — Stop a scan
**Persona**: Vetting Analyst, Pipeline Integrator · **Priority**: Must

As a Vetting Analyst, I want to stop a running scan immediately — including one started elsewhere — so that a wrong
or runaway scan does not tie up Airlock.

- Emergency Stop in the Terminal, behind a confirmation dialog, cancels the active scan whoever started it.
- API: `POST /api/v2/scans/{id}/cancel` for a given scan; `POST /api/v2/system/stop` for the active scan.
- Stopping kills the Syft process tree and abandons pending NVD calls, on Windows and Linux, without affecting the
  Airlock server.
- A stopped scan ends with status Cancelled and its staging folder is deleted.

```gherkin
Scenario: Stop during SBOM generation
  Given Syft is running for the active scan
  When I confirm Emergency Stop
  Then the Syft process and its child processes end
  And the scan's status is Cancelled

Scenario: Stop during correlation
  Given correlation has reached package 30 of 87
  When I confirm Emergency Stop
  Then no further NVD requests are sent for that scan
  And the scan's status is Cancelled

Scenario: The server keeps running
  Given I stopped a scan
  Then Airlock stays ONLINE
  And I can start a new scan straight away

Scenario: Dismissing the confirmation
  When I open Emergency Stop and dismiss the confirmation
  Then the scan continues

Scenario: Same behaviour on Windows
  Given Airlock runs on Windows
  When I stop a scan
  Then it ends as Cancelled exactly as it does on Linux
```

**Traceability**: FR-16, FR-27, NFR-06 · Q5-B, Q9 · defects 3, 15

---

## Feature area 4 — Reports and exports

### E10 — Review scan results
**Persona**: Vetting Analyst · **Priority**: Must

As a Vetting Analyst, I want a clear summary and a filterable inventory with CVE details, so that I can decide
whether the software may be admitted.

- Spinner while the scan runs; empty state when there is no result.
- Header with the completion time (in my browser's time zone) and the duration.
- Cards: Total packages, Critical findings (pulsing when above zero), High findings, Clean packages; an Unverified
  banner when any package is unverified.
- Inventory grid: Risk (highest severity, or UNVERIFIED), Package, Version, Method (CPE match or Product match),
  Findings count; filter by package name or CVE id.
- Detail pane: CVE link to NVD, copy description, CVSS bar with version, description and package type; selection is
  by package and version.
- Successful scans open `/reports/{id}`, which survives a refresh while the scan is retained.
- Rescan re-submits the same target; for upload scans it needs the files still staged in this browser session,
  otherwise it opens Control Center with a hint to re-select them.

```gherkin
Scenario: Risk shows the highest severity
  Given pyyaml 5.3 has a HIGH finding listed before a CRITICAL finding
  Then its Risk shows CRITICAL

Scenario: Product matches are labelled
  Given a package was linked to NVD by product match
  Then its Method shows "Product match"

Scenario: Filter by CVE id
  When I type "CVE-2020-1747" into the filter
  Then only pyyaml 5.3 remains in the grid

Scenario: Two versions of the same package
  Given requests 2.25.1 and requests 2.31.0 are both vulnerable
  When I select requests 2.25.1
  Then only that row is selected and the detail pane shows its findings

Scenario: Cards count findings and packages
  Given pyyaml 5.3 is the only vulnerable package and has two CRITICAL CVEs
  Then Critical findings shows 2
  And the result has 1 vulnerable package

Scenario: The result survives a refresh
  Given I am viewing /reports/{id}
  When I refresh the page
  Then the same result is shown

Scenario: Rescanning uploads after a refresh
  Given the result came from uploaded artifacts and I refreshed the page
  When I choose Rescan
  Then Control Center opens with a hint to re-select the artifacts
```

**Traceability**: FR-14, FR-19, FR-28, FR-29, FR-31 · Q4, Q5-A, Q6, Q15, N1, Q-S0-2 · defects 1, 2

### E11 — Export a Scan Report
**Persona**: Vetting Analyst, Pipeline Integrator · **Priority**: Must

As a Vetting Analyst, I want to export a scan as PDF or JSON, so that I can attach evidence to the admission
decision.

- One server-side PDF generator serves the UI and the API: title with the completion time (labelled UTC), summary
  including Unverified, findings table (package, version, match method, CVE, severity, score with CVSS version, NVD
  link, full description) and an Unverified section.
- The JSON export is the scan resource returned by `GET /api/v2/scans/{id}`.

```gherkin
Scenario: The UI and the API produce the same report
  Given a finished scan {id}
  When I export the PDF from Reports and also GET /api/v2/scans/{id}/report.pdf
  Then both documents contain the same summary and findings

Scenario: An Incomplete scan's report lists unverified packages
  Given scan {id} finished as Incomplete with 12 unverified packages
  When I export the PDF
  Then it contains an Unverified section listing those 12 packages

Scenario: JSON export matches the API
  When I export JSON from Reports
  Then the file's content equals the body of GET /api/v2/scans/{id}

Scenario: No export while the scan runs
  Given the scan is Running
  Then the export buttons are disabled
  And GET /api/v2/scans/{id}/report.pdf returns 409
```

**Traceability**: FR-21, FR-22 · Q10, Q13 · defect 18

---

## Feature area 5 — Health and configuration

### E12 — Know whether Airlock is ready
**Persona**: Vetting Analyst, Platform Operator · **Priority**: Must

As a Vetting Analyst, I want a status pill that tells me whether Airlock can scan right now and why not, so that I
don't start scans that cannot succeed.

- ONLINE: Syft is found and an NVD key is configured.
- DEGRADED: either is missing; the reason shows on hover; Execute is disabled.
- OFFLINE: the connection to the server is lost or reconnecting.
- LOADING: the page is starting up.
- `GET /api/v2/system/health` returns the same checks as JSON; NVD is never probed periodically.

```gherkin
Scenario: Ready
  Given Syft is found and an NVD key is configured
  Then the status pill shows ONLINE

Scenario: Not ready
  Given the NVD key is missing
  Then the status pill shows DEGRADED
  And hovering it shows "NVD API key not configured"

Scenario: Connection lost
  Given the Airlock server stops
  Then the status pill shows OFFLINE within a few seconds

Scenario: Health over the API
  When a client GETs /api/v2/system/health
  Then the response lists the Syft check with its version and the NVD key check, each passing or failing
```

**Traceability**: FR-23, FR-25, NFR-07 · Q8, N6

### E13 — Configure Airlock
**Persona**: Platform Operator · **Priority**: Must

As a Platform Operator, I want to configure Airlock through standard settings with safe, validated defaults, so that
it behaves predictably wherever I run it.

- Original variable names kept: `NVD_API_KEY`, `SYFT_BINARY_PATH`, `LOG_LEVEL`, `SCAN_UPLOAD_PATH`; further options
  under `Airlock:*` (AllowedScanRoots, limits, timeouts, retention).
- The NVD key comes from user-secrets in development and from an environment variable in containers, and never
  appears in any log.
- Invalid values (for example a negative limit) stop startup with a message naming the option; a missing NVD key does
  not stop startup — it makes Airlock DEGRADED.
- Defaults: 2 GB and 25 files per scan; Syft 15 minutes; scan 60 minutes; retention 24 hours / 20 scans; NVD 50
  requests per 30 seconds. The severity threshold is fixed at HIGH.

```gherkin
Scenario: The key never reaches the logs
  Given NVD_API_KEY is configured
  When Airlock starts and runs a scan with Debug logging
  Then no console line and no Live Log line contains the key

Scenario: An invalid option stops startup
  Given Airlock:Limits:MaxFilesPerScan is set to -1
  When Airlock starts
  Then startup fails with a message naming Airlock:Limits:MaxFilesPerScan

Scenario: Allowed scan roots are optional
  Given AllowedScanRoots is not set
  Then any existing directory on the host can be scanned, as before

Scenario: Console verbosity is independent of the Live Log
  Given LOG_LEVEL is Warning
  When a scan runs
  Then the console shows only warnings and errors
  And the Live Log still shows the scan's activity
```

**Traceability**: FR-02, FR-19, NFR-03, NFR-04, NFR-07 · Q8, Q17, N3

---

## Feature area 6 — Delivery

### E14 — Run Airlock locally or in Docker
**Persona**: Platform Operator · **Priority**: Must

As a Platform Operator, I want to run Airlock with `dotnet run` on Windows or with Docker Compose on Linux and get the
same behaviour, so that development and deployment match.

- `scripts/install-tools.ps1` and `scripts/install-tools.sh` fetch Syft v1.55.0 into `./bin`.
- One container image with Syft bundled, on a chiseled base with no shell, run with a read-only root filesystem
  (Q-U6-N1); Docker Compose mounts `SCAN_UPLOAD_PATH` at `/uploads`.
- `docker compose up` runs the published image; `docker compose up --build` builds the working copy (Q-U6-2).
- The same target gives the same packages and findings on Windows and in the container.

```gherkin
Scenario: Run locally on Windows
  Given the .NET 10 SDK is installed and install-tools.ps1 has fetched Syft
  And an NVD key is in user-secrets
  When I run "dotnet run --project src/Airlock.Web"
  Then Airlock is reachable in the browser and shows ONLINE

Scenario: Run with Docker Compose
  Given SCAN_UPLOAD_PATH points at a host folder and the NVD key is in .env
  When I run "docker compose up --build"
  Then scanning "/uploads" scans that host folder

Scenario: Same results on both platforms
  Given the same manifest and the same NVD responses
  When I scan it locally on Windows and in the container
  Then both results list the same packages and findings
```

**Traceability**: NFR-01, NFR-06, NFR-11 · Q3, Q17, Q18, D3, Q-U6-2, Q-U6-N1

### E15 — Trust how Airlock is built and licensed
**Persona**: Platform Operator · **Priority**: Should

As a Platform Operator, I want automated builds, tests, code scanning, dependency updates and clear licensing, so
that I can rely on Airlock and redistribute it.

- GitHub Actions builds, tests and builds the Docker image on pushes and pull requests to `main`.
- A version tag the owner pushes publishes the tested image to the GitHub Container Registry (Q-U6-2).
- The image is scanned for known vulnerabilities before it is published (Q-U6-N2), and every published version
  carries signed build provenance and an SBOM (Q-U6-N3).
- Every action and base image is pinned to a commit SHA or digest (Q-U6-N4).
- CodeQL for C# on pull requests and weekly; Dependabot for NuGet, Docker, GitHub Actions and the .NET SDK.
- MIT licence; the README states the QuestPDF Community licence terms; the README describes actual behaviour.
- Nothing is pushed to GitHub without the owner's explicit approval.

```gherkin
Scenario: Pull requests are verified
  When a pull request targets main
  Then the workflow builds the solution, runs the tests and builds the Docker image
  And reports failure if any step fails

Scenario: Code scanning
  Then CodeQL analyzes the C# code on every pull request and once a week

Scenario: Licensing is documented
  When I read the README
  Then it states that Airlock is MIT-licensed
  And it names the QuestPDF Community licence and its revenue threshold

Scenario: A published image can be verified
  Given a version the release workflow published
  When I run "gh attestation verify" for its image
  Then GitHub confirms the workflow and the commit that built it
  And the image's SBOM attestation verifies as well
```

**Traceability**: NFR-10, NFR-11 · Q13, Q18, Q-U6-1, Q-U6-2, Q-U6-N2, Q-U6-N3, Q-U6-N4 · defect 18

---

## Coverage

### Requirements → epics

| Requirement | Epics |
|---|---|
| FR-01 | E01, E02 |
| FR-02 | E01, E02, E04, E13 |
| FR-03 | E02 |
| FR-04 | E04 |
| FR-05 | E01, E02 |
| FR-06 | E01, E02, E06 |
| FR-07 | E01, E02 |
| FR-08 | E05 |
| FR-09 | E05 |
| FR-10 | E05 |
| FR-11 | E06 |
| FR-12 | E05 |
| FR-13 | E05 |
| FR-14 | E06, E10 |
| FR-15 | E06 |
| FR-16 | E09 |
| FR-17 | E03, E08 |
| FR-18 | E08 |
| FR-19 | E03, E10, E13 |
| FR-20 | E03 |
| FR-21 | E11 |
| FR-22 | E03, E11 |
| FR-23 | E04, E12 |
| FR-24 | E07 |
| FR-25 | E01, E02, E04, E08, E12 |
| FR-26 | E08 |
| FR-27 | E08, E09 |
| FR-28 | E10 |
| FR-29 | E10 |
| FR-30 | E07 |
| FR-31 | E08, E10 |
| NFR-01 | E14 |
| NFR-02 | internal — requirements.md only |
| NFR-03 | E02, E04, E06, E13 |
| NFR-04 | E05, E06, E13 |
| NFR-05 | E02 |
| NFR-06 | E09, E14 |
| NFR-07 | E08, E12, E13 |
| NFR-08 | internal — requirements.md only |
| NFR-09 | internal — requirements.md only |
| NFR-10 | E15 |
| NFR-11 | E14, E15 |
| NFR-12 | E07 |

### Original defects → epics

| Defect | Epics | Defect | Epics |
|---|---|---|---|
| 1 | E05, E10 | 10 | E05 |
| 2 | E10 | 11 | E05 |
| 3 | E09 | 12 | E05 |
| 4 | E08 | 13 | E01, E02 |
| 5 | E02 | 14 | E03, E08 |
| 6 | E01, E06 | 15 | E04, E09 |
| 7 | E04, E06 | 16 | E07 |
| 8 | E06 | 17 | E07 |
| 9 | E05 | 18 | E11, E15 |
