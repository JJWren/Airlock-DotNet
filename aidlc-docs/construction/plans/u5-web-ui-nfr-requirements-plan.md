# U5 Web UI — NFR Requirements Plan

**Stage**: CONSTRUCTION — U5 Web UI — NFR Requirements (Standard depth)
**Started**: 2026-10-26T01:37:23Z

## Inputs

- U5 functional design (UR-01 to UR-41, the copy catalogue, the theme tokens, the `airlock.js` contract, the
  rule-to-test map and what it leaves to this stage)
- NFR-01, NFR-02, NFR-03, NFR-05, NFR-07, NFR-09, NFR-10, NFR-12; stories E07, E08, E10 and E12
- U4's NFRs that the UI shares or extends: U4-NFR-06 (headers), U4-NFR-07 (nothing from another host), U4-NFR-09
  (no leaks), U4-NFR-13 (testability), U4-NFR-14 (coverage)
- Facts checked on 2026-10-26:
  - **MudBlazor 9.11.0** (nuget.org, about a month old): MIT; its .NET 10 group depends on
    `Microsoft.AspNetCore.Components`, `Microsoft.AspNetCore.Components.Web` and `Microsoft.Extensions.Localization`
    10.0.1 or later. It does not support static rendering, which suits Airlock's global Interactive Server rendering.
  - **bUnit**: 2.13.3 is the latest stable, published the day before the check and not yet downloaded; 2.12.5 is a
    week old. 2.13.3's .NET 10 group depends on `Microsoft.AspNetCore.Components` 10.0.13, Airlock's ASP.NET Core
    version, and on AngleSharp 1.8.6, AngleSharp.Css 1.2.0 and AngleSharp.Diffing 1.1.1. MIT.
  - **Inter 4.1** (2024-11-16) and **JetBrains Mono 2.304** (2023-01-14), the latest releases, both under the SIL
    Open Font License 1.1. Their release archives are 32.1 MB and 5.36 MB; Airlock needs only a few WOFF2 files and
    the licence texts from each.
  - **.NET 10's `CircuitOptions`** (dotnet/aspnetcore, release/10.0): a disconnected circuit is kept 3 minutes, at
    most 100 at once; a call from the server into the browser times out after 1 minute
    (`JSInteropDefaultCallTimeout`).
  - **Reconnection** (Microsoft Learn, ASP.NET Core 10): the browser raises `components-reconnect-state-changed` with
    the states `show`, `paused`, `hide`, `retrying`, `failed` and `rejected`. Its default server timeout is 30
    seconds.
  - **Contrast of the theme's colours** (WCAG relative luminance):
    - #71717A, the original's grey for small labels, reaches 3.67:1 on the surfaces (#18181B) and 4.12:1 on the page
      (#09090B);
    - #A1A1AA reaches 6.91:1 and 7.76:1;
    - #3B82F6, #EF4444, #FB923C, #EAB308, #10B981, #94A3B8 and #CBD5E1 all pass 4.5:1 on the page and the surfaces,
      but #3B82F6 and #EF4444 fall to 3.89:1 and 3.81:1 on the highlighted drawer item (#2A2A2C);
    - dark text on the primary Execute button passes (5.41:1), white text on it does not (3.68:1).

## Steps

- [x] Resolve the open question below (answered: A)
- [x] `construction/u5-web-ui/nfr-requirements/nfr-requirements.md` — U5-NFR-01 onwards, each with its target, how it
      is checked and its trace
- [x] `construction/u5-web-ui/nfr-requirements/tech-stack-decisions.md` — MudBlazor, bUnit, the fonts, the icons, no
      JavaScript toolchain; the versions as checked, to be checked again at code generation
- [x] Present for approval (approved 2026-10-27T02:15:53Z)

## Question

### Q-U5-N1 — Contrast against the original's grey
The original wrote its small labels — "Elapsed Time", the card labels, the column heads, the filter — in #71717A, 9 to
11 pixels high. On Airlock's dark backgrounds that grey reaches 3.6 to 4.4:1, below the 4.5:1 that WCAG 2.2 AA asks
of small text (1.4.3); NFR-12 promises keyboard access and text badges, but says nothing on contrast.

A) Meet WCAG 2.2 AA. Small grey text uses #A1A1AA (6.9:1 on the surfaces), a step lighter than the original. #71717A
   stays for borders, icons, dividers and disabled controls, where 3:1 suffices (1.4.11). The few highlighted
   backgrounds where blue or red text falls short get a lighter shade of the same colour (Recommended)
B) Keep the original colours exactly, and accept small labels below AA, as the original had them
C) Other

[Answer]: A

## Targets taken by default (object at the gate if any is wrong)

