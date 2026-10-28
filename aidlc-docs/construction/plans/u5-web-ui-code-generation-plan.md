# U5 Web UI — Code Generation Plan

**Stage**: CONSTRUCTION — U5 Web UI — Code Generation, Part 1 (Planning) · **Depth**: Standard
**Started**: 2026-10-27T13:34:34Z

## Inputs

- `construction/u5-web-ui/functional-design/` (UR-01 to UR-41, the copy catalogue, the `airlock.js` contract, the worked
  example on screen, the rule-to-test map)
- `construction/u5-web-ui/nfr-requirements/` and `nfr-design/` (the patterns, the values, the theme mapping, the CSP, the
  logical components and their checks)
- `inception/application-design/component-methods.md` (UI section)
- The U4 code on `feature/u4-web-api`: `AirlockHost`, the `/api` branch, `AirlockWebFactory` with its fakes for Syft and
  NVD, the worked example's golden file
- The U2 use cases the UI calls (`GetScan`, `WatchScan`, `WatchActiveScan`, `WatchLiveLog`, `EmergencyStop`,
  `GetSystemHealth`) and `ScanReservation`'s limit refusals
- The scratchpad's Syft v1.55.0 and its copy of .NET 10's Blazor Web App template

## Ground rules

- Code lives at the repository root (`src/`, `tests/`); `aidlc-docs/construction/u5-web-ui/code/` gets a summary only.
- Nothing is committed during generation. At the completion checkpoint I propose two commits — the U5 design documents
  with the earlier documents they amended, then the U5 code — on the branch chosen in Q-CG5-1, and make them only if you
  say so. Nothing is pushed.
- **Downloads**, made only once you approve this plan:
  - From nuget.org, 7 packages, 15.2 MB:
    - `MudBlazor` 9.11.0, 7.75 MB. Its three dependencies are part of ASP.NET Core's shared framework, which .NET 10's
      package pruning drops.
    - `bunit` 2.12.5, 3.06 MB, with `AngleSharp` 1.8.5 (2.58 MB) and `AngleSharp.Css` 1.1.3 (1.47 MB).
    - `Microsoft.AspNetCore.Components.WebAssembly`, `Microsoft.AspNetCore.Components.WebAssembly.Authentication` and
      `Microsoft.JSInterop.WebAssembly` 10.0.13, 0.34 MB together. bUnit references them, though no test runs
      WebAssembly.
    - `AngleSharp.Diffing` 1.1.1 is already in the local cache; bUnit's other dependencies come from the shared framework
      (refinement 3).
  - From GitHub, 6 files, 0.64 MB, out of each project's tree at its release tag (`rsms/inter` v4.1,
    `JetBrains/JetBrainsMono` v2.304): `InterVariable.woff2` (344 KB), `Inter-BlackItalic.woff2` (116 KB),
    `JetBrainsMono-Regular.woff2` (90 KB), `JetBrainsMono-Bold.woff2` (92 KB), and the two licence texts (4 KB each).
    Each file is checked against its Git blob hash at the tag before it is copied into `wwwroot`.
- **Versions**, checked on 2026-10-27:
  - MudBlazor 9.11.0 is still the latest.
  - bUnit 2.13.3 was published on 2026-10-25, less than two days ago, so the rule of the tech-stack decisions takes
    2.12.5, from 2026-10-19. 2.13.3's two fixes don't apply here. One is for AngleSharp 1.8.6, and 2.12.5 resolves 1.8.5.
    The other is for a `WaitFor` timer left running until its timeout, which matters only with a long timeout; the tests
    keep bUnit's default.
  - nuget.org lists no vulnerability and no deprecation for any of the seven packages, so NuGet's audit raises nothing in
    this warnings-as-errors build.
- Every file must build with warnings as errors. Analyzer findings are fixed, not suppressed; the only exceptions are
  the test-project ones U1 set (CA1707, CA1515). A name an analyzer forces to change is updated in the design documents
  in step I2.
- No test touches the network. The browser checks of H6 run against the local host only, and no request reaches NVD.
  Before the commit, the staged files are checked for keys, cookies and personal paths.

## Design refinements made while planning

Each keeps the approved behaviour and is recorded in the design documents in step I2.

1. **Razor's classes are public.** Razor generates every component as a public class. A build in the scratchpad
   confirmed it, and showed that a component can neither derive from an internal class nor take a parameter of an
   internal type. So:
   - the components, and `SessionComponent`, their base, are public;
   - what they share of the session is `private protected`;
   - their parameters are of public types: `InventoryGrid` selects by the Domain's `Package`, and `VulnerabilityDetail`
     takes the `Scan` and the `Package` instead of an `InventoryRow`.

   Every other UI type stays internal, as in U4.
