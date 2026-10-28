# U5 Web UI — Business Rules

**Stage**: CONSTRUCTION — U5 Web UI — Functional Design (Standard depth)

The UI rules (UR) say what the Blazor UI shows and does. Like the API, the UI decides nothing about a scan: every
decision stays with the Application (AR rules, U2). Components call use cases in-process (ADR-0001), never an
Infrastructure type (architecture rule 5). Only two things go over HTTP: submitting a scan, and downloading its
exports. "Trace" names the requirement, story, question or plan decision behind a rule. The types and the copy
catalogue are in `domain-entities.md`; the flows, the `airlock.js` contract, the worked example on screen and the test
map are in `business-logic-model.md`.

## The shell

**UR-01 — Layout and navigation.** Every page has the top bar and the drawer.

- **Top bar**: the brand "Airlock" on the left; the notifications, settings and profile buttons on the right.
- **Drawer heading**: "Control Center · Tactical Observer", with its green dot.
- **Drawer items**: Control Center (`/`), Active Logs (`/logs`) and Reports (`/reports` and `/reports/{id}`). The
  current item is highlighted; Reports is highlighted on both of its addresses.
- **Execute Audit** at the bottom opens Control Center. It starts no scan, as in the original.

*Trace*: FR-24; E07; plan decision 30.

**UR-02 — Placeholders.** Notifications, settings and profile are buttons, labelled for screen readers
("Notifications", "Settings", "Profile"), that do nothing. They neither navigate nor reload, so nothing on screen is
lost (defect 16). The profile shows a local icon, and no page loads anything from another host. *Trace*: FR-24; E07
"Placeholder buttons are inert"; plan decision 23; U4 hand-off (fonts and icons from Airlock itself).

**UR-03 — The tab's session.** A tab's session (`ScanSession`) lasts as long as the tab's connection to Airlock — its
circuit — and every page of the tab shares it.

- Moving between Control Center, Active Logs and Reports keeps everything in it: the directory path, the staged
  artifacts, the upload under way, the scan followed, the terminal's lines and the notice (E07).
- A connection that drops and comes back keeps the session.
- A new session starts with a reload, with a new tab, or when the server has given up on the tab's connection. It
  picks up a running scan (UR-13) and receives the Live Log's last 200 lines (U3).
- No session outlives the server.

*Trace*: E07 "Navigation keeps the scan state"; services.md; plan decision 1; Q-U5-2.

## Readiness and the connection

**UR-04 — The status pill.** Control Center's pill reads "System Status: " and its state (`domain-entities.md`).

- **LOADING** until the tab's first health answer.
- **ONLINE** or **DEGRADED** as `GetSystemHealth` answers. Health is read again every 10 seconds, as the original
  polled, and never calls NVD (U2).
- **OFFLINE** while the connection is lost (UR-05).

DEGRADED's tooltip lists the detail of each failing check: "NVD API key not configured", "Syft not found at
/opt/airlock/bin/syft". It opens on keyboard focus as well as on hover, and screen readers read the details with the
state. *Trace*: FR-23, FR-25; E04, E12; N6; plan decision 8.

**UR-05 — Connection loss.** The server cannot update a page it has lost, so the page's script reacts when the tab's
connection drops.

- The pill turns OFFLINE, and a thin bar at the top of every page reads "Connection lost — reconnecting…". The page
  stays readable, and nothing blocks it; Execute, Rescan and the terminal's actions, which the server answers, are
  dimmed and inert meanwhile.
- When the connection comes back, the bar goes and the pill shows the server's state again.
- When reconnecting fails, or the server no longer knows the tab's session, the bar reads "Connection lost." and
  offers Reload; after a failed reconnect it offers Retry as well.

Blazor's default reconnect dialog is not used. *Trace*: E12 "Connection lost"; ADR-0001 (consequences); plan
decision 9.

## Control Center

**UR-06 — The scan target (Q-U5-1).** The Local Directory Path starts as `/uploads` and is sent as typed. While any
artifact is staged, a scan uploads the artifacts only. The path field is then dimmed, cannot be edited, keeps its text,
and shows "Staged artifacts are scanned instead of this path"; removing the last artifact brings it back. The page
therefore never sends both, and the API's "not both" refusal (U2 AR-04) cannot reach the analyst from here.
*Trace*: FR-01, FR-25; E01, E02; Q-U5-1; U2 AR-04.

