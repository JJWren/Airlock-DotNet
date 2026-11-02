# Performance Test Instructions

**Stage**: CONSTRUCTION — Build and Test (Standard depth)
**Plan**: `construction/plans/build-and-test-plan.md` (approved 2026-10-30T16:42:36Z)
**Covers**: every performance target of U1 to U5, how it is measured, and against what. The results are in
`build-and-test-summary.md`.

## Measured by the tests

Each test asserts a bound ten times wider than the target, so a slow CI runner does not fail it, and writes what it
measured, `measured <requirement>: …`, to the test output (`unit-test-instructions.md`, "The measurements").

| Requirement | Target | Test |
|---|---|---|
| U1-NFR-05 | A scan of 10,000 packages (3,000 vulnerable, 2,000 unverified) summarized and finished within 50 ms | `Airlock.Domain.Tests` `PerformanceSmokeTests` |
| U2-NFR-10 | 10,000 packages correlated within 10 s, and Syft's 281 within 1 s, against an instant catalog | `Airlock.Application.Tests` `PerformanceSmokeTests` (in `OptionsAndTextTests.cs`) |
| U3-NFR-10 | Syft's JSON read: 281 packages within 100 ms, 10,000 within 2 s; 2,000 NVD records within 1 s; a 500-finding PDF within 5 s | `Airlock.Infrastructure.Tests` `PerformanceSmokeTests` |
| U3-NFR-05 | A 256 MiB upload staged with under 16 MB allocated | `Airlock.Infrastructure.Tests` `PerformanceSmokeTests` |
| U4-NFR-08 | A refusal within 100 ms, health within 50 ms, a 500-finding scan read within 250 ms, an event within 100 ms | `Airlock.Web.Tests` `PerformanceSmokeTests` |
| U5-NFR-04 | 2,000 rows drawn within 1 s, filtered within 300 ms of the last keystroke, a row selected within 100 ms | `Airlock.Web.Tests` `LargeResultsSmokeTests` (bUnit, no browser); the browser below |

## Measured in a browser

These need a browser that draws, the image, or a real NVD. Build and Test ran them against the image with Compose. A
browser's page that is hidden, for example in a minimized window, has its frames and observers throttled, so time only
a visible page; a headless Chromium (Microsoft Edge with `--headless=new`) is one, and its DevTools protocol can run
scripts the page's CSP would refuse.

### U5-NFR-04 — 2,000 rows in the browser

1. Make a `package-lock.json` (npm's lockfile version 3) listing 2,000 packages, `node_modules/<name>` with a version and
   a `resolved` URL each, under a root package. Syft lists 2,001 packages.
2. Point NVD at a closed port, so every package ends Unverified and becomes a row: the smoke test's override does this
   (`tests/smoke/compose.smoke.yml`, `AIRLOCK_SMOKE_NVD_URL=https://127.0.0.1:9/rest/json/cves/2.0`). The outage breaker
   (`Airlock:Correlation:OutageThreshold`, 5) ends the scan Incomplete within about 80 s.
3. On Reports, time in the page with a `MutationObserver` and `performance.now()`:
   - drawn: from `Blazor.navigateTo('/reports/<id>')` to the first `tr.airlock-grid__row`, then one animation frame;
   - filtered: from setting `.airlock-grid__filter`'s value and dispatching `input` to the matching rows, then a frame
     (the 200 ms debounce is part of the target);
   - selected: from a row's `click()` to `.airlock-detail` naming it with the row marked `is-selected`, then a frame.
4. Also scroll the grid's own area (`.airlock-grid__table .mud-table-container`) to rows 250, 1,000 and 1,990 and check
   they are drawn: the area must be 60 % of the window's height, not the height of all the rows.

### U5-NFR-05 — a long upload while changing pages

1. In the page, make a 1 GiB `File` (for example 1,024 parts of a 1 MiB `Uint8Array`), put it in the upload panel's
   `input[type=file]` through a `DataTransfer`, and dispatch `change`; it is listed as staged.
2. Press Execute, then open Active Logs, Reports and Control Center while it uploads. Record each change of
   `.airlock-progress__line` with the time and the page.
3. Expect the progress on Control Center and Reports throughout, distinct values one to four times a second, the scan
   to start once the upload ends, and the staging root to hold only `.airlock-lock` afterwards (`docker cp` it out of
   the container). `docker top <container> -o pid,rss,comm` shows the Airlock process's resident memory, which should
   not grow with the upload; `docker stats` counts the page cache of the file being written as well.

### U5-NFR-06 — OFFLINE and Reload

Watch `document.documentElement`'s `data-connection` attribute and the classes of `#components-reconnect-modal`, with
the wall clock, then run `docker compose stop`. OFFLINE is due within 5 s. Start the container again: the bar reaches
`components-reconnect-rejected`, shows Reload and hides Retry; Reload reloads the page.

### U5-NFR-11 — a cold load

With an empty cache (a new browser profile, or an origin never loaded), load Control Center and read
`performance.getEntriesByType('navigation')` and `('resource')`: every request to the page's own host, the sum of
`transferSize` under 2 MB, no entry served from the cache (`transferSize` 0 with a body). Interactive means the circuit
has drawn the layout: in a page where a script runs before the page's own (the DevTools protocol's
`Page.addScriptToEvaluateOnNewDocument`), the time `.mud-layout` appears; otherwise the time `js/airlock.<hash>.js`
loads, which the circuit asks for once it has drawn. Target: within 2 s on localhost.

### U3-NFR-03 and U5-NFR-02 — NVD's pace, and ten tabs following a scan

1. Start the image with your key and `LOG_LEVEL=TRACE`, with `/uploads` holding Syft's own Windows binary (281
   packages). At Trace, Airlock logs each answered NVD request, event 2201: `NVD <parameter> request from index <n>:
   <status> in <ms> ms (attempt <a>)`, with no headers.
2. Open ten tabs on Airlock (Control Center, Active Logs and Reports), then start the scan from the API, so every tab
   only follows it.
3. From the log, a request starts at its line's time less its elapsed time. Check that no 30-second span holds more
   than 50 starts, that starts are at least 600 ms apart, and that no answer is 429.
4. A keyed scan is paced by NVD's limit, so its length without tabs is its request count times 600 ms. Ten tabs
   lengthen it by no more than 5 % when the gaps between starts stay at 600 ms and the first start to the last is
   within 5 % of (requests − 1) × 600 ms.

Before the key goes into a Trace-level run, run the same at Trace with the smoke test's stand-in key and NVD at a
closed port, and count the stand-in key and `apiKey` in the log: both 0.
