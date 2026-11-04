# Build and Test Summary

**Stage**: CONSTRUCTION — Build and Test (Standard depth)
**Plan**: `construction/plans/build-and-test-plan.md` (approved 2026-10-30T16:42:36Z)
**Ran**: 2026-10-30 (the steps needing no key) and 2026-11-02 (the keyed steps) · **Branch**: `feature/u6-delivery`, on
the U6 commits `19cdae4` and `28c7a36`; this stage's changes are in `542f1d6` and `58c0c1e`, and defects 9 to 11, found
by CI's first two runs on 2026-11-03, after them

## Result

| Step | Target | Result |
|---|---|---|
| A1 The key | Stored by the owner, never read | In `.env` and in user-secrets, by the owner; read by Airlock only, never by these checks |
| A2 The commits (Q-BT-2 = A) | Two commits, nothing pushed | `19cdae4` (U6 design documents) and `28c7a36` (U6 code) |
| C1 Build | 0 warnings | Debug and Release: 0 warnings, then again after this stage's fixes |
| C2 Quick start from a fresh clone of `28c7a36` | The README as written; the UI on port 8080 | Compose built the image (the pull is refused before the first release); healthy 45 s after `up`; the page 200 and DEGRADED with no key. The page never became interactive: defect 1, found in F6 |
| C3 The install scripts | Download and check Syft v1.55.0; refuse a corrupted archive | Windows and Linux both install it, refuse a tampered archive with exit code 1 and install nothing, and find it already installed on a second run |
| D1 Every test, with coverage | All pass; coverage targets | After this stage's fixes: 1,253 tests, 1,250 passed, 3 skipped by design (the live NVD test, a Linux-only test, a symbolic-link test); coverage below |
| E1 The live NVD test | pyyaml 5.3's two CVEs, no key | Passed, one request |
| E2 E14 "Run locally on Windows" | ONLINE; the worked example scanned | ONLINE in health and in the browser; COMPLETED: 4 packages, 3 vulnerable, 1 clean |
| E3 E14 "Run with Docker Compose" | ONLINE; scanning `/uploads` scans the host folder | ONLINE with Syft 1.55.0 and the key configured; `/uploads` scanned as the worked example, the same result |
| E4 E14 "Same results on both platforms" | The same packages and findings | Identical, below |
| E5 Security | Refusals as designed, no stack trace | All as designed, below |
| E6 Stopping the container during a scan | Cancelled within Docker's 10 s; nothing left | `docker compose stop` in 1.1 s, exit code 0; the scan stopped in SBOM generation; only `.airlock-lock` in the staging root, after the stop and after the restart |
| E7 The end-to-end pass in the browser | Every item of `integration-test-instructions.md` | All pass, after defects 7 and 8 were fixed |
| E8 Accessibility | No serious or critical axe-core finding; keyboard; reduced motion | After defects 3 to 6: no axe-core violation of any impact on six page states; keyboard and reduced motion as designed |
| E9 Copying over plain HTTP (Q-BT-3 = A) | The fallback copies | "Description copied" on a page that is not a secure context |
| F1 to F6 Performance | The targets | All met, below |
| G1 Clean-up | This stage's Docker objects and the fresh clone removed | Both Compose projects with their volumes, the two images built here, the builder `airlock-dotnet` and its state, and the fresh clone; the pulled images kept |
| Linters | No unaccepted finding | actionlint and shellcheck: none; zizmor offline: none (1 accepted in `.github/zizmor.yml`) |

### Coverage (D1)

| Project | Lines | Branches | Target |
|---|---|---|---|
| `Airlock.Domain` | 100.0 % | 99.3 % | 95 / 90 |
| `Airlock.Application` | 97.1 % | 94.7 % | 90 / 85 |
| `Airlock.Infrastructure` | 95.6 % | 85.5 % | 85 / 80 |
| `Airlock.Web` | 96.3 % | 86.6 % | 90 / 85 |

Tests by project: Domain 212, Application 207, Infrastructure 276 (3 skipped), Web 404, Architecture 68, Delivery 86.

### E4: the worked example on both platforms

The scan from source on Windows and the scan in the container agree in status (Completed), summary, warnings (none),
unverified packages (none), and every finding:

| Package | Match | Findings |
|---|---|---|
| pyyaml 5.3 | CPE `cpe:2.3:a:pyyaml:pyyaml:5.3`, generated | CVE-2020-14343 and CVE-2020-1747, CRITICAL 9.8, CVSS 3.1 |
| jinja2 2.10 | Product `jinja2` | CVE-2019-8341, CRITICAL 9.8, CVSS 3.1 |
| flask 0.12.2 | Product `flask` | CVE-2018-1000656 and CVE-2019-1010083, HIGH 7.5, CVSS 3.0; CVE-2023-30861, HIGH 7.5, CVSS 3.1 |
| requests 2.25.1 | — | Clean |