1. **Architecture.** The UI lives in `Airlock.Web.Components` (layout, pages, shared components), `Airlock.Web.State`
   (`ScanSession`), `Airlock.Web.Interop` (`AirlockJs`) and `Airlock.Web.Theme`. Rule 5 already keeps Infrastructure
   out of all of them. The server never calls its own API over HTTP: components call use cases in-process, and only
   the browser posts to the API.
2. **Responsiveness during a scan.** A tab redraws for progress and log lines at most 4 times a second, coalescing
   bursts, and once a second for the timer. Typing, clicks and keys are answered within 100 ms on the developer
   machine. Ten tabs following a scan of 1,000 packages lengthen it by no more than 5 % against the same scan with no
   tab open, measured in Build and Test.
3. **Bounds per tab.** The terminal keeps the last 1,000 lines, the size of a Live Log subscriber's buffer (U3), and
   Export Logs saves those. A tab holds one subscription each to the active slot, its scan and the Live Log, and at
   most one upload; all end with its session (U4-NFR-09). A disconnected session is kept for Blazor's default of 3
   minutes, at most 100 at once.
4. **Large results.** Reports stays usable with 2,000 inventory rows: its first drawing within 1 second, a filter's
   result within 300 ms of the last keystroke, a selection within 100 ms. A smoke test checks ten times these bounds,
   as U4 did.
5. **Long uploads.** A 2 GB upload over a slow link completes, with its progress, however long it takes: no call from
   the server into the browser waits for the whole upload, which Blazor would end after 1 minute. Progress arrives at
   least once a second and at most four times a second. The upload continues while the analyst changes pages, and
   closing the tab ends it, after which the server frees the slot (U4-NFR-04).
6. **Connection loss.** OFFLINE shows within 5 seconds when the server stops or closes the connection, and within 30
   seconds — SignalR's default server timeout — when the network goes silent. Reconnection follows Blazor's defaults.
   A session the server no longer holds is offered Reload (the `rejected` state).
7. **Accessibility: WCAG 2.2 AA** on the pages, for contrast (as Q-U5-N1 decides), keyboard use, visible focus,
   focus order, names for icon buttons, and notices and snackbars announced to screen readers. Colour is never the
   only signal, and reduced motion is honoured. bUnit checks names, roles and focusability. Build and Test adds a
   keyboard pass and an automated accessibility scan (axe-core) of the three pages in the browser pane, which must
   report no serious or critical issue.
8. **Security of the pages.**
   - Pages load nothing from another host (UR-02, UR-36).
   - The pages answer with headers that stop another site from framing them, so Emergency Stop and Execute cannot be
     clicked through a disguised page: `X-Frame-Options: DENY` and `frame-ancestors 'none'`. They also send
     `X-Content-Type-Options: nosniff`.
   - A Content-Security-Policy limits scripts, styles, fonts, images and connections to Airlock itself.
   - External text — CVE descriptions, log messages, file names — is shown as text, never as markup.
   - Blazor's antiforgery protects the UI's forms.
   - The NVD key never reaches a page.
9. **Time and culture.** Times are converted with the browser's IANA zone on Windows and Linux, daylight saving
   included; the container must carry time-zone data (U6). Numbers and dates are formatted the same whatever the
   server's culture — "92.42", "9.8", ISO dates — and the text is English only.
10. **Browsers**: the current and previous major versions of Chrome, Edge, Firefox and Safari, with WebSockets.
    Checked in Chromium, the browser pane, in Build and Test.
11. **Page weight and start.** A cold load of Control Center makes no request to another host, transfers less than
    2 MB, and is interactive within 2 seconds on localhost. Static files are served compressed, as .NET 10 does.
12. **Observability.** UI failures — an exception in a component, a failed call into the browser, a broken circuit —
    are logged as errors under `Airlock.Web`, without file contents or the key. Routine UI activity logs nothing at
    Information, so the Live Log shows scans, not clicks.
13. **Testability.** bUnit tests run with the real Application over fakes of its ports, as U4's did, on a fake
    `TimeProvider`, with bUnit's JavaScript stand-in in strict mode, so every call into `airlock.js` is planned; no
    test touches the network.
14. **Coverage.** `Airlock.Web` as a whole — API and UI, Razor components included, generated code excluded — keeps
    U4's targets: 90 % of lines and 85 % of branches.
15. **Maintainability.** MudBlazor's components come first (ADR-0005). One theme class. One stylesheet for what the
    theme cannot express (the terminal, the pulse, the fonts), plus scoped component styles. No CSS framework, no npm
    toolchain; `airlock.js` is a plain ES module with no bundler.
16. **Licensing.** MudBlazor (MIT); bUnit (MIT, tests only); Inter and JetBrains Mono (SIL OFL 1.1), their licence
    texts shipped beside the fonts and credited in the README (U6).
