# U5 Web UI — Domain Entities (UI types)

**Stage**: CONSTRUCTION — U5 Web UI — Functional Design (Standard depth)

The UI shows the Domain's own values — `Scan`, `ScanSummary`, `VulnerablePackage`, `UnverifiedPackage`, `Finding`,
`ScanWarning` — as the Application hands them over in-process (ADR-0001); it never reads the API's JSON. It adds only
what a browser tab needs: the tab's session, the files it has staged, its upload, the lines of its terminal, the states
of its status pill and of its scan view, the rows of its grid, and its words and colours. The rules are in
`business-rules.md` (UR); the flows, the `airlock.js` contract, the worked example on screen and the test map are in
`business-logic-model.md`.

## The tab's session — `ScanSession`

One per browser tab — one Blazor circuit — shared by every page of that tab (UR-03).

| Member | Holds | Changes when |
|---|---|---|
| `DirectoryPath` | The Local Directory Path; `/uploads` at first | The analyst edits it; kept while artifacts are staged (UR-06) |
| `Staged` | The staged artifacts (`StagedArtifact`), in the order picked | Files are picked or removed (UR-07); the page's script holds the files themselves |
| `Upload` | This tab's upload under way (`UploadProgress`), or none | A submission with artifacts starts, reports progress, receives its answer (UR-11) |
| `Active` | Who holds the server's active slot (`ActiveScanState`, U2): nobody, a reservation (perhaps receiving an upload), or a running scan | `WatchActiveScan` reports a change (UR-13) |
| `Current` | The scan the tab follows, or followed last (`Scan`), or none | A scan takes the active slot, and on each of its events (UR-13) |
| `Submitted` | The ids of the scans this tab submitted | A submission is accepted with 202 (UR-10) |
| `Lines` | The terminal's lines (`TerminalLine`), oldest first, bounded | The Live Log writes; a new scan starts (UR-17, UR-18) |
| `Health` | The last `SystemHealth` (U2), or none before the first answer | Every 10 seconds (UR-04) |
| `TimeZone` | The browser's time zone, or none until known | The page's script reports it (UR-34) |
| `Notice` | The tab's inline notice (`InlineNotice`), or none | UR-12 |

`Changed` tells the tab's components what changed, so each redraws for its own part. U5's NFR design keeps these
members as one immutable snapshot and coalesces the redraws.

## A staged artifact — `StagedArtifact`

| Member | Meaning |
|---|---|
| `Id` | The page script's handle for the file (`airlock.js`, `business-logic-model.md`) |
| `FileName` | The file's name as the browser gives it; the server sanitizes it (U2 AR-07) |
| `SizeBytes` | Its size in bytes |

Two staged artifacts are the same file when their names and sizes are equal (UR-07).

## An upload — `UploadProgress`

| Member | Meaning |
|---|---|
| `SentBytes`, `TotalBytes` | The request body's bytes sent so far and in all, as the browser reports them |
| `Percent` | `SentBytes` as a share of `TotalBytes`, rounded down, from 0 to 100 |

## A terminal line — `TerminalLine`

Made from a Live Log entry (`LiveLogEntry`, U2), whose `Timestamp`, `Level`, `Message` and `ScanId` it keeps. It reads
`14:01:32 INFO 📥 Processing 1 uploaded artifact(s)` (UR-17):

- the time in the browser's zone;
- the level, in the names `LOG_LEVEL` accepts: TRACE, DEBUG, INFO, WARNING, ERROR, CRITICAL;
- the message as Airlock wrote it, emoji included.

The entry's category is not shown.

## An inline notice — `InlineNotice`

At most one per tab. It shows under Execute on Control Center, and under the header on Reports (UR-12).

