# U5 Web UI — NFR Design Plan

**Stage**: CONSTRUCTION — U5 Web UI — NFR Design (Standard depth)
**Started**: 2026-10-27T02:18:35Z

## Inputs

- `construction/u5-web-ui/nfr-requirements/` (U5-NFR-01 to U5-NFR-16, the contrast table, the tech-stack decisions)
- U5's functional design (UR-01 to UR-41, the `airlock.js` contract, the theme tokens with Q-U5-N1's shades)
- U4's `nfr-design/logical-components.md` (the host's pipeline the UI joins) and code generation summary
- Facts checked on 2026-10-27:
  - **.NET 10's Blazor Web App template**, generated from the installed SDK into the scratchpad, nothing downloaded:
    - `UseExceptionHandler("/Error", createScopeForErrors: true)` outside Development;
    - `UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true)`, with the router's
      `NotFoundPage`;
    - `UseAntiforgery()`, `MapStaticAssets()` and `MapRazorComponents<App>().AddInteractiveServerRenderMode()`;
    - `<ImportMap />`, which writes an inline script;
    - a reconnect `<dialog id="components-reconnect-modal">`, driven by `components-reconnect-state-changed` with
      `Blazor.reconnect()` and `Blazor.resumeCircuit()`;
    - the `#blazor-error-ui` bar, whose links `blazor.web.js` handles itself;
    - `BlazorDisableThrowNavigationException`.
  - **MudBlazor v9.11.0's source**:
    - the palette's properties (`PrimaryContrastText`, `TextSecondary`, `TextDisabled`, `LinesInputs`,
      `DrawerBackground`, `AppbarBackground` and the rest);
    - `MudThemeProvider` and the dialog, popover and snackbar providers;
    - `MudSimpleTable`, which wraps a plain `<table>` around markup of Airlock's own.

## Steps

- [x] `construction/u5-web-ui/nfr-design/nfr-design-patterns.md` — a pattern for each of U5-NFR-01 to U5-NFR-16, and
      the values table
- [x] `construction/u5-web-ui/nfr-design/logical-components.md` — namespaces and files, registrations, the
      pipeline's additions, the component tree and the tab's session as diagrams (validated Mermaid, with text
      alternatives), the checks, and what Code Generation and U6 must provide
- [x] Bring `component-methods.md` and, where a choice needs it, the U5 functional design in line
- [x] Present for approval (approved 2026-10-27T13:18:17Z)

No question is open: each choice below follows from an approved requirement or a framework fact.

## Choices made in this design (object at the gate if any is wrong)

1. **No prerendering.** Pages render once, when the tab's circuit connects. A tab's session, its subscriptions and the
   browser's time zone all need the live circuit; a prerendered page would build a throwaway session and show "—" or
   UTC times for an instant. While the circuit connects — well under a second on localhost — the static shell shows
   the dark background and the brand, so the screen does not flash white.
2. **One snapshot per session.** The tab's session keeps its state as one immutable snapshot, swapped under a lock
   whenever a stream delivers. It then raises `Changed`, naming what changed: lines, scan, slot, health, notice,
   staging or upload. Each component redraws only for its kinds of change, at most once per 250 ms, on the
   Application's `TimeProvider`.
3. **Four loops per session**: the active slot, the followed scan, the Live Log, and health every 10 seconds. All of
   them stop when the session is disposed with its circuit.
4. **The end of a scan** reaches `MainLayout` as an event; the layout navigates or shows the snackbar on the circuit's
   own thread.
5. **The terminal** keeps a ring of 1,000 lines, each with a sequence number used as its key, so a redraw adds and
   drops lines without rebuilding the rest. Following the newest line is done in the browser (`airlock.js`), with no
   round trip per line.
6. **The grid** is `MudSimpleTable` with Blazor's `<Virtualize>` rows of a fixed height. Each row is focusable, takes
   Enter and Space, and carries `aria-selected`. The filter waits 200 ms after the last keystroke.
7. **Uploads report to the session.** `submitScan` starts the upload and returns at once. Progress — every 250 ms at
   most — and the answer arrive through a .NET object the session owns, so no call waits for the upload and a page
   change cannot interrupt it.
8. **The file dialog opens in the browser.** The upload panel opens the dialog locally, by click, Enter or Space,
   without a round trip; the script stages the files and tells the session their names and sizes.
9. **Connection loss is drawn by CSS.** The page carries Airlock's own element with Blazor's id
   `components-reconnect-modal`, so Blazor adds its state classes there instead of showing its dialog; the bar and the
   pill's OFFLINE state follow those classes. `airlock.js` handles Retry and Reload; there is no inline script.
10. **The Content-Security-Policy** on the UI's pages:

    ```text
    default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self';
    connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'
    ```

    - `'unsafe-inline'` for styles is the one allowance, for MudBlazor's style attributes and theme.
    - `<ImportMap />` is left out, since its inline script would need one too; `airlock.js` is imported by its
      fingerprinted address instead.
    - `/docs` keeps no CSP, because Scalar writes inline script, but it still gets `X-Frame-Options: DENY` and
      `nosniff`.
    - `/api` keeps U4's headers.
11. **Errors.**
    - An error boundary around each page shows "Something went wrong" with Reload, and logs the error.
    - The `#blazor-error-ui` bar, shown when a circuit fails, carries the same words.
    - `/not-found` and `/error` are re-executed for the UI only, never for `/api`, whose status codes stay problems.
12. **An announcer** in the layout — a visually hidden live region — repeats notices and snackbars for screen readers,
    whatever MudBlazor's own markup does.
13. **The theme**, in `AirlockTheme`, maps the tokens onto MudBlazor's dark palette:
    - Text: `TextSecondary` #A1A1AA, `TextDisabled` #71717A.
    - Dark text on every coloured fill (`…ContrastText` #09090B).
    - Input outlines in #71717A, which passes 3:1.
    - A 2-pixel #60A5FA focus ring.
14. **Tests**: bUnit in loose mode for MudBlazor's own browser calls and in strict mode for `airlock.js`, so every call
    Airlock makes is planned; a contrast test reads the theme's colours and checks every pair in use.