**UR-07 — Staging artifacts.** The "Artifact Upload" panel opens the browser's file dialog, by click or by Enter or
Space while it has focus, for any number of files of any type.

- Picked files are added to the staged list in order. A file whose name and size equal a staged one's is not added
  again.
- The list reads "<n> File(s) Staged" and shows each file's name and size, with a remove button labelled "Remove
  <name>".
- The files stay in the page (`airlock.js`); the session knows only their names and sizes. Nothing is read or sent
  until Execute.
- The list survives moving between pages and is lost on reload.

*Trace*: FR-25; E02; plan decision 2; ADR-0001.

**UR-08 — Checks before uploading.** Before a submission with artifacts, the page compares the staged list with the
server's limits, `Limits.MaxArtifactsPerScan` and `Limits.MaxBytesPerScan`. Too many files, or too many bytes in all,
is refused at once and nothing is sent. The message is the Application's own — "At most 25 files per scan", "Uploads
are limited to 2 GB per scan" — since the Application provides these refusals, so that the page and the API cannot
word them differently. The empty panel states the limits: "Up to 25 files, 2 GB in total". Every other check is the
server's: readiness, the active slot, the path. *Trace*: FR-02; E02 "Upload limits are enforced"; U4 WR-08; plan
decision 3.

**UR-09 — Execute.** "Execute Security Audit" is enabled only when all of these hold:

- the pill is ONLINE;
- nothing holds the server's active slot — neither a reservation nor a running scan, whoever's;
- this tab is not uploading.

While the slot is held, Execute reads "Vetting Sequence Active..." and is disabled. When Airlock is DEGRADED, it is
disabled and shows under it "Airlock is not ready: " followed by the failing checks' details; while LOADING, "Checking
system status...". Under OFFLINE, the bar of UR-05 says why. *Trace*: FR-25; E04 "A missing NVD key blocks scanning";
plan decision 6.

**UR-10 — Submitting.** Execute clears the tab's notice and terminal (UR-18) and submits through the API (ADR-0001).
The page's script posts `POST /api/v2/scans` — `targetPath`, or one `artifacts` part per staged file — and returns the
answer.

- **202**: the tab records the scan as submitted by it, and follows it (UR-13).
- **A problem** — 400, 403, 409, 413, 415 or 503: its `detail` becomes a refusal notice, such as "A scan is already in
  progress", "NVD API key not configured", "Target path does not exist", or "Emergency Stop ended the upload before
  the scan started".
- **No answer** — the connection dropped, or the server stopped: "Upload interrupted — check the connection and try
  again".

The staged list is kept in every case. *Trace*: FR-02, FR-25; E01, E02, E04; ADR-0001; U4 WR-06 to WR-09; plan
decision 4.

**UR-11 — Upload progress.** While a submission with artifacts sends its body, Control Center shows a progress bar and
"Transferring artifacts: 45% (450.0 MB of 1.0 GB)", updated at least once a second while bytes move. Reports shows the
same line (UR-22).

The upload belongs to the session, so it carries on while the analyst opens another page. It ends only with the
server's answer, a dropped connection, Emergency Stop, or the tab closing. Other tabs read "Receiving artifacts…" for
as long as the reservation receives it. A directory scan shows no bar. *Trace*: FR-25; E02 "Upload a manifest and a
container image archive"; N2; plan decision 5.

**UR-12 — The inline notice.** A tab has at most one notice (`domain-entities.md`). It shows under Execute on Control
Center and under the header on Reports. It can be:

- a refusal;
- an interrupted upload;
- a failed or a cancelled scan;
- a finished scan to view;
- the Rescan hint.

A newer notice replaces an older one; ✕ dismisses it; Execute clears it. *Trace*: FR-25 ("inline error on failure");
E04; plan decisions 4, 7 and 22.

## Following a scan

**UR-13 — Every tab follows the active scan (Q-U5-2).** Each tab watches the server's active slot (`WatchActiveScan`).

- **While a reservation holds the slot**, the tab shows the Preparing view. It reads "Receiving artifacts…" while an
  upload is received; this tab's own upload shows its transfer progress instead.