| Kind | Text | Look |
|---|---|---|
| Refusal | The refusal's message, or the problem's `detail` from the API: "A scan is already in progress", "Target path does not exist", "At most 25 files per scan"… | Error |
| Interrupted | "Upload interrupted — check the connection and try again" | Error |
| Failed | "Scan failed: <reason>" | Error |
| Cancelled | "Scan cancelled: <reason>" | Information |
| Finished | "Scan COMPLETED — View report" or "Scan INCOMPLETE — View report", linking to the report | Success for COMPLETED, warning for INCOMPLETE |
| Rescan hint | "Re-select the artifacts to rescan: requirements.txt, app-image.tar" | Information |

## The status pill's states

| State | When | Text | Dot |
|---|---|---|---|
| LOADING | Before the tab's first health answer | System Status: LOADING | Grey, pulsing |
| ONLINE | The last health answer is Online | System Status: ONLINE | Emerald |
| DEGRADED | The last health answer is Degraded | System Status: DEGRADED, with the failing checks' details in its tooltip | Yellow |
| OFFLINE | The tab's connection is lost; the page's script sets it (UR-05) | System Status: OFFLINE | Red |

## A page's view of a scan

| View | When | Reports shows | Control Center and Active Logs show |
|---|---|---|---|
| None | The tab has no current scan | "No audit data available. Execute a scan to begin." | Timer `00:00`, no progress |
| Preparing | A reservation holds the active slot: a scan being submitted, or an upload being received | The spinner with "Receiving artifacts…", or this tab's transfer progress | "Vetting Sequence Active..." and the same line |
| Running | The scan is Running | The spinner with the phase and the progress | The timer running, the phase and the progress |
| Analysis | Completed or Incomplete | Header, cards, banner, warnings, grid, detail pane | The timer stopped at the duration |
| Failed | Failed | The header and "Scan failed: <reason>" | The timer stopped |
| Cancelled | Cancelled | The header and "Scan cancelled: <reason>" | The timer stopped |
| Not found | `/reports/{id}` names no retained scan | "Scan not found or no longer retained" | — |

## An inventory row — `InventoryRow`

One per vulnerable or unverified package, in the scan's own order: the vulnerable packages by highest severity, then
name and version; then the unverified ones by name and version (U1).

| Column | Vulnerable package | Unverified package |
|---|---|---|
| Risk | Its highest severity, CRITICAL or HIGH (defect 2) | UNVERIFIED |
| Package Name | Its name | Its name |
| Version | Its version | Its version |
| Method | "CPE match" or "Product match" (defect 1) | Empty |
| Findings | Its number of findings in two digits: "02" | Empty |

A row's identity is its package's: type, name and version (UR-29). A row matches a filter when the text is part of its
name or of one of its CVE ids, ignoring case (UR-28).

## The copy catalogue

Every text the UI shows. **Source**:

- **Original**: the React UI at `JJWren/Airlock`, kept as it was.
- **Changed**: the original's text, altered as the note says.
- **Requirement**: a requirement, the glossary, or a U2 to U4 design.
- **New**: added by this design.

The catalogue gives the text as written. The theme may display it in capitals (UR-35): "Control Center" appears as
CONTROL CENTER, as it did in the original. Values that are capitals by nature stay so: statuses, severities and
levels.

