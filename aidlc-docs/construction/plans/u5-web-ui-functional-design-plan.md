# U5 Web UI — Functional Design Plan

**Stage**: CONSTRUCTION — U5 Web UI — Functional Design (Standard depth)
**Started**: 2026-10-25T01:28:56Z

## Inputs

- Requirements FR-24 to FR-31, with FR-02, FR-14, FR-21 and FR-22; NFR-03, NFR-05, NFR-12
- Stories E07, E08, E10 and E12, which U5 completes, and the scenarios of E01, E02, E04, E06, E09 and E11 that the
  story map gives U5
- `inception/application-design/components.md` (the UI rows), `component-methods.md` (UI section), `services.md`
  (the tab's session, the streams, health) and `unit-of-work.md` (U5)
- ADR-0001 (the UI works in-process; uploads and exports go over HTTP), ADR-0002 (fail closed) and ADR-0005
  (MudBlazor themed to the original)
- The U2 use cases the UI calls — `GetScan`, `WatchScan`, `WatchActiveScan`, `WatchLiveLog`, `EmergencyStop`,
  `GetSystemHealth` — and U4's hand-off (`u4-web-api/nfr-design/logical-components.md` and its code generation
  summary): same-origin submission, the count and size checks before uploading, exports as links, nothing at `/` yet,
  rule 5 covering the UI, subscriptions disposed with their components
- The original React UI at `JJWren/Airlock` master, read for its layout and wording: the views `ControlCenter`,
  `ActiveLogs` and `Reports`; the components `Navbar`, `Sidebar`, `ConnectionStatus`, `Terminal` and
  `VulnerabilityRow`; `tailwind.config.js`

## Steps

- [x] Resolve the open questions below (answered: A, A)
- [x] `construction/u5-web-ui/functional-design/domain-entities.md` — the UI's own types: the tab's scan session and
      what it holds, staged artifacts, terminal lines, the status pill's states, the view of a scan each page shows;
      the copy catalogue (every on-screen text and where it comes from); the theme tokens
- [x] `construction/u5-web-ui/functional-design/business-rules.md` — the UR rules: shell and navigation, the status
      pill, Control Center (target, staging, checks before uploading, Execute, upload progress, inline messages),
      following a scan, the Terminal and Emergency Stop, Active Logs, Reports (states, header, cards, banner,
      warnings, grid, filter, selection, detail pane, exports, Rescan), time display, keyboard and colour, errors
- [x] `construction/u5-web-ui/functional-design/business-logic-model.md` — executing with an upload, following a scan
      to its end, Emergency Stop and Rescan as sequences; the status pill and Execute as state tables (validated
      Mermaid, with text alternatives); the screens of the worked example scan; the `airlock.js` contract; the
      rule-to-test map with the defect regressions U5 owns (1, 2, 16, 17)
- [x] Bring `component-methods.md`, `components.md`, `services.md` and, where a decision needs it, the requirements in
      line
- [x] Present for approval (approved 2026-10-26T01:34:06Z)

## Questions

### Q-U5-1 — A directory path and staged artifacts at the same time
Control Center shows both inputs at once, and the path is pre-filled with `/uploads`, so an analyst who stages files
has in effect entered a path as well. The original then scanned only the files. The rebuild's API refuses a request
that carries both ("Choose a directory or upload artifacts, not both", U2 AR-04), so the page must decide what it
sends.

A) Staged artifacts win: while any file is staged, Execute uploads only the artifacts, and the path field is dimmed
   with the note "Staged artifacts are scanned instead of this path"; removing the files brings the path back — the
   original's behaviour (Recommended)
B) Send what is entered: with files staged and a path present, the analyst gets the refusal "Choose a directory or
   upload artifacts, not both" and must clear one of them
C) A Directory | Artifacts switch above the two panels; only the chosen panel is active and sent
D) Other

[Answer]: A

### Q-U5-2 — Tabs other than the one that started the scan
One scan runs at a time, Emergency Stop works from every tab, and every tab receives the Live Log. What should a tab
show of a scan that another tab, the API, or this tab before a reload started?

A) Every tab follows the active scan, whoever started it: Control Center and Active Logs show its phase, progress and
   timer, Reports its spinner, and the terminal starts fresh when it begins. When the scan ends, the tab that submitted
   it opens the report and the others offer "View report". A reloaded tab picks a running scan up again (Recommended)
B) Only the tab that submitted the scan follows it; other tabs show that a scan is active (Execute disabled, Emergency
   Stop enabled) and the Live Log, nothing more. A reloaded tab loses the scan, though `/reports/{id}` still shows it
C) As A, but every following tab opens the report when the scan ends
D) Other

[Answer]: A

## Decisions taken in this design (object at the gate if any is wrong)