2. **The error page is drawn by the server alone.** With prerendering off and every page interactive, a re-executed
   `/error` would start a circuit that draws the address in the browser's address bar, not the error.
   - `Error` is therefore excluded from interactive routing and drawn by the server, on a plain layout without MudBlazor
     or the session.
   - `App` picks each page's render mode with `HttpContext.AcceptsInteractiveRouting()`, as .NET 10 does for its static
     pages.
   - `NotFound` stays interactive: for an unknown address, the circuit's router draws it itself, so the re-executed 404
     shows it.
3. **The Web tests run on the shared framework.** `Airlock.Web.Tests` references `Microsoft.AspNetCore.App`, as the host
   does.
   - ASP.NET Core and `Microsoft.Extensions` then come from the framework, as in the running app. Without the reference,
     the tests would mix bUnit's 10.0.13 copies of them with the framework's 10.0.11 assemblies.
   - Restoring bUnit fetches 6 packages instead of 18 (8.80 MB).
   - U4's tests resolve the same way afterwards and must pass unchanged.
4. **Fonts.** 642 KB in all, every file served by Airlock itself.
   - JetBrains Mono 2.304 ships its variable font only as TTF (303 KB), so its static 400 and 700 weights are used,
     NFR Design's fallback.
   - Inter's variable font covers the upright weights.
   - Inter's Black Italic draws the italic page titles, which the browser would otherwise slant from the upright font.
5. **`airlock.js`'s address.** Only components can read `Assets`, the fingerprinted addresses, so `MainLayout` hands the
   module's address to `AirlockJs` before its first call.

## Steps

### A. Branch, packages and fonts
- [x] A1 Branch as chosen in Q-CG5-1; the uncommitted U5 design documents come along
- [x] A2 `Directory.Packages.props` — `MudBlazor` in the Web group, `bunit` under Tests
- [x] A3 The six font files: fetched into a new scratchpad folder, each checked against its Git blob hash at the tag,
      then copied to `wwwroot/fonts/inter/` and `wwwroot/fonts/jetbrains-mono/`, each licence as `OFL.txt`

### B. Amendment to U2
- [x] B1 `UploadLimits` in `Airlock.Application.UseCases` (`Check`, `Size`). `ScanReservation` takes its two limit
      refusals and their size wording from it, with unchanged behaviour. `UploadLimitsTests` in
      `Airlock.Application.Tests`

### C. `src/Airlock.Web` — the host and the static files
- [x] C1 `Airlock.Web.csproj` — `MudBlazor`, and `BlazorDisableThrowNavigationException` as .NET 10's template sets it;
      `launchSettings.json` opens `/` (U4 refinement 5)
- [x] C2 `AirlockHost` — the registrations:
      - Razor components, with `DetailedErrors` in Development only;
      - `AddMudServices` with the snackbar settings;
      - the scoped `ScanSession` and `AirlockJs`.

      Then the pipeline's additions in the order of `logical-components.md`, with `MapRazorComponents<App>()` last
- [x] C3 `Security/PageSecurityHeaders` — `X-Frame-Options`, `nosniff`, and the CSP everywhere but `/docs` and `/openapi`
      (U5-NFR-08)
- [x] C4 `wwwroot`:
      - `css/airlock.css`: `@font-face`, the terminal, the connection bar, the pulse, the focus ring, reduced motion;
      - `js/airlock.js`: the contract of the functional design, as NFR Design amended it;
      - `favicon.svg`

### D. State, interop and theme
- [x] D1 `State`:
      - `ScanSession`: the snapshot, `SessionChange`, `ScanEnded`, the four loops, the sink's reports;
      - the records: `SessionState`, `ScanEnding`, `StagedArtifact`, `UploadProgress`, `TerminalLine`, `InlineNotice` with
        `NoticeKind`, `InventoryRow` with `VulnerableRow` and `UnverifiedRow`;
      - `RenderCoalescer` and `UiFormat`
- [x] D2 `Interop` — `AirlockJs` (a failed call logged at Error), `UploadSink`, `ScanSubmissionTarget`, `SubmissionAnswer`
- [x] D3 `Theme/AirlockTheme` — the palette, typography and layout of the theme mapping

### E. Components
- [x] E1 `App` (the static shell, with the render mode per page of refinement 2), `Routes`, `_Imports`
- [x] E2 `Layout` — `MainLayout`, `NavMenu`, `ConnectionBar`, `Announcer`, `AirlockErrorBoundary`, and the error page's
      plain layout