| Place | Text | Source |
|---|---|---|
| Top bar | Airlock | Original |
| Top bar buttons (screen-reader labels) | Notifications · Settings · Profile | New |
| Drawer heading | Control Center · Tactical Observer | Original |
| Drawer items | Control Center · Active Logs · Reports | Original |
| Drawer action | Execute Audit | Original |
| Status pill | System Status: ONLINE · DEGRADED · OFFLINE · LOADING | Original; DEGRADED from N6 |
| Page titles (the browser tab) | Control Center · Airlock · Active Logs · Airlock · Reports · Airlock · Page not found · Airlock · Something went wrong · Airlock | New (Code Generation) |
| Top bar menu (screen-reader label, below 960 pixels) | Menu | New (Code Generation) |
| Connection bar | Connection lost — reconnecting… · Connection lost. · Retry (after a failed reconnect) · Reload | New; Retry from NFR Design |
| Control Center heading | Control Center | Original |
| Control Center subtitle | Prepare your assets for deep analysis. Select a path or upload artifacts. | Changed: "upload manifest files" |
| Path card | Local Directory Path | Original |
| Path note | Staged artifacts are scanned instead of this path | New (Q-U5-1) |
| Upload panel, empty | Artifact Upload · Up to 25 files, 2 GB in total | Changed: "Manifest Upload"; the limits are new |
| Upload panel, staged | 2 File(s) Staged · requirements.txt · 56 B · Remove requirements.txt (screen-reader label) | Original; size and label new |
| Execute | Execute Security Audit · Vetting Sequence Active... | Original |
| Execute, disabled | Airlock is not ready: NVD API key not configured · Checking system status... | New |
| Timer | Elapsed Time · 01:32 | Original |
| Progress | SBOM Generation · Correlation (before the count is known) · Correlating 40 of 87 · Summary | Requirement (E08) |
| Preparing | Receiving artifacts… · Transferring artifacts: 45% (450.0 MB of 1.0 GB) | New |
| Checks before uploading | At most 25 files per scan · Uploads are limited to 2 GB per scan | Requirement (the Application's refusals) |
| Notices | The texts of `InlineNotice` above · Dismiss notice (screen-reader label of ✕) | New |
| Terminal header | Global Terminal · Minimize terminal · Restore terminal (screen-reader labels) | Original; labels new |
| Terminal, empty | 📡 Standing by for telemetry... | Original |
| Terminal actions | Export Logs · Emergency Stop | Original |
| Stop dialog | CRITICAL: Terminate the active scan? · Cancel · Terminate | Changed: "CRITICAL: Terminate process group?" |
| Log export, first line | # Airlock 2.0.0 Live Log, exported 2026-10-22 14:05:00 UTC+02:00 (Europe/Berlin) | New |
| Active Logs | System Vulnerability Scan · Node: <machine name> // Mode: Tactical Analysis · Elapsed Time | Original; "DC-04" becomes the machine's name (FR-26) |
| Reports, running | Vetting In Progress · Building Security Matrix... | Original |
| Reports, empty | No audit data available. Execute a scan to begin. | Original |
| Reports, not found | Scan not found or no longer retained · Back to Control Center | Requirement (U2); the link is new |
| Reports heading | Post-Scan Analysis | Original |
| Status badge | COMPLETED · INCOMPLETE · FAILED · CANCELLED | Requirement (FR-15, U4 values) |
| Target | Target: /uploads/acme-app · Target: requirements.txt · Target: requirements.txt and 1 more artifact · Target: requirements.txt and 2 more artifacts | New |
| Time line | 2026-10-22 14:01:32 UTC+02:00 // SCAN TIME: 92.42S | Changed: the finish time instead of the time the page was drawn |
| Reports actions | Export JSON · Export PDF · Rescan Project | Original |
| Failed, cancelled | Scan failed: <reason> · Scan cancelled: <reason> | New |
| Cards | Total Packages · Critical Findings · High Findings · Clean Packages | Original; "Critical Risks" and "High Risks" changed (FR-14) |
| Unverified banner | INCOMPLETE — 1 package could not be verified and is not counted as clean · INCOMPLETE — 12 packages could not be verified and are not counted as clean · Show all (12) · Show fewer | New (E06) |
| Banner rows | jinja2 2.10 — lookup failed: NVD returned 503 · syft UNKNOWN — version unknown | New |
| Warnings panel | Warnings (3) | New |
| Grid | Inventory Matrix · FILTER BY PACKAGE OR CVE... · Risk · Package Name · Version · Method · Findings | Original |
| Risk values | CRITICAL · HIGH · UNVERIFIED | Requirement (FR-28) |
| Method values | CPE match · Product match | Requirement (FR-28); the original showed CPE or KEYWORD |
| Grid, nothing matches | No matching assets found in project inventory. | Original |
| Grid, nothing to list | No HIGH or CRITICAL findings: every package is clean. · No packages identified in the scan target. | New |
| Detail pane | Vulnerability Details · Active Analysis: pyyaml 5.3 (python) · Close details (screen-reader label) | Original; version, type and label new |
| Match | Matched by CPE: cpe:2.3:a:pyyaml:pyyaml:5.3:*:*:*:*:*:*:* (generated) · (dictionary) · Matched by product: flask | New |
| Finding | CVE-2020-1747 · COPY · CVSS v3.1 Score · NVD · CNA · 9.8 CRITICAL | Changed: the original's fixed "CVSS v3.1 Score"; the source is new |
| Unverified package | Not verified — lookup failed: NVD returned 503 · Not verified — version unknown | New |
| Copying | Description copied · Copy failed — select the text and copy it | New |
| Snackbars | Scan COMPLETED — View report · Scan INCOMPLETE — View report · Scan failed: <reason> · Scan cancelled: <reason> · No active scan | New |
| Unknown address | Page not found · Back to Control Center | New |
| Unexpected error | Something went wrong · Reload | New |

Reasons and details come from the Application as written: "Emergency Stop", "Cancelled by request", "Server shutting
down", "Scan exceeded the 60-minute limit", a failure naming its input, "NVD returned 503". An unverified package's
reason reads "lookup failed: <detail>" or "version unknown".

## Theme tokens

| Token | Value | Used for | Source |
|---|---|---|---|
| Background | #09090B | Pages | Original `neutral` |
| Surface | #18181B | Cards, panels, the grid, the pill | Original `surface` |
| Top bar | #131315 | The top bar | Original navbar |
| Drawer | #1C1B1D | The drawer; the terminal's header and action bar | Original sidebar and terminal |
| Terminal | #000000 | The terminal's lines | Original |
| Primary | #3B82F6 | The brand, Execute's fill, links, focus, the selected row's tint, the Total Packages card | Original `primary` |
| Primary text on tints | #60A5FA | Blue text on a tinted or highlighted background: the selected row, the active drawer item, the selected findings count | New (Q-U5-N1) |
| On primary | #09090B | Text on primary fills, such as Execute | Original |
| Secondary | #71717A | Borders, icons, dividers, disabled controls; LOADING's dot | Original `secondary`, no longer for text (Q-U5-N1) |
| Secondary text | #A1A1AA | Secondary text and small labels: "Elapsed Time", card labels, column heads, hints, placeholders | New (Q-U5-N1) |
| Error | #EF4444 | Errors, OFFLINE, Emergency Stop, the Critical Findings card, CRITICAL's tint and border | Original `tertiary` |
| Error text on tints | #F87171 | Red text on a tinted background: the CRITICAL badge, error notices | New (Q-U5-N1) |
| High | #FB923C | The HIGH badge | Original row badge (orange-400) |
| High card | #F97316 | The High Findings card | Original card (orange-500) |
| Warning | #EAB308 | UNVERIFIED, DEGRADED, the Incomplete banner, WARNING lines | New |
| Clean | #10B981 | The Clean Packages card; ONLINE; the drawer's dot | Original (emerald-500) |
| Text | #FFFFFF · #E2E8F0 · #CBD5E1 · #94A3B8 | Headings · body · terminal lines · grid labels | Original (white, slate-200, slate-300, slate-400) |
| Fonts | Inter; JetBrains Mono | Text and headings; labels, values, the grid's versions, the terminal | Original |
| Labels | Capitals, letter spacing 0.1 to 0.2 em | Card labels, column heads, buttons, the drawer | Original |
| Badges | The colour as text, at 10 % as background and 20 % as border; red and blue text in their lighter shades | Severity and status badges | Original; shades from Q-U5-N1 |

Every text colour meets WCAG 2.2 AA on the background it is used on (Q-U5-N1, U5-NFR-07); the measurements are in
`../nfr-requirements/nfr-requirements.md`.