1. **One session per tab.** Each browser tab (one Blazor circuit) has its own session: the directory path, the staged
   artifacts, the scan it follows, its terminal lines and its time zone. Moving between Control Center, Active Logs and
   Reports keeps all of it (E07). A reload starts a new session, in which the Live Log replays its last 200 lines.
2. **Staged files stay in the page.** File contents never cross the circuit: `airlock.js` holds the selected files for
   the life of the page, and the session holds only their names and sizes. A new selection adds to the staged list —
   a file of the same name and size is not added twice — and each row has a remove button. The original replaced the
   list on every selection.
3. **Checks before uploading** (U4 WR-08). More than `Limits.MaxArtifactsPerScan` files, or more than
   `Limits.MaxBytesPerScan` in total, is refused on the page with the API's own messages ("At most 25 files per
   scan", "Uploads are limited to 2 GB per scan") before a byte is sent, because a browser may see the connection
   close before it reads a refusal sent mid-upload. Everything else is the server's to answer, from one source.
4. **Every scan is submitted through the API** (ADR-0001). `airlock.js` posts `targetPath` or the staged artifacts to
   `POST /api/v2/scans`, and the session follows the 202's scan in-process. A refusal shows its `detail` inline under
   Execute; a dropped connection shows "Upload interrupted — check the connection and try again".
5. **The upload belongs to the session.** A progress bar under Execute — "Transferring artifacts: 45% (450 MB of
   1.0 GB)" — runs until the server answers, and the upload carries on while the analyst visits another page.
   Directory scans show no bar.
6. **Execute** ("Execute Security Audit") is enabled only when the pill is ONLINE, no scan or upload is active
   anywhere, and this tab is not uploading. While a scan or upload is active it reads "Vetting Sequence Active..." as
   in the original. Otherwise a disabled Execute shows why underneath: "Airlock is not ready: NVD API key not
   configured", or "Checking system status..." while LOADING; when the connection is lost, the connection bar says so
   (decision 9). The drawer's Execute Audit opens Control Center, as in the original.
7. **How a scan ends on screen.** Completed or Incomplete opens `/reports/{id}` in the tab that submitted it; other
   tabs show a snackbar and a notice offering "View report" (Q-U5-2). Failed shows "Scan failed: <reason>", and
   Cancelled "Scan cancelled: <reason>", as a snackbar and a notice in every following tab, and the analyst stays where
   they are. The Terminal carries the pipeline's own lines in every case.
8. **The status pill** reads "System Status: ONLINE", "DEGRADED", "OFFLINE" or "LOADING", as the original's did.
   LOADING lasts until the first health answer; health is read again every 10 seconds (services.md). DEGRADED's
   tooltip lists each failing check's detail, and it opens on keyboard focus too, not on hover only. OFFLINE is set by
   the page's script, because the server cannot update a page it has lost.
9. **Connection loss** blocks nothing: the pill turns OFFLINE and a thin bar at the top says "Connection lost —
   reconnecting…"; when reconnecting fails, the bar offers Reload. Blazor's default reconnect dialog is replaced.
10. **Terminal lines** read `14:01:32 INFO 📥 Processing 1 uploaded artifact(s)`: the time in the browser's zone, the
    level as text, the message. WARNING, ERROR and CRITICAL lines are also coloured. The view follows the newest line
    unless the analyst has scrolled up, and follows again once they return to the bottom. Each tab keeps a bounded
    number of lines (NFR Design fixes it), dropping the oldest.
11. **Export Logs** saves the lines as shown, after a first line naming Airlock's version, the export time with its UTC
    offset and the time zone. The file is named like the PDF: `airlock_audit_2026-10-22T12-05-00-000Z.log`.
12. **Emergency Stop** is enabled while a scan or upload is active anywhere. Its dialog asks "CRITICAL: Terminate the
    active scan?" — the original asked "Terminate process group?", which the rebuild no longer does — with Cancel
    focused first. Terminate stops whatever holds the slot: a stopped scan ends Cancelled with "Emergency Stop", which
    every tab reports (decision 7); a stopped upload is answered to its own tab; if nothing was active any more, a
    snackbar says "No active scan". Cancel or Escape changes nothing (E09).
13. **Active Logs** keeps the original's heading and line, with the real machine name: "Node: <machine name> // Mode:
    Tactical Analysis". In a container that is its host name, which the compose file can set (U6).
14. **Reports' states.** `/reports` shows the tab's latest scan, `/reports/{id}` that scan. Running: the original's
    spinner ("Vetting In Progress", "Building Security Matrix...") with the phase and progress. Completed or
    Incomplete: the analysis. Failed or Cancelled: the header and the reason. An unknown or pruned id: "Scan not found
    or no longer retained", with a link to Control Center. No scan in this tab: "No audit data available. Execute a
    scan to begin."
15. **Reports' header**: "Post-Scan Analysis"; a status badge (COMPLETED, INCOMPLETE, FAILED, CANCELLED); the target
    ("/uploads/acme-app", or "requirements.txt and 1 more artifact"); and the finish time with the duration in the
    original's style: `2026-10-22 14:01:32 UTC+02:00 // SCAN TIME: 92.42S`.