- **Once a scan holds the slot**, the tab follows it (`WatchScan`), whoever started it: this tab, another tab or the
  API. Its current scan becomes that scan. The snapshot comes first, then the phase changes and the progress, up to
  the finished event.
- **A tab that opens, or reloads, while a scan runs** picks it up the same way.

The tab ignores the scan's own log events: its terminal shows the Live Log, which carries them (UR-17).
*Trace*: FR-17, FR-25; E04, E08; Q-U5-2; ADR-0001; services.md (streams).

**UR-14 — How a scan ends on screen.** When the followed scan finishes, the timer stops at its duration, and:

- **Completed or Incomplete**: the tab that submitted the scan opens `/reports/{id}` (FR-28), wherever the analyst is.
  Every other tab shows the snackbar "Scan COMPLETED — View report", or "Scan INCOMPLETE — View report", and the same
  notice.
- **Failed**: every following tab shows "Scan failed: <reason>" as a snackbar and as its notice; the analyst stays
  where they are.
- **Cancelled**: every following tab shows "Scan cancelled: <reason>" the same way. The reason is "Emergency Stop",
  "Cancelled by request" or "Server shutting down" (U2).

A tab that submitted a scan and was reloaded meanwhile is no longer its submitter, and offers "View report" like the
others. *Trace*: FR-15, FR-28; E06, E08, E09, E10; Q-U5-2; plan decision 7.

**UR-15 — The elapsed timer.** "Elapsed Time", on Control Center and Active Logs, counts from the followed scan's start
in whole seconds: `mm:ss`, and `h:mm:ss` from one hour. It stops at the scan's finish and keeps that value until the
next scan starts. Before the tab follows any scan, it reads `00:00`. It counts the scan, not the upload before it, so
every tab shows the same time. *Trace*: FR-31; E07, E08; plan decision 26.

**UR-16 — Progress.** Under the timer, and under Reports' spinner, the tab shows where the scan is: "SBOM Generation";
then "Correlating 40 of 87" once the correlation has counted its packages; then "Summary". Before a scan exists, it
shows the Preparing line (UR-11, UR-13). *Trace*: FR-17, FR-25; E08 "Correlation progress".

## The terminal

**UR-17 — The Live Log in the terminal.** The terminal, "Global Terminal", shows the Live Log (`WatchLiveLog`): first
the lines the server replays, then each line as it is written, from every scan and every tab (defect 4).

- A line reads `14:01:32 INFO 📥 Processing 1 uploaded artifact(s)`: the time in the browser's zone, the level's name
  as `LOG_LEVEL` spells it, and the message as written, emoji included.
- WARNING lines are yellow, and ERROR and CRITICAL lines red; each line still carries its level's name.
- The view follows the newest line, unless the analyst has scrolled up; it follows again once they are back at the
  bottom.
- The tab keeps a bounded number of lines and drops the oldest. U5 NFR Design fixes the number.
- With no lines, it reads "📡 Standing by for telemetry...".
- Its header minimizes and restores it ("Minimize terminal", "Restore terminal"), by click or keyboard.

Control Center docks a small terminal at the bottom right; Active Logs shows it at full height. *Trace*: FR-18,
FR-27; E08; plan decision 10.

**UR-18 — A fresh terminal for each scan.** When a scan starts, the terminal drops every line that does not belong to
that scan and keeps the lines written afterwards. A scan starts for a tab when it executes one, or when it sees a new
reservation or scan take the active slot. So the terminal starts fresh in every tab, and no line of the new scan is
lost to the order in which its first lines and the slot's change arrive. A tab that opens while a scan runs keeps the
replayed lines. *Trace*: FR-27; E08 "The terminal starts fresh for each scan"; Q-U5-2.

**UR-19 — Export Logs.** Export Logs saves the terminal's lines as shown, one per line. A first line names Airlock's
version, the export time with its UTC offset, and the time zone: `# Airlock 2.0.0 Live Log, exported 2026-10-22
14:05:00 UTC+02:00 (Europe/Berlin)`. The file is `airlock_audit_<UTC time>.log`, with the time written as in the PDF's
name: `2026-10-22T12-05-00-000Z`. It is built from the tab's lines; the server is not asked. *Trace*: FR-27; E08
"Export the log"; plan decision 11.

