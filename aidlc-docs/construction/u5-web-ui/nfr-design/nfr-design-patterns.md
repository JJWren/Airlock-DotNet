# U5 Web UI — NFR Design Patterns

**Stage**: CONSTRUCTION — U5 Web UI — NFR Design (Standard depth)

| NFR | Pattern | How it is applied |
|---|---|---|
| U5-NFR-01 Architecture | Namespaces, guarded by rule 5 and a rule against HTTP | The UI's code sits beside the API in `Airlock.Web` (`logical-components.md`): `Components` (layout, pages, shared components), `State`, `Interop`, `Theme` and `Security`. Rule 5 already keeps every Web type but `AirlockHost` away from Infrastructure. A new architecture rule forbids `HttpClient` in `Components`, `State` and `Interop`, so a component can never call the API from the server; its positive control is that `Reports` reaches `GetScan`. `AirlockHost` registers the UI, as it does the API. |
| U5-NFR-02 Responsiveness | One snapshot, typed changes, coalesced redraws | The session keeps its state as an immutable `SessionState`, replaced under a lock when a loop delivers. It then raises `Changed` with flags naming what changed — `Lines`, `Scan`, `Slot`, `Health`, `Notice`, `Staging`, `Upload`, `TimeZone`. Each component derives from a small `SessionComponent` that listens for its own flags and asks for a redraw through a coalescer: at most one `InvokeAsync(StateHasChanged)` per 250 ms, on the Application's `TimeProvider`. Redraws that follow the analyst's own input are Blazor's and are never delayed. The elapsed timer redraws itself once a second, only while its scan runs. The check counts a component's renders on a fake clock while 100 events arrive within 250 ms. |
| U5-NFR-03 Bounds per tab | A ring of lines and one cancellation for everything | The terminal's lines are a ring of 1,000 `TerminalLine` entries, each with a sequence number that is its `@key`, so a redraw adds and drops lines without rebuilding the others. The session's four loops — the active slot, the followed scan, the Live Log, and health — share one `CancellationTokenSource`. `DisposeAsync`, which Blazor calls when the circuit ends, cancels it and awaits the loops. A new followed scan cancels the previous scan loop first. Blazor's `CircuitOptions` keep their defaults: 3 minutes and 100 disconnected circuits. |
| U5-NFR-04 Large results | Virtualized rows in a table of Airlock's own markup | `InventoryGrid` renders `MudSimpleTable`, which wraps Airlock's own `<thead>` and `<tbody>`, with `<Virtualize>` rows of a fixed 48-pixel height (`SpacerElement="tr"`, 10 rows of overscan) in a scroll area of its own. Only the visible rows are drawn, however many there are. The rows are computed once per scan and filter: the filter's text field waits 200 ms after the last keystroke, matches names and CVE ids in memory, and hands the grid the result. Selection compares package identities, so it costs nothing. The smoke test generates a scan of 2,000 rows. |
| U5-NFR-05 Long uploads | Start, then report back | `airlock.js` `submitScan(target, sink)` builds the `FormData`, starts an `XMLHttpRequest`, and returns as soon as the request is sent. Progress goes to the sink — a `DotNetObjectReference<UploadSink>` owned by the session, not by a page — through `[JSInvokable]` methods: at most every 250 ms, at least once a second while bytes move, and once on completion with the status, the scan id, or the problem's `detail`. The session awaits a `TaskCompletionSource` that the sink completes, so no call from the server waits on the browser, and changing pages leaves the upload untouched. If the circuit is gone when the upload ends, the script catches the failed call and stops. |
| U5-NFR-06 Connection loss | Blazor's state classes on Airlock's own element | `ConnectionBar` renders `<div id="components-reconnect-modal">`; with that element present, Blazor adds its state classes to it — `components-reconnect-show`, `-hide`, `-retrying`, `-failed`, `-rejected`, `-paused` — instead of its own dialog. `airlock.js` listens for `components-reconnect-state-changed` and mirrors the state on `<html data-connection="…">`. The CSS then shows the bar ("Connection lost — reconnecting…", or "Connection lost." with Reload) and switches the pill's text to OFFLINE; the pill holds both labels and shows one. Retry calls `Blazor.reconnect()`, and Reload reloads the page; the buttons are wired in the script, with no inline handler. A server that stops closes the WebSocket at once, and a silent network ends after SignalR's 30-second default. |
| U5-NFR-07 Accessibility | Theme tokens with measured pairs; semantic markup; an announcer | `AirlockTheme` maps the tokens onto MudBlazor's `PaletteDark` (values below). Small labels use `TextSecondary` (#A1A1AA). Blue and red text on tints uses #60A5FA and #F87171 through two CSS classes. Every coloured fill carries dark text. Input outlines (`LinesInputs`) are #71717A, which passes 3:1. The focus ring is 2 pixels of #60A5FA, at least 5.42:1 on every background measured, where 3:1 is required. Grid rows are focusable `<tr>` elements with `aria-selected`; icon buttons carry `aria-label`; the terminal is `role="log"` with `aria-live="off"`, so it does not flood a screen reader. `MainLayout` holds a visually hidden `aria-live="polite"` announcer that repeats each notice and snackbar; errors use `role="alert"`. `FocusOnNavigate` moves the focus to each page's `h1`. After the stop dialog closes, the focus returns to Emergency Stop. A `prefers-reduced-motion` media query switches off the fade, the slide and the pulse. `ThemeContrastTests` reads the colours from `AirlockTheme` and checks every text and background pair the pages use against 4.5:1, and every control part against 3:1. |
| U5-NFR-08 Security | Response headers for pages; text only | `PageSecurityHeaders`, a middleware ahead of U4's `/api` branch that leaves `/api` to U4's headers, sets in `Response.OnStarting`: `X-Frame-Options: DENY` and `X-Content-Type-Options: nosniff` everywhere in that branch; the Content-Security-Policy (values below) everywhere but `/docs` and `/openapi`, since Scalar's page writes inline script. `App.razor` leaves out `<ImportMap />`, whose inline script the policy would block, and imports `airlock.js` by its fingerprinted address (`Assets["js/airlock.js"]`). The UI writes every external text through Razor's encoding; `MarkupString` is not used. `UseAntiforgery()` stays in the pipeline, as Razor components require. |
| U5-NFR-09 Time and culture | One formatter, the browser's zone, the invariant culture | `airlock.js` `timeZone()` reports the IANA name once per session. `UiFormat` converts with `TimeZoneInfo.TryFindSystemTimeZoneById`, falling back to UTC, and writes every time, offset, duration, size, score and count with `CultureInfo.InvariantCulture`: `yyyy-MM-dd HH:mm:ss`, `UTC+02:00`, `92.42`, `9.8`, `12.4 KB`. The process culture is never changed. Its tests set a `de-DE` current culture and cross 2026-10-25, the day `Europe/Berlin` leaves summer time. |
| U5-NFR-10 Browsers | Standard platform features only | WebSockets (Blazor), `XMLHttpRequest` upload events, `Intl.DateTimeFormat`, the Clipboard API with a fallback, ES modules and CSS custom properties: all available in the current and previous major versions of Chrome, Edge, Firefox and Safari. No polyfill. |
| U5-NFR-11 Page weight and start | Static assets compressed and fingerprinted; no prerendering | `MapStaticAssets()` serves MudBlazor's files, Airlock's stylesheet and script, and the fonts compressed (gzip, and Brotli when published), fingerprinted and cached. The fonts are WOFF2: Inter's variable font, with its Black Italic for the italic page titles, and JetBrains Mono's 400 and 700 weights, since JetBrains Mono 2.304 ships its variable font only as TTF (642 KB in all). Pages render without prerendering, `InteractiveServerRenderMode(prerender: false)` on `Routes` and `HeadOutlet`, so the server builds each page once, for the live circuit. Until the circuit connects, the static shell in `App.razor` shows the background and the brand. |
| U5-NFR-12 Observability | Airlock's own error boundary and interop guard | `AirlockErrorBoundary`, Blazor's `ErrorBoundary` extended, wraps each page. It logs the exception at Error in `Airlock.Web.Components` and shows "Something went wrong" with Reload; the Live Log carries the message, the console the details. `AirlockJs` catches a failed call into the browser, logs it at Error in `Airlock.Web.Interop`, and lets the page carry on. Failures of a whole circuit are logged by ASP.NET Core in its own category, on the console. No UI code logs at Information. |
| U5-NFR-13 Testability | A component-test context composed like the host | `UiTestContext`, a `BunitContext`, registers MudBlazor's services, the Application, and the Infrastructure with U4's fakes for Syft and NVD. Staging goes under a temporary root, and the `TimeProvider` is a fake one. bUnit's JavaScript stand-in runs in loose mode for MudBlazor's own calls and in strict mode for the `airlock.js` module, so every call Airlock makes must be planned. The page headers are checked through U4's `AirlockWebFactory`. |
| U5-NFR-14 Coverage | Components count as code | Razor components compile into `Airlock.Web`, and their lines map to the `.razor` files, so coverage counts them like any class. Generated code under `obj` stays excluded, as in U1 to U4. The measure is U4's. |
| U5-NFR-15 Maintainability | Folders by role; one comment per rule | `Components/Layout`, `Components/Pages` and `Components/Parts` (not `Shared`, a Visual Basic keyword CA1716 refuses in a public type's namespace), with a scoped `.razor.css` where a component needs its own styles. `wwwroot/css/airlock.css` holds the fonts' `@font-face`, the terminal, the connection bar, the pulse and the motion rules; `wwwroot/js/airlock.js` the contract of the functional design. Each component's first line names its UR rules. |
| U5-NFR-16 Licensing | Licence texts beside the files | `wwwroot/fonts/inter/` and `wwwroot/fonts/jetbrains-mono/` each hold their WOFF2 files and the font's `OFL.txt`; the README credits them (U6). |

## Values chosen

| Fixed value | Size | Reason |
|---|---|---|
| Redraws for progress and log lines | at most 1 per 250 ms per component | U5-NFR-02 |
| Elapsed timer | 1 redraw per second while its scan runs | UR-15 |
| Health read | every 10 seconds | UR-04, as the original polled |
| Terminal lines kept | 1,000 | U5-NFR-03, a Live Log subscriber's buffer (U3) |
| Grid row height and overscan | 48 pixels, 10 rows | U5-NFR-04 |
| Filter delay | 200 ms after the last keystroke | U5-NFR-04 |
| Upload progress | at most every 250 ms, at least every second, and once at the end | UR-11, U5-NFR-05 |
| Unverified banner | 5 packages before "Show all" | UR-25 |
| Snackbars | 6 seconds for success and information; errors stay until closed | UR-14 |
| Animations | page fade 200 ms; pane slide 250 ms; pulse 1.5 s; none under reduced motion | UR-38 |
| Focus ring | 2 pixels, #60A5FA, offset 2 pixels | U5-NFR-07 |
| Disconnected session kept | 3 minutes, at most 100 (Blazor's defaults) | U5-NFR-03 |

## Theme mapping (`AirlockTheme`)

| MudBlazor palette | Value | Token |
|---|---|---|
| `Primary` · `PrimaryContrastText` | #3B82F6 · #09090B | Primary · On primary |
| `Secondary` · `SecondaryContrastText` | #71717A · #FFFFFF | Secondary |
| `Info` · `InfoContrastText` | #60A5FA · #09090B | Primary text on tints |
| `Success` · `SuccessContrastText` | #10B981 · #09090B | Clean |
| `Warning` · `WarningContrastText` | #EAB308 · #09090B | Warning |
| `Error` · `ErrorContrastText` | #EF4444 · #09090B | Error |
| `TextPrimary` · `TextSecondary` · `TextDisabled` | #E2E8F0 · #A1A1AA · #71717A | Text · Secondary text · Secondary |
| `ActionDefault` · `ActionDisabled` | #A1A1AA · #71717A | Secondary text · Secondary |
| `Background` · `Surface` | #09090B · #18181B | Background · Surface |
| `AppbarBackground` · `AppbarText` | #131315 · #E2E8F0 | Top bar · Text |
| `DrawerBackground` · `DrawerText` · `DrawerIcon` | #1C1B1D · #A1A1AA · #A1A1AA | Drawer · Secondary text |
| `LinesInputs` | #71717A | Input outlines, at least 3:1 |
| `LinesDefault` · `Divider` · `TableLines` | white at 5 % | The original's `border-white/5`; decorative |
| `TableHover` | #3B82F6 at 5 % | The original's row hover |

Typography: Inter for the default family and the headings — page titles in weight 900, italic, in capitals, as the
original; JetBrains Mono for labels, values, versions and the terminal. Layout: a 56-pixel top bar, a 256-pixel drawer
clipped under it, corners of 8 pixels (cards 12).

## The pages' Content-Security-Policy

```text
default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self';
connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'
```

`connect-src 'self'` covers the circuit's WebSocket to the same host in every browser of U5-NFR-10. `'unsafe-inline'`
for styles is the one allowance, for MudBlazor's style attributes and theme variables. No script runs inline: Blazor,
MudBlazor and `airlock.js` load as files, and the reconnect and error bars are wired in scripts.