- [x] E3 `Shared` — `SessionComponent`, `StatusPill`, `ArtifactPicker`, `ScanProgress`, `ElapsedTimer`, `ScanNotice`,
      `Terminal`, `StopScanDialog`, `SummaryCards`, `UnverifiedBanner`, `WarningsPanel`, `InventoryGrid`,
      `VulnerabilityDetail`
- [x] E4 `Pages` — `ControlCenter`, `ActiveLogs`, `Reports`, `NotFound`, `Error`

### F. `tests/Airlock.Web.Tests`
- [x] F1 Project file — `bunit`, and the `Microsoft.AspNetCore.App` framework reference (refinement 3)
- [x] F2 Helpers:
      - `UiTestContext`: MudBlazor's services; the Application and the Infrastructure with U4's fakes for Syft and NVD; a
        temporary staging root; a fake clock; bUnit's interop loose for MudBlazor and strict for `airlock.js`;
      - scan builders for the worked example and for 2,000 rows
- [x] F3 Rule tests (the rule-to-test map): `ShellTests`, `ScanSessionTests`, `StatusPillTests`, `ControlCenterTests`,
      `TerminalTests`, `ActiveLogsTests`, `ReportsTests`, `PresentationTests`, `ErrorPageTests`, with the regressions for
      defects 1, 2, 16 and 17
- [x] F4 NFR checks (`logical-components.md`):
      - coalesced redraws, bounds, uploads through the sink;
      - the 2,000-row smoke test, in the collection that runs alone;
      - `ThemeContrastTests`, the accessibility markup, external text shown as text;
      - the page headers, through `AirlockWebFactory`;
      - times across `Europe/Berlin`'s change and in `America/Phoenix`, under `de-DE`;
      - logging

### G. `tests/Airlock.Architecture.Tests`
- [x] G1 `WebRules` — no `HttpClient` in `Components`, `State` or `Interop`; positive control: `Reports` reaches
      `GetScan`

### H. Verify
- [x] H1 `dotnet build` — zero warnings
- [x] H2 `dotnet test` with `SYFT_BINARY_PATH` set to the scratchpad's Syft — every test passes; record the counts
- [x] H3 Coverage of `Airlock.Web` against 90 % of lines and 85 % of branches (U5-NFR-14); U1 to U4 still at theirs
- [x] H4 Confirm the worked example on screen is test rows
- [x] H5 A temporary probe shows the new architecture rule fails on a violation; then it is deleted
- [x] H6 Run the host with `dotnet run` (port 8080 checked free first) and the scratchpad's Syft, twice:
      - first with the NVD key emptied on the command line, so any key kept in user secrets goes unused;
      - then with a placeholder key and `Airlock:Nvd:BaseUrl` pointed at a closed port on this machine (checked closed
        first). Scans then start, no request reaches NVD, and every package ends unverified.

      In the browser pane:
      - DEGRADED with its reason; the shell, theme and fonts, with no request to another host;
      - staging files — picked through the page's script, since the pane cannot drive the system's file dialog — and an
        upload large enough to watch its progress while Active Logs is open; a second tab following the scan;
      - the terminal, Emergency Stop by keyboard, and the exports' links and names, read from the page without saving
        anything;
      - Reports for the Incomplete scan, a refresh of it, and Rescan;
      - OFFLINE and the bar when the host stops, and Reload once it is back;
      - a keyboard pass through Control Center; the page weight (under 2 MB) and start (under 2 seconds); no CSP
        violation in the console.

      Left for Build and Test: findings on screen and a full scan with a real NVD key, the axe-core scan, copying over
      plain HTTP from another host name, and reduced motion, which the pane cannot emulate

### I. Documentation
- [x] I1 `aidlc-docs/construction/u5-web-ui/code/code-generation-summary.md` — files, test counts, coverage, deviations
      and the constraints handed to U6
- [x] I2 Update the design documents for refinements 1 to 5 and any name changed during implementation
- [x] I3 Tick this plan; update `aidlc-state.md` and `audit.md`

## Questions

### Q-CG5-1 — Branch for U5
U4 is approved and committed on `feature/u4-web-api`; `main` holds U3. U5 builds on the U4 code and amends U2 slightly
(B1).

A) Fast-forward `main` to `feature/u4-web-api`, then branch `feature/u5-web-ui` from `main`, as for U2 to U4
   (Recommended)
B) Branch `feature/u5-web-ui` from `feature/u4-web-api`; `main` stays at U3
C) Keep working on `feature/u4-web-api`
D) Other

[Answer]: A (2026-10-27T21:56:57Z)

### Plan approval
A) Request Changes
B) Approve and generate (restores 7 packages, 15.2 MB, from nuget.org and fetches 6 font files, 0.64 MB, from GitHub)

[Answer]: B (approved 2026-10-27T21:56:57Z)