### E5: security

| Check | Result |
|---|---|
| An upload named `../../evil.txt` | Staged as `evil.txt` |
| Cross-site `POST /scans` and `POST /system/stop` (`Origin`, `Sec-Fetch-Site`) | 403 `#cross-site-request` |
| An unknown `Host` | 400 |
| `/etc`, `/uploads/../etc` | 400 "Path is outside the allowed scan roots" |
| A missing folder, a relative path | 400 "does not exist", "must be absolute" |
| A JSON body | 415 |
| An unknown or malformed scan id | 404 |
| Error bodies | Problems, with no stack trace |
| The page's headers | `Content-Security-Policy`, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff` |
| The key in the logs | The smoke test's stand-in key 0 times at the default level and at Trace, `apiKey` 0 times |

### E7: the end-to-end pass

Against the container with the key: Execute, SBOM generation, "Correlating n of 4", and the report opened by itself;
cards 4 / 3 / 3 / 1 with Critical pulsing; the grid's risk, method and counts; the filter by name, by CVE id, and with
no match; the detail pane with each CVE's NVD link, "CVSS V3.1 SCORE · NVD 9.8 CRITICAL" and the description; COPY;
Export JSON (the scan resource) and Export PDF (its times labelled UTC, its CVE ids whole) checked without downloading;
a reloaded report; Rescan; Emergency Stop, which cancelled a scan 293 ms after it started; and times shown in the
browser's zone while the container runs in UTC. During the Rescan, NVD itself answered 503 to every request for a
minute: each package ended Unverified ("lookup failed: NVD returned 503") and the scan Incomplete, with no package
counted as clean.

### E8: accessibility

