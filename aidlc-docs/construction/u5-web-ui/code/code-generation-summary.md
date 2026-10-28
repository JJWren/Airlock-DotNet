# U5 Web UI — Code Generation Summary

**Stage**: CONSTRUCTION — U5 Web UI — Code Generation, Part 2 (Generation)
**Plan**: `construction/plans/u5-web-ui-code-generation-plan.md` (approved 2026-10-27T21:56:57Z)
**Completed**: 2026-10-27T23:12:51Z · **Branch**: `feature/u5-web-ui`, from `main` fast-forwarded to the U4 commit

## Result

| Check | Target | Result |
|---|---|---|
| Build, warnings as errors (H1) | 0 warnings | 0 warnings, 0 errors |
| All tests, with Syft v1.55.0 through `SYFT_BINARY_PATH` (H2) | all pass | 1,128 of 1,131; the 3 skipped are U3's, as in U4 |
| Web tests | all pass | 373 of 373: U4's 174 and 199 new |
| Domain tests | all pass | 211 of 211, unchanged |
| Application tests | all pass | 206 of 206: U4's 196 and 10 for `UploadLimits` (B1) |
| Infrastructure tests | all pass | 270 of 273, 3 skipped, unchanged |
| Architecture tests | all pass | 68 of 68: U4's 65 and 3 for the UI's rule |
| Line coverage of `Airlock.Web` (H3, U5-NFR-14) | 90 % | 96.3 % (2,124 of 2,206 lines), Razor components included |
| Branch coverage of `Airlock.Web` (H3) | 85 % | 88.1 % (741 of 841 branches) |
| U1 to U4 still at theirs (H3) | as before | Domain 100 % and 100 %; Application 97.1 % and 95.3 %; Infrastructure 95.5 % and 87.1 % |
| The worked example on screen is test rows (H4) | every row | every row (table below) |
| The UI's architecture rule fails on a violation (H5) | — | A temporary `ArchitectureProbe` in `Airlock.Web.State` calling `HttpClient.GetStringAsync` broke `WebUiRules`, which named it; the probe was deleted and the rule passes |
| The host in the browser pane (H6) | the checks below | All passed, after two fixes found there (deviations 13 and 14) |

Coverage excludes generated code under `obj`, as for U1 to U4. What stays uncovered in the UI is mostly the session's
failure paths — a feed whose subscription throws, a call into the browser that fails while an upload starts — and
`MainLayout`'s snackbars for a failed or a cancelled scan, whose notices the session tests cover.

Run the checks with:

```bash
dotnet build Airlock.slnx
```

```bash
dotnet test --coverage --coverage-output-format cobertura
```

The component tests need no Syft, no NVD key, no browser and no network: they run the real Application and
Infrastructure with U4's fakes for Syft and NVD, a fake Live Log and a fake clock, and bUnit's stand-in for the page's
script, strict for `airlock.js`.

## The browser checks (H6)

`dotnet run` with the scratchpad's Syft v1.55.0, port 8080 checked free first, in the browser pane (Chromium):