**UR-20 — Emergency Stop.** The terminal's Emergency Stop is enabled while anything holds the active slot, a
reservation included. It opens the dialog "CRITICAL: Terminate the active scan?" with Cancel, focused first, and
Terminate. Terminate calls `EmergencyStop`, which ends the scan or upload holding the slot, whoever started it (U2).
What follows is reported where it happens:

- a stopped scan ends Cancelled with "Emergency Stop" (UR-14);
- a stopped upload answers its own tab "Emergency Stop ended the upload before the scan started" (UR-10);
- if nothing was active any more, a snackbar says "No active scan".

Cancel, Escape, or a click outside the dialog closes it and changes nothing. *Trace*: FR-16, FR-27; E09; plan
decision 12.

## Active Logs

**UR-21 — Active Logs.** The page shows:

- "System Vulnerability Scan";
- the line "Node: <machine name> // Mode: Tactical Analysis", naming the machine Airlock runs on — in a container, its
  host name;
- the elapsed timer (UR-15);
- the terminal at full height.

*Trace*: FR-26; E08; plan decision 13.

## Reports

**UR-22 — Which scan Reports shows, and how.** `/reports/{id}` shows that scan: from the session while the tab follows
it, otherwise from `GetScan`. `/reports` shows the tab's current scan.

- **Preparing or Running**: the spinner, "Vetting In Progress", "Building Security Matrix...", and the progress (UR-11,
  UR-16). The analysis appears when the scan finishes, without a reload.
- **Completed or Incomplete**: the analysis (UR-23 to UR-30).
- **Failed or Cancelled**: the header, then "Scan failed: <reason>" or "Scan cancelled: <reason>", with no cards or
  grid.
- **An id no scan answers to** — it never existed, was pruned after 24 hours or beyond 20 scans, or was lost in a
  restart: "Scan not found or no longer retained" and "Back to Control Center". A malformed id is an unknown address
  (UR-39).
- **No current scan**: "No audit data available. Execute a scan to begin."

A refresh of `/reports/{id}` shows the same scan while it is retained. *Trace*: FR-19, FR-28; E10 "The result
survives a refresh"; plan decision 14.

**UR-23 — The header.**

- **Title**: "Post-Scan Analysis".
- **Status badge**, in its value's capitals: COMPLETED, INCOMPLETE, FAILED or CANCELLED.
- **Target**: "Target: /uploads/acme-app"; for artifacts, "Target: requirements.txt", or "Target: requirements.txt and
  1 more artifact", "… and 2 more artifacts", naming the first artifact as the scan lists it.
- **Time line**, in the original's form: `2026-10-22 14:01:32 UTC+02:00 // SCAN TIME: 92.42S`. It gives the finish time
  and the duration in seconds with two decimals; the original printed the time the page was drawn.

The header's actions follow (UR-32, UR-33). *Trace*: FR-28, FR-31; E10; plan decision 15.

**UR-24 — The cards.** Total Packages (`TotalPackages`), Critical Findings (`CriticalFindings`, its dot pulsing above
zero), High Findings (`HighFindings`) and Clean Packages (`CleanPackages`) show the summary's own numbers; the UI
computes none. The original derived Clean as total minus vulnerable, which counted unverified packages as clean.
*Trace*: FR-14, FR-28; E06, E10 "Cards count findings and packages"; plan decision 16.

**UR-25 — The Unverified banner.** Under the cards, an Incomplete scan shows "INCOMPLETE — 12 packages could not be
verified and are not counted as clean", or "1 package … is not counted as clean". It then lists the unverified
packages as "<name> <version> — <reason>": "jinja2 2.10 — lookup failed: NVD returned 503", "syft UNKNOWN — version
unknown". It lists five, then "Show all (12)", which toggles to "Show fewer". The banner cannot be dismissed.
*Trace*: FR-11, FR-28; E06 "Some lookups fail"; ADR-0002; plan decision 17.

**UR-26 — Warnings.** A scan with warnings shows the collapsed panel "Warnings (3)". Opened, it lists each warning's
message as the scan states it: "No packages identified in notes.docx", "Distribution backports are not considered for
alpine-3.20.tar". A scan without warnings shows no panel. *Trace*: FR-06, FR-12, FR-21; E02, E05; plan decision 17.