axe-core 4.14.0 (its npm tarball checked against the registry's SHA-512), WCAG 2.0 to 2.2 A and AA and best practices,
on Control Center idle and with a file staged, Active Logs with lines arriving, Reports with and without the detail
pane, and Reports with no scan: 0 violations. Left to review by axe, and measured by hand: the path input's text,
14.37:1; the snackbars' text on their fills, 10.37 (warning), 7.84 (success), 7.83 (info) and 5.29 (error);
`th-has-data-cells`, from Virtualize's empty spacer rows. A check of each of Airlock's overrides of MudBlazor against
the computed styles found none defeated. Keyboard: Blazor focuses each page's heading on navigation, every stop shows
a 2-pixel outline, Enter and Space open the file dialog from the upload panel, Enter selects a row with the focus kept
on it, Escape clears it, Space selects without scrolling. Reduced motion: the page's fade and the detail's slide become
`none`.

## Measurements

| Requirement | Target | Measured |
|---|---|---|
| U1-NFR-05 | 50 ms | 10,000 packages summarized in 4.2 ms, finished in 5.2 ms |
| U2-NFR-10 | 10 s; 1 s | 10,000 packages correlated in 796 ms; 281 in 17.4 ms |
| U3-NFR-10 | 100 ms; 2 s; 1 s; 5 s | Syft's 281 packages read in 3.7 ms, 10,000 in 56.4 ms; 2,000 NVD records in 164 ms; a 500-finding PDF in 426 ms |
| U3-NFR-05 | Under 16 MB allocated | A 256 MiB upload staged with 0.0 MiB allocated; in the container, a 1 GiB upload left the Airlock process at 273 to 278 MiB resident |
| U4-NFR-08 | 100 ms; 50 ms; 250 ms; 100 ms | A refusal in 6.4 ms; health in 1.2 ms; a 500-finding scan read in 8.1 ms; an event in 0.4 ms |
| U5-NFR-04 | 1 s; 300 ms; 100 ms | 2,001 rows in the browser: drawn in 69 ms, filtered in 234 ms after the last keystroke (the 200 ms debounce included), selected in 31 ms (medians; bUnit: 5.4, 8.5 and 2.8 ms) |
| U5-NFR-05 | Progress 1 to 4 times a second; the upload survives changing pages | A 1 GiB upload in about 7 s while three pages were opened in turn; distinct progress every 274 to 283 ms; the scan started once it ended |
| U5-NFR-06 | OFFLINE within 5 s; Reload after a restart | OFFLINE 354 ms after `docker compose stop`; Reload offered 1.96 s after the container was healthy again, and it reloaded |
| U5-NFR-11 | One host, under 2 MB, interactive within 2 s | 13 requests to one host, 1,414,580 bytes, nothing from cache; the layout drawn by the circuit at 141 ms, the first paint at 416 ms |
| U3-NFR-03 | No 30-second span over 50 requests; 600 ms apart | 619 requests, every answer 200, no 429 and no retry; at most 50 in any 30 s; starts 599 to 605 ms apart (5th to 95th percentile) |
| U5-NFR-02 | Ten tabs lengthen a scan by at most 5 % | All ten tabs followed the 281-package scan to its end; its length was 9.5 % over the pure pace, and 8.9 % with no tab open, both from this machine's slow clock (observation 1): about 0.5 % from the tabs |

U5-NFR-04, -05 and -11 were timed in a page that draws: the browser pane's page was hidden while its window was, which
throttles frames and observers, so headless Microsoft Edge (`--headless=new`, a throwaway profile, the DevTools
protocol) measured them, and the pane confirmed U5-NFR-04 once visible (68, 231 and 33 ms).

## Defects found and fixed

| # | Found in | Defect | Fix | Test |
|---|---|---|---|---|
| 1 | F6 (and C2) | `blazor.web.js` answered 404 in the image, so the page never became interactive: the build stage restores before it copies `src/`, and the SDK adds the framework's script package only once it sees a `.razor` file | `RequiresAspNetWebAssets` set in `Airlock.Web.csproj` | `DockerfileTests`; the smoke test checks every script on the page |
| 2 | F3 | The inventory grid had no scroll area of its own: MudBlazor's sticky-header rule (specificity 0,3,0) set `max-height: 100%` over Airlock's 60vh (0,2,0), so past about 120 rows the table was blank | `max-height: 60vh !important` | `StylesheetTests` |
| 3 | E8 | The active nav item kept MudBlazor's #3B82F6 on its hover tint, 4.25:1, for the same reason | Its colour and background `!important` | `StylesheetTests` |
| 4 | E8 | The hint "Staged artifacts are scanned instead of this path" was dimmed with the whole card, below 4.5:1 | Only the field is dimmed | `StylesheetTests` |
| 5 | E8 | The finished scan's snackbar link read 1.66:1 on the warning fill | Snackbar links take the fill's text colour, bold and underlined | `StylesheetTests` |
| 6 | E8 (moderate) | No main landmark, so each page's header counted as a second banner | `<main>` around the page body | `UiAccessibilityTests` |
| 7 | E7 | The PDF's CVE column broke ids longer than 13 characters: CVE-2020-14343 read "CVE-2020-1434" over "3" | The column widened from 2.2 to 2.8 units, the description narrowed from 5 to 4.4 | `ScanReportRendererTests`, which failed before |
| 8 | E7 | A scan ending while Emergency Stop's question was open took the tab to its report; returning the focus to the button, no longer on the page, threw and ended the circuit | The focus returns only while the button is on the page | `TerminalTests`, which failed before |
| 9 | CI's first run, Ubuntu | Three architecture rules failed: on Linux the coverage tool instruments statically by default, rewriting `Airlock.Domain.dll` and `Airlock.Application.dll` on disk, with references to `netstandard` and to its tracker, before the rules read them. Windows instruments in memory, so D1 never saw it | `testconfig.json` turns static instrumentation off in `Airlock.Architecture.Tests` | The three rules: with static instrumentation on, they failed on Windows and in a Linux container as on Ubuntu; with it off, all 68 pass |
| 10 | CI's first run, Windows | The problems page's test found no heading: the runner checks files out with CRLF, and the heading pattern's `$` matches only before `\n`. The working copy D1 used is LF | The pattern allows a `\r` before the line's end | `ProblemDetailsTests`, which failed on a CRLF copy of the page before the fix |
| 11 | CI's second run, Windows | A tab could miss a scan that ended at once, and with it a failed scan's reason (`ScanSessionTests` waited 10 s for it). The active slot's feed kept one change per watcher and dropped the older ones, against AR-26, so a tab's loop that read late saw only the slot freed. Under CPU load the reason was lost in 271 of 300 runs | Each watcher keeps up to 64 changes, a scan making four or more | `A_watcher_that_reads_late_still_hears_every_change_in_order`, which failed before; under the same load the reason came in 1,300 of 1,300 runs |

Fixes 1 to 8 were checked again in the rebuilt image. Fixes 9 and 10 change only the tests. With them, CI's test step
and coverage check passed on Windows (1,253 tests, 9 skipped, Syft's among them as that shell had no Syft). In a Linux
container with no network (SDK 10.0.400, the packages from this machine's NuGet cache, Syft 1.55.0), the architecture
rules passed and coverage met every target: Domain 100.0 / 99.3 %, Application 97.1 / 94.7 %, Infrastructure
93.3 / 84.7 %, Web 96.3 / 86.5 % (lines / branches). Three other tests failed there only from the container: the SDK
pin and the notices, as its SDK is 10.0.400 with ASP.NET Core 10.0.11, and Syft's cancellation, as the folder it scans,
NuGet's cache, was empty there and Syft finished first. GitHub's Ubuntu run had passed all three. Fix 11 changes
`ScanCoordinator`; with it the whole suite passed on Windows (1,254 tests) and coverage met every target.

## Observations, not changed

1. **Gaps in NVD's pace, from this machine's clock.** In the logs of every scan of some length, no NVD request seemed to
   start for about 3 s every 33 s, and scans took about 9 % longer than 600 ms a request. Investigated on 2026-11-02
   with a local stand-in for NVD, so no request reached NVD: the WSL2 virtual machine that WSL and Docker Desktop share
   keeps time about 9 % slow on its `tsc` clock source (a 20-second `sleep` lasts about 21.7 s by the host's clock, in
   WSL and in a container alike), and the host's time synchronisation steps the machine's wall clock forward about
   3.2 s every 33 s, while the monotonic clock, which every timer uses, stays slow. Airlock's requests are evenly
   spaced, 600 ms by that clock and about 660 ms in real time; each "gap" is the wall clock jumping in the log's
   timestamps, matched to the millisecond by a second process in another container. Docker's 30-second health check
   lasts 33 s for the same reason. Airlock is correct and stays within NVD's limit, so nothing in the code changes.
   Switching the virtual machine's clock source to `hyperv_clocksource_tsc_page` may cure it; the owner's choice,
   measured again with the same `sleep`.
2. MudBlazor's own `MudBlazor.min.css` (624,712 bytes) and `MudBlazor.min.js` (70,128 bytes) are served without a
   compressed variant, unlike Airlock's files; the cold load stays within its 2 MB.
3. After Emergency Stop's Terminate, the focus falls to the page: the button it returns to is then disabled.
4. A package with no candidate CPE and a name too generic to search counts as Clean and is listed in the scan's
   `NOT_PRODUCT_MATCHED` warning (AR-15), as designed; Syft's own Go modules gave one such warning in F2.
5. Docker's default builder keeps the build cache of C2's and E3's builds; `docker builder prune` would clear it, and
   any other cache it holds.

## Deviations from the plan

- Headless Microsoft Edge over the DevTools protocol stood in for the pane where the pane could not measure: F3 and F6
  (its page was hidden), E8 (axe-core is a script the page's CSP refuses, and nothing was put in `wwwroot`), and F2's
  ten tabs.
- E3 ran as the README says, with `SCAN_UPLOAD_PATH` and `AIRLOCK_PORT` given in the shell: the `.env` named a folder
  holding a 2,000-package test file, and ports 8080 and 8081 were in use. E2 ran on port 8082 with a staging root of
  its own, using the fresh clone's Syft.
- E9's port was published on the network address for about seven minutes, not the planned minute.
- F2 added a 150-second run with no tab (223 requests), since the pace alone did not explain the scan's length. About
  930 NVD requests were made with the key in all, against the plan's 1,200.
- Downloads beyond the plan's: Syft's archives were fetched twice by C3 (about 117 MiB instead of 58), and NuGet's
  packages again for the image rebuilds (about 67 MiB). axe-core's tarball was 706,680 bytes.
- An Airlock run from source that this stage had not started (Syft and key missing) held the build's files and port
  8080 on 2026-11-02, and was stopped through the preview tool.

## What is left

- **After the push** (`integration-test-instructions.md`, "After the push"): CI's first run, on 2026-11-03, failed only
  on defects 9 and 10, with Lint, Image and CodeQL passing, and its second, on their fix, only on defect 11; then CI on
  its fix, the merge, Dependabot, and the first release with its attestations.
- **This machine's clock** (observation 1): the owner may switch the WSL2 virtual machine's clock source; Airlock needs
  no change.
- **The image scan's acceptance** of the findings in Syft's binary ends on 2026-11-30 (`.grype.yaml`).

## Changes made in this stage

- `src/Airlock.Web/Airlock.Web.csproj`, `wwwroot/css/airlock.css`, `Components/Layout/MainLayout.razor`,
  `Components/Parts/Terminal.razor`; `src/Airlock.Infrastructure/Reports/ScanReportDocument.cs`;
  `scripts/ci/smoke-test.cs`.
- Tests: `DockerfileTests`, `StylesheetTests` (new), `UiAccessibilityTests`, `TerminalTests`, `ScanReportRendererTests`,
  and the performance tests' measurements with the new `Airlock.Domain.Tests/Quality/PerformanceSmokeTests.cs`.
- `aidlc-docs/construction/build-and-test/` (these five documents), the plan, `aidlc-state.md` and `audit.md`.
- After the push, defects 9 to 11: `tests/Airlock.Architecture.Tests/testconfig.json` and its project file,
  `ProblemDetailsTests`, and `src/Airlock.Application/Scanning/ScanCoordinator.cs` with `StartScanTests`.