16. **Cards**: Total Packages, Critical Findings (pulsing above zero), High Findings, Clean Packages. The original's
    "Critical Risks" and "High Risks" count CVEs, not packages, so they are named after findings (FR-14, glossary).
17. **Unverified and warnings.** An Incomplete scan shows a banner — "INCOMPLETE — 12 packages could not be verified
    and are not counted as clean" — listing each package with its reason (five, then "Show all"). The scan's warnings
    appear in a collapsible "Warnings (3)" panel under the cards, as they do in the PDF (FR-21).
18. **The inventory grid** ("Inventory Matrix") lists the vulnerable and the unverified packages — the scan holds no
    list of clean ones, and the original's table showed none either. Columns: Risk (the highest severity, or
    UNVERIFIED), Package Name, Version, Method ("CPE match" or "Product match"), Findings (two digits, as the
    original's "02"). Order: CRITICAL, HIGH, then UNVERIFIED; within each, by name and version. The filter ("FILTER BY
    PACKAGE OR CVE...") matches part of a package name or a CVE id, ignoring case. Copy: "No matching assets found in
    project inventory." when nothing matches; "No HIGH or CRITICAL findings: every package is clean." for a scan
    without vulnerable or unverified packages, and "No packages identified in the scan target." for one that found
    none.
19. **Selection by identity**: a row is selected by type, name and version (the original compared names only), by
    click, or by Enter or Space. The filter keeps the selection while its row shows and clears it otherwise.
20. **The detail pane** ("Vulnerability Details", "Active Analysis: pyyaml 5.3 (python)") adds the match: the CPE with
    its source (dictionary or generated), or the product. Each finding then shows:
    - its CVE id, linking to NVD in a new tab;
    - COPY, which copies the description;
    - "CVSS v3.1 Score · NVD" — or "· CNA" for a score from the reporting CNA — with the score, its severity and the bar;
    - the description.

    An unverified package shows its reason and detail. ✕ or Escape closes the pane.
21. **Exports** are links to `GET /api/v2/scans/{id}` and `…/report.pdf` (ADR-0001). The JSON is saved as
    `airlock_audit_<finish time>.json`, using the PDF's time format. Both are disabled while the scan runs, and Export
    PDF also for Failed and Cancelled scans, which have no report.
22. **Rescan** ("Rescan Project") re-submits the target of the scan shown: its directory, or — while the page still
    holds them — the very files that scan uploaded. Reports then follows the new scan. When the files are gone (the page
    was reloaded), Control Center opens with "Re-select the artifacts to rescan: requirements.txt, app-image.tar"
    (FR-29).
23. **Placeholders**: notifications, settings and profile are labelled icon buttons that do nothing (defect 16). The
    profile picture is a local icon rather than the original's avatar from `api.dicebear.com`, so the UI requests
    nothing from another host.
24. **Wording.** The original's on-screen copy is kept (the catalogue in `domain-entities.md`), with these changes:
    - "Manifest Upload" becomes "Artifact Upload", and the subtitle says "upload artifacts", since any file type is
      accepted (FR-01);
    - the cards (decision 16);
    - the Emergency Stop question (decision 12);
    - the Method values (decision 18).
25. **Theme**: dark only, as the original.
    - Background #09090B, surfaces #18181B, top bar #131315, drawer #1C1B1D.
    - Primary #3B82F6, error #EF4444, secondary #71717A.
    - Severity colours: CRITICAL #EF4444, HIGH #FB923C, UNVERIFIED #EAB308; clean #10B981.
    - Inter for text; JetBrains Mono for labels, in capitals with wide spacing.
    - Fonts and icons are served by Airlock itself (U4's hand-off).
26. **Times** show in the browser's time zone as `yyyy-MM-dd HH:mm:ss` with the UTC offset, and as `HH:mm:ss` in the
    terminal. The elapsed timer counts from the followed scan's start — `mm:ss`, `h:mm:ss` from an hour — stops at its
    finish, and shows `00:00` before the tab's first scan.
27. **Keyboard and colour** (NFR-12): every action is a button or link reached by Tab, with a visible focus; the
    artifact panel opens the file dialog with Enter or Space; severity badges always carry their text.
28. **Entry animations** run (defect 17): a short fade for pages and a slide for the detail pane, and none when the
    system asks for reduced motion.
29. **Errors outside the API**: an unknown address shows "Page not found" with a link to Control Center; an unexpected
    error in a page shows "Something went wrong" with Reload, never the exception, which the console log keeps.
30. **Layout** for desktop widths, as the original: the top bar; the 256-pixel drawer; on Control Center, the mini
    terminal floating at the bottom right; the Reports split pane. Below 960 pixels, the drawer opens from a menu button
    and the detail pane stacks under the grid.