**UR-27 — The inventory grid.** "Inventory Matrix" lists the scan's vulnerable packages, then its unverified ones, in
the scan's order (`InventoryRow`, `domain-entities.md`). Its columns are Risk, Package Name, Version, Method and
Findings.

- **Risk** is the package's highest severity — CRITICAL even when a HIGH finding is listed first (defect 2) — or
  UNVERIFIED.
- **Method** is "CPE match" or "Product match" (defect 1); empty for an unverified package.
- **Findings** is the number of findings in two digits, "02"; empty for an unverified package.

Clean packages are not listed: the scan does not name them, and the cards count them. A scan without vulnerable or
unverified packages shows "No HIGH or CRITICAL findings: every package is clean." in the grid's place, or "No packages
identified in the scan target." when it found none. *Trace*: FR-28; E10 "Risk shows the highest severity", "Product
matches are labelled"; plan decision 18.

**UR-28 — The filter.** "FILTER BY PACKAGE OR CVE..." keeps the rows whose package name, or one of whose CVE ids,
contains the text typed, ignoring case. An empty filter keeps every row. When nothing is kept, the grid reads "No
matching assets found in project inventory." *Trace*: FR-28; E10 "Filter by CVE id".

**UR-29 — Selection.** A row is selected by a click, or by Enter or Space while it has focus. The selected row is
highlighted and its details open (UR-30). Selection follows the package's identity — type, name and version — so
requests 2.25.1 and requests 2.31.0 are different rows; the original compared names only. The filter keeps the
selection while its row shows, and clears it otherwise. Selecting the selected row again keeps it selected.
*Trace*: FR-28; E10 "Two versions of the same package"; plan decision 19.

**UR-30 — The detail pane.** The pane reads "Vulnerability Details" and "Active Analysis: pyyaml 5.3 (python)", then
how the package was matched: "Matched by CPE: cpe:2.3:a:pyyaml:pyyaml:5.3:*:*:*:*:*:*:* (generated)", or
"(dictionary)", or "Matched by product: flask". Each finding follows, in the scan's order — the highest severity and
score first (U1) — with:

- its CVE id, linking to the CVE's NVD page in a new tab that gets no access back to Airlock;
- COPY, which copies the description (UR-31);
- "CVSS v3.1 Score · NVD", or "· CNA" when the score is the reporting CNA's, with the score, its severity ("9.8
  CRITICAL"), and a bar filled to the score's share of 10;
- the description, in quotes.

An unverified package shows "Not verified — lookup failed: NVD returned 503". ✕ ("Close details") or Escape closes the
pane and clears the selection. Below 960 pixels, the pane sits under the grid. *Trace*: FR-13, FR-28; E05 "The CVSS
version is chosen and shown"; E10; plan decision 20.

**UR-31 — Copying.** COPY puts the CVE's description on the clipboard and says "Description copied". The browser
offers no clipboard to a page that is not a secure context, such as a page over plain HTTP from a host other than
localhost. There the page copies through a hidden text field, and says "Copy failed — select the text and copy it" if
that fails too. *Trace*: FR-28; plan decision 20.

**UR-32 — Exports.** Export JSON and Export PDF are links to `GET /api/v2/scans/{id}` and `GET
/api/v2/scans/{id}/report.pdf` (ADR-0001), so the files are byte for byte the API's answers (E11). The JSON is saved as
`airlock_audit_<finish time>.json`, with the time written as in the PDF's own name: `2026-10-22T12-01-32-418Z`. Both
are disabled while the scan runs. Failed and Cancelled scans have no report, so Export PDF is disabled for them while
Export JSON is not. *Trace*: FR-21, FR-22; E11 "No export while the scan runs"; U4 WR-11, WR-14; plan decision 21.

**UR-33 — Rescan.** "Rescan Project" submits the shown scan's target again, under the rules of Execute (UR-08 to
UR-11), and is enabled under the same conditions (UR-09). The target is:

- **a directory**: its path;
- **artifacts this page uploaded**: the same files, which the page keeps for every upload it submitted until it is
  reloaded;