- **First run, the NVD key emptied on the command line** (any key in user secrets went unused): the pill read
  DEGRADED, its tooltip opened on keyboard focus with "NVD API key not configured", and Execute was disabled with
  "Airlock is not ready: NVD API key not configured". The four font files loaded; every request went to
  `localhost:8080`. A keyboard pass reached the top bar, the drawer, the pill, the path, the upload panel, the terminal's
  toggle and lines, and Export Logs in reading order, skipping the disabled Execute and Emergency Stop, each with a
  visible focus ring. A cold load came to 1.44 MB (under 2 MB; MudBlazor's stylesheet is not compressed in Development)
  and the page was drawn 181 ms after navigation began (under 2 s). An unknown address answered 404 with the page shell
  and drew "Page not found".
- **Second run, a placeholder key and `Airlock:Nvd:BaseUrl` on a closed port of this machine** (127.0.0.1:47811, checked
  refused first), so scans ran and no request left the machine:
  - Two files were staged through the page's script — the S0 `requirements.txt` (56 B) and an 800 MB `big.bin` — since
    the pane cannot drive the file dialog: "2 File(s) Staged", the path dimmed with its note.
  - Execute uploaded them. The transfer line climbed from 0 % to 100 % over 6.6 seconds while the tab moved from Control
    Center to Active Logs and on to Reports, which showed the spinner with the same line. The second tab read
    "Receiving artifacts…" and "Vetting Sequence Active...", and its terminal started afresh with the scan's lines.
  - The scan went through "SBOM Generation", "Correlation" and "Correlating 0 of 4" to "4 of 4" while U3's client
    retried the closed port; every package ended unverified, and the submitting tab opened `/reports/{id}` by itself:
    INCOMPLETE, "Target: requirements.txt and 1 more artifact", the finish time in the browser's zone with its offset
    (`… // SCAN TIME: 46.47S`), cards 4, 0, 0, 0, the banner's four rows, "Warnings (1)" and
    four UNVERIFIED rows. The second tab showed "Scan INCOMPLETE — View report" as a snackbar and a notice, its timer
    stopped at 00:46.
  - The export links read `airlock_audit_2026-10-27T23-04-23-698Z.json` and `.pdf`, and both addresses answered 200
    (JSON, and the PDF under the same name). Export Logs built `airlock_audit_2026-10-27T23-07-53-594Z.log`, UTF-8 text
    whose first line names Airlock 2.0.0, the export time with its offset and the browser's IANA zone. Nothing was saved.
  - Rescan uploaded the held files again under the spinner and moved to the new scan's address. Emergency Stop, by
    keyboard from Active Logs, opened the question with Cancel focused inside the dialog; Tab and Enter on Terminate
    ended the scan Cancelled, and both tabs reported "Scan cancelled: Emergency Stop".
  - A refresh of the first report showed it again. After the refresh, Rescan opened Control Center with "Re-select the
    artifacts to rescan: requirements.txt, big.bin".
  - A directory typed into the path field scanned and opened its report: COMPLETED, "No packages identified in the
    scan target."
  - With the host stopped, the bar read "Connection lost — reconnecting…" and the pill OFFLINE at once. Started again,
    the host rejected the old circuit and the bar offered "Connection lost." with Reload, which brought the tab back
    ONLINE.
  - The console showed no Content-Security-Policy violation from Airlock's pages. The one violation recorded came from
    the check itself, which fetched the export's `blob:` address — a fetch `connect-src 'self'` rightly refuses; the
    page's own download is a link, which it does not govern.

Left for Build and Test, as planned: findings on screen and a full scan with a real NVD key, the axe-core scan, copying
over plain HTTP from another host name, reduced motion, and timings in a visible pane (the pane was hidden here).

## Files

### `src/Airlock.Web` — 46 new files

| Folder | Files |
|---|---|
| `Components/` | `App.razor` (the static shell; each page's render mode from `AcceptsInteractiveRouting`), `Routes.razor`, `_Imports.razor`, `AirlockUi.cs` (`AddAirlockUi`, `UseAirlockPages`, `MapAirlockUi`) |
| `Components/Layout/` | `MainLayout`, `NavMenu`, `ConnectionBar`, `Announcer`, `AirlockErrorBoundary`, `PlainLayout` |
| `Components/Pages/` | `ControlCenter` (`/`), `ActiveLogs` (`/logs`), `Reports` (`/reports/{Id:guid?}`), `NotFound` (`/not-found`), `Error` (`/error`) |
| `Components/Parts/` | `SessionComponent.cs`; `StatusPill`, `ArtifactPicker`, `ScanProgress`, `ElapsedTimer`, `ScanNotice`, `Terminal`, `StopScanDialog`, `SummaryCards`, `UnverifiedBanner`, `WarningsPanel`, `InventoryGrid`, `VulnerabilityDetail` |
| `State/` | `ScanSession`; `SessionState.cs` (`SessionChange`, `SessionState`, `ScanEnding`, `StagedArtifact`, `UploadProgress`, `TerminalLine`, `NoticeKind`, `InlineNotice`, `SubmissionKind`, `SubmissionOutcome`); `InventoryRow.cs` (`InventoryRow`, `VulnerableRow`, `UnverifiedRow`); `RenderCoalescer`; `UiFormat` |
| `Interop/` | `AirlockJs`; `UploadSink.cs` (`UploadSink`, `StagedFile`, `SubmissionAnswer`, `ScanSubmissionTarget`, `UploadStart`) |
| `Theme/`, `Security/` | `AirlockTheme`; `PageSecurityHeaders` |
| `wwwroot/` | `css/airlock.css`, `js/airlock.js`, `favicon.svg`; `fonts/inter/` (`InterVariable.woff2`, `Inter-BlackItalic.woff2`, `OFL.txt`) and `fonts/jetbrains-mono/` (`JetBrainsMono-Regular.woff2`, `JetBrainsMono-Bold.woff2`, `OFL.txt`), each checked against its Git blob hash at its release tag |

Changed: `Airlock.Web.csproj` (MudBlazor, `BlazorDisableThrowNavigationException`), `AirlockHost.cs` (the UI's
registrations and pipeline), `appsettings.json` (deviation 5), `Properties/launchSettings.json` (opens `/`),
`Logging/WebLog.cs` (events 3401 to 3403) and `Api/Problems/ApiExceptionHandler.cs` (deviation 4).

### `src/Airlock.Application`

`UseCases/UploadLimits.cs` (B1); `ScanReservation` takes its two limit refusals from it, with unchanged words.

### `tests/Airlock.Web.Tests` — 18 new code files

| File | Covers |
|---|---|
| `Support/UiTestContext.cs` | The component-test context (U5-NFR-13): MudBlazor's services, the Application, the Infrastructure with U4's fakes, a staging root of its own, a fake clock, a fake Live Log; `airlock.js` strict and the rest loose; helpers that plan the page's calls, start the session, stand in for the API's side of a submission and answer it, and assert while moving the fake clock on |
| `Support/FakeLiveLog.cs`, `Support/Scans.cs` | The Live Log the tests write, with a replay; scan builders, the 2,000-row scan among them |
| `Ui/ShellTests.cs` | UR-01, UR-02: the highlighted item on each address; Execute Audit opens `/`; the placeholders change neither the address nor what is shown (defect 16); no other host in the markup |
| `Ui/ScanSessionTests.cs` | UR-03, UR-13 to UR-16, UR-18: navigation keeps the session; Preparing and "Receiving artifacts…"; a scan started elsewhere followed to its end; a new session picking up a running scan with its replayed lines; the submitter opening the report; the other tabs' snackbars, COMPLETED and INCOMPLETE; Failed and Cancelled with their reasons; the fresh terminal in either order of arrival; the timer's formats and stop |
| `Ui/StatusPillTests.cs` | UR-04, UR-05: LOADING, ONLINE, DEGRADED with its details for screen readers and in a tooltip focus opens; health read again after 10 seconds of fake time |
| `Ui/ControlCenterTests.cs` | UR-06 to UR-12: the default path sent as typed; staged artifacts sent alone (Q-U5-1); a file staged twice; removal; the checks' messages equal the Application's; the Execute table; a problem's detail as the notice; an interrupted upload; the transfer line; one notice at a time |
| `Ui/TerminalTests.cs` | UR-17, UR-19 to UR-21: the line format and the six levels with their colours; the empty state; minimize; the export's first line, lines and name; Emergency Stop's enablement, the question dismissed, Terminate, "No active scan"; Active Logs' node line |
| `Ui/ReportsTests.cs` | UR-22 to UR-33: every view; the worked example's header, cards, banner and grid; the target's forms; times before the zone is known; the exports; the banner's Show all; the warnings panel; the grid's empty states; defects 1 and 2; the filter by name and CVE id, its delay, and the selection it keeps; two versions selected apart; the detail pane; copying; Rescan of a directory, of held files, and the hint after a reload |
| `Ui/PresentationTests.cs` | UR-34 to UR-38: times across `Europe/Berlin`'s change and in `America/Phoenix` under `de-DE`; the dash and the UTC fallback; sizes, durations and numbers; badges with text; keyboard reach; the motion classes (defect 17) and the reduced-motion rule |
| `Ui/ErrorPageTests.cs` | UR-39, U5-NFR-12: 404 for `/nowhere` and `/reports/not-a-guid`; "Page not found"; a failing page's boundary logging one error; an error outside a page drawn by the server, without its details or Blazor's markers; Reload only to Airlock's own paths |
| `Quality/UiResponsivenessTests.cs` | U5-NFR-02, U5-NFR-03: the coalescer; 100 lines in one burst redraw the terminal at most twice; 1,500 lines keep the last 1,000; disposing the session ends its subscriptions |
| `Quality/UiUploadTests.cs` | U5-NFR-05: submitting returns at once; progress and the answer arrive through the sink while another page is open; a session that ends during an upload answers its own wait |
| `Quality/LargeResultsSmokeTests.cs` | U5-NFR-04 at ten times the targets, in the collection that runs alone |
| `Quality/ThemeContrastTests.cs` | U5-NFR-07: every text pair at 4.5:1 and every control part at 3:1, from the theme and the stylesheet; the stylesheet carries the theme's tokens |
| `Quality/UiAccessibilityTests.cs` | U5-NFR-07, U5-NFR-08: every button named; rows focusable with `aria-selected`; the announcer repeating notices; external text shown as text |
| `Quality/PageHeaderTests.cs` | U5-NFR-08, U5-NFR-11: the headers on the pages, on `/docs` and `/openapi/v2.json`, and U4's on `/api`; the shell's fingerprinted files from Airlock only, no import map, no inline script |
| `Quality/UiLoggingTests.cs` | U5-NFR-12: following a scan logs no UI line at Information; a failed call into the browser is logged at Error and the session falls back to UTC |

Changed: the project (bUnit; the shared framework, refinement 3), `Host/HostCompositionTests.cs` and
`Contract/ProblemCustomizationTests.cs` (deviation 6).

### Other projects

- `tests/Airlock.Application.Tests/UseCases/UploadLimitsTests.cs` — the refusals and the size wording under `de-DE`,
  and the reservation refusing in the same words.
- `tests/Airlock.Architecture.Tests` — `WebUiRules` (no `HttpClient` in `Components`, `State` or `Interop`; the UI types
  are checked; `Reports` reaches `GetScan`); the exported-types rule updated for refinement 1 (deviation 7); the project
  on the shared framework (deviation 1).
- `Directory.Packages.props` (`MudBlazor` 9.11.0, `bunit` 2.12.5), `.gitattributes` (`*.woff2 binary`).
- `.claude/launch.json` (local tooling, uncommitted as Q-CG4-3 decided) gained two run configurations for H6.

## The worked example on screen as test rows (H4)

| Worked example (U5 `business-logic-model.md`) | Test |
|---|---|
| While it runs: ONLINE; the path dimmed with its note; "1 File(s) Staged", "requirements.txt", "56 B" | `StatusPillTests.The_pill_reads_online_when_every_check_passes`; `ControlCenterTests.Staged_artifacts_dim_the_path_and_are_sent_alone`, `A_file_staged_twice_is_listed_once_with_its_size` |
| "Vetting Sequence Active...", disabled; "Correlating 2 of 4" | `ControlCenterTests.Execute_is_disabled_while_another_tab_holds_the_slot`; `PresentationTests.The_worked_example_while_it_runs_reads_correlating_two_of_four` |
| The terminal's lines | `TerminalTests.A_line_reads_the_time_in_the_browsers_zone_the_level_and_the_message`, `Each_level_has_its_name_and_its_colour` |
| A second tab shows the same; Reports shows the spinner | `ScanSessionTests.A_scan_started_elsewhere_is_followed_to_its_end_with_a_notice`; `ReportsTests.A_running_scan_shows_the_spinner_and_the_progress` |
| The submitting tab opens `/reports/{id}` | `ScanSessionTests.The_tab_that_submitted_a_scan_opens_its_report` |
| The header, the time line, the exports' names | `ReportsTests.The_worked_example_header_cards_banner_and_grid`, `The_exports_are_the_apis_answers_saved_under_the_pdfs_name` |
| Cards 4, 2 (pulsing), 3, 1; the banner; no warnings panel | `ReportsTests.The_worked_example_header_cards_banner_and_grid` |
| The grid's three rows | the same test; `A_product_match_is_labelled_as_one` (defect 1) |
| "cve-2020-1747", "FLASK", "2.10" | `ReportsTests.The_filter_keeps_rows_by_name_or_cve_id_ignoring_case`, `Versions_are_not_searched` |
| Selecting pyyaml, flask and jinja2 | `ReportsTests.The_detail_pane_shows_the_match_each_finding_and_its_link`, `The_detail_pane_names_the_product_the_cvss_version_and_the_cna`, `An_unverified_package_says_why_and_closing_clears_the_selection` |
| Every other tab: "Scan INCOMPLETE — View report", the notice; the timer at `01:32` | `ScanSessionTests.Every_other_tab_gets_the_snackbar_and_the_notice(INCOMPLETE)`; `The_timer_stops_at_the_finish` |

## Deviations from the design and the plan

1. **Sixteen more packages were downloaded than planned: 3.26 MB.** They are Microsoft's 10.0.1 ASP.NET Core and
   Microsoft.Extensions packages, which MudBlazor names as dependencies. `Airlock.Architecture.Tests`, which references
   the host without the shared framework, restored them; the planning measure had missed that project. That project now
   references `Microsoft.AspNetCore.App` too, as refinement 3 did for the Web tests, so the packages are pruned again
   and unused. The approved downloads were 7 packages, 15.2 MB; the restore fetched those 7 plus these 16.
2. **`Airlock.Web.Components.Parts`, not `.Shared`.** CA1716 refuses `Shared` in the namespace of a public type, being a
   Visual Basic keyword, and refinement 1 makes `SessionComponent` public.
3. **The pages' error handling sits in the main pipeline, ahead of U4's `/api` branch, not in a branch of its own.**
   `WebApplication` withholds its router from branches, so the status-code pages re-executed `/not-found` without
   routing it and answered an empty 404. `UseAirlockPages` adds `PageSecurityHeaders`, the exception handler outside
   Development and the status-code pages before `UseAirlockApi`. Under `/api` the API's problems are written first,
   inside, so they find nothing left to do; `PageSecurityHeaders` skips `/api` itself, which keeps U4's headers.
4. **U4 amendment: `ApiExceptionHandler` handles `/api` only.** Every exception handler middleware asks the host's
   `IExceptionHandler` services first, so the pages' handler answered with the API's problem until it declined.
5. **`DisableStaticAssetNotFoundRuntimeFallback` is set in `appsettings.json`.** Run from its build output
   (`dotnet run`, the tests), `MapStaticAssets` adds a development fallback, `{**path:file}` for GET and HEAD, which
   serves files added after startup. As a GET candidate for every address, it turned the API's 405 for a wrong method
   into 404. A published app has no such fallback.
6. **Two U4 tests adjusted.** `HostCompositionTests` names its hand-built application as `Program`'s, since
   `MapStaticAssets` looks for the manifest under the application's name; the unit test of `ApiExceptionHandler` gives
   its request an `/api` path, and a new test shows a page's exception is declined.
7. **The exported-types rule** now allows the components and Razor's generated `_Imports` class besides `Program`
   (refinement 1), and checks every exported component lives under `Airlock.Web.Components`.
8. **`Reports`' route parameter is `Id`**, `/reports/{Id:guid?}`; a property named `ScanId` would hide the type.
9. **The session's shape grew** (component-methods.md shows it): `SessionState.Submitting` (a path submission has no
   upload to show but still holds Execute); `ExecuteAsync` and `RescanAsync` return a `SubmissionOutcome`, which
   `Reports` uses to move to the new address or to Control Center; `Announce` and `Announced` feed the announcer; the
   sink takes three callbacks; `AirlockJs` reports a failed call instead of throwing, and takes the module's address
   through `UseModuleAddress`. The first slot state a session sees never starts its terminal afresh, and a tab that is
   submitting has started afresh already.
10. **`UploadLimits` has two internal factories**, `TooManyArtifacts` and `TooLarge`, for `ScanReservation`'s checks
    before each file and while each one streams.
11. **Links to Control Center use `/`.** `MudButton` draws an empty `Href` as a plain button, so Execute Audit and both
    "Back to Control Center" buttons would have done nothing. Found by `ShellTests`.
12. **Copy added**: page titles ("Control Center · Airlock" and so on, WCAG's titled pages), "Dismiss notice" and "Menu"
    for screen readers, "Retry" on the connection bar after a failed reconnect (NFR Design's Retry, which the copy
    catalogue lacked), and "Correlation" for the moment before the count is known. Transfer sizes follow UR-34's one
    decimal: "450.0 MB" where UR-11's example said "450 MB".
13. **Control Center's terminal floats at the bottom right from 1,280 pixels**, and sits under the page below that: at
    1,024 pixels it covered Execute and its reason. Found in H6.
14. **While OFFLINE, Execute, Rescan and the terminal's actions are dimmed and inert**, through the stylesheet on
    `html[data-connection]`; the design called them unreachable. Found in H6, as was a one-render flash of the old
    analysis after Rescan's acceptance, fixed by keeping the spinner until the new address.
15. **The warnings panel is a native `<details>`, and the score bar Airlock's own markup**, simpler and accessible
    without script.
16. **Focus after Emergency Stop** returns to the button, which is disabled once the slot is free, so the browser leaves
    it on the page.
17. **UI log events are 3401 to 3403**, after U4's 3100 to 3399.

## Constraints handed to U6

The constraints of `nfr-design/logical-components.md` stand. Code Generation adds:

- The UI is served compressed and fingerprinted only when published; a container built from `dotnet publish` gets both,
  and no development fallback (deviation 5).
- A Production run straight from the build output does not serve package and framework static assets (static web
  assets are a Development feature); run published, or in Development, as the launch profile does.
- `.claude/launch.json`'s run configurations hold this machine's scratchpad path for Syft; they stay uncommitted.
- Build and Test re-measures page weight and start in a visible pane, runs axe-core on the three pages, copies over plain
  HTTP from another host name, checks reduced motion, and scans with a real NVD key against the worked example.