- **artifacts the page no longer holds** — it was reloaded, or the scan came from another tab or the API: Control
  Center opens with the notice "Re-select the artifacts to rescan: requirements.txt, app-image.tar".

After submitting, Reports shows the new scan: the spinner while the files transfer and the scan runs, then its
analysis. Its address becomes `/reports/<new id>` once the server has accepted the scan. A refusal shows as the notice
under the header, above the scan shown. *Trace*: FR-29; E10 "Rescanning uploads after a refresh"; plan decision 22.

## Presentation

**UR-34 — Times, durations and sizes.**

- **Times** show in the browser's time zone, which the page's script reports when the tab connects:
  `yyyy-MM-dd HH:mm:ss` with the offset (`UTC+02:00`) in the Reports header and the log export, and `HH:mm:ss` in the
  terminal. Until the zone is known, times show as "—". A zone .NET does not know falls back to UTC.
- **Durations**: the timer as UR-15 says; the header in seconds with two decimals.
- **Sizes**: in B, KB, MB or GB, in powers of 1,024, with one decimal above bytes: "56 B", "12.4 KB", "1.0 GB". The
  limits' wording is the Application's (UR-08).

*Trace*: FR-31; plan decision 26.

**UR-35 — Wording.** The text on screen is the copy catalogue of `domain-entities.md`: the original's tactical wording,
with the changes marked there — "Artifact Upload", the findings cards, the Emergency Stop question and the Method
values. Statuses, phases and severities appear in the API's capitals: COMPLETED, INCOMPLETE, CRITICAL. *Trace*: FR-30;
Q14; plan decision 24.

**UR-36 — Theme.** One dark theme, with the tokens of `domain-entities.md`: the original palette, Inter and JetBrains
Mono, labels in capitals with wide spacing, and the original's layout. Fonts and icons come from Airlock itself (U4
hand-off), so the UI works on a network without internet access. *Trace*: FR-30; N4, N5; ADR-0005; plan decision 25.

**UR-37 — Keyboard and colour.** Every action can be reached with Tab, in reading order, and shows a visible focus:
links, buttons, the artifact panel, the grid's rows, the dialog, the pane's ✕ and the terminal's toggle. Buttons
answer Enter and Space. The dialog keeps the focus inside until it closes, then returns it to Emergency Stop. Colour
is never the only signal: severity and status badges carry their text, notices their words, and the pill its state's
name. *Trace*: NFR-12; E07 "Keyboard operation", "Severity is readable without colour"; plan decision 27.

**UR-38 — Motion.** Pages fade in briefly, the detail pane slides in, and the Critical card's dot pulses — the
animations the original intended but never ran (defect 17). None of them runs, the pulse included, when the system
asks for reduced motion. *Trace*: E07 (Could); defect 17; plan decision 28.

**UR-39 — Errors and unknown addresses.** An address the UI does not know, such as `/nowhere` or
`/reports/not-a-guid`, answers 404 and shows "Page not found" and "Back to Control Center". An unexpected error in a
page shows "Something went wrong" with Reload, never the exception, which the console log keeps (U4 infrastructure
design). Errors under `/api` stay the API's problems (U4 WR-04). *Trace*: U4 hand-off; plan decision 29.

**UR-40 — Widths.** The layout is the original's, for desktop widths: the top bar; a 256-pixel drawer; Control
Center's two panels side by side, with its small terminal floating at the bottom right from 1,280 pixels and under the
page below that, where it would cover Execute; and Reports' grid with the detail pane beside it. Below 960 pixels, the
drawer opens from a menu button in the top bar, the panels stack, and the detail pane goes under the grid.
*Trace*: FR-30; plan decision 30.

## Boundaries

**UR-41 — What the UI may use.** Components and the session call the Application's use cases in-process — `GetScan`,
`WatchScan`, `WatchActiveScan`, `WatchLiveLog`, `EmergencyStop`, `GetSystemHealth` — and read the limits from its
options. No Web type but the composition root uses an Infrastructure type (architecture rule 5, already enforced).
They reach HTTP only through the page's script, for the submission, and through links, for the exports (ADR-0001).
Subscriptions end with the component or session that made them (U4 hand-off). *Trace*: NFR-02; ADR-0001, ADR-0004;
U4 WR-23.
