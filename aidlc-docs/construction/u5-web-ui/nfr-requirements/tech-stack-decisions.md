# U5 Web UI — Tech Stack Decisions

**Stage**: CONSTRUCTION — U5 Web UI — NFR Requirements (Standard depth)

The host and the API are U4's; the UI joins them in `Airlock.Web`. Exact package versions are checked again at Code
Generation and recorded in `Directory.Packages.props`; the font files are fetched then, with your permission, as
U3's Syft binary was.

## Airlock.Web — the UI

| Item | Decision |
|---|---|
| Rendering | A Blazor Web App in the same project and host as the API (ADR-0001): Razor components with global Interactive Server rendering, set once at the root, so every page is interactive; `AddRazorComponents().AddInteractiveServerComponents()` and `MapRazorComponents<App>().AddInteractiveServerRenderMode()`, mapped after the API (U4 hand-off). Whether pages prerender, and how LOADING covers the start, is NFR Design's |
| Component library | MudBlazor 9 (ADR-0005; 9.11.0 checked on 2026-10-26, MIT): `AddMudServices()`, the theme, dialog, snackbar and popover providers in the layout. MudBlazor needs interactive rendering, which the global render mode gives it. Its .NET 10 dependencies are in the ASP.NET Core shared framework, which .NET 10's package pruning reconciles |
| Theme | One `AirlockTheme` building a dark `MudTheme` from the tokens of the functional design, with Q-U5-N1's text shades: #A1A1AA for small grey text; #60A5FA and #F87171 for blue and red text on tinted backgrounds; #09090B as the text on primary fills |
| Icons | MudBlazor's built-in Material icons, drawn as inline SVG; no icon font, replacing the original's Material Symbols font |
| Fonts | Inter 4.1 and JetBrains Mono 2.304 (SIL OFL 1.1), as WOFF2 files in `wwwroot/fonts` with their licence texts, declared with `@font-face` in Airlock's stylesheet; no font service. Inter's variable font covers every weight the theme uses, and its Black Italic draws the italic titles; JetBrains Mono 2.304 ships its variable font only as TTF, so Code Generation took its 400 and 700 WOFF2 weights (642 KB in all, each file checked against its Git blob hash) |
| Styles | MudBlazor's stylesheet, one Airlock stylesheet (`wwwroot/css/airlock.css`: fonts, terminal, pulse, motion), and scoped `.razor.css` files; no CSS framework, no preprocessor |
| Script | `wwwroot/js/airlock.js`, one plain ES module imported through JS interop, holding the staged files and the uploads for the page's lifetime; no npm, no bundler, no TypeScript |
| Static files | `MapStaticAssets()`, which serves the files compressed and fingerprinted, built at build time; the components reference them through `Assets` |
| Page headers | A small middleware for page answers outside `/api`: `X-Frame-Options`, the Content-Security-Policy, `X-Content-Type-Options` (U5-NFR-08). `/api` keeps U4's headers |
| Reconnection | Airlock's own handling of `components-reconnect-state-changed` in `airlock.js`, for the OFFLINE pill and the connection bar, in place of the template's reconnect dialog (UR-05) |
| Time zones | `TimeZoneInfo.FindSystemTimeZoneById` with the browser's IANA name, which .NET resolves on Windows (through ICU) and Linux (through tzdata); UTC when the name is unknown. Formatting uses the invariant culture (U5-NFR-09) |
| Clock | The Application's `TimeProvider`, for the elapsed timer, the 10-second health read and render coalescing, so tests run them on a fake clock |
| Logging | The host's logging; UI failures at Error in `Airlock.Web.*` categories (U5-NFR-12) |

## Test projects

| Project | Packages | Notes |
|---|---|---|
| `Airlock.Web.Tests` | adds `bunit` | Component tests in the same project as the API tests: a `BunitContext` with MudBlazor's services, the real Application over the existing fakes of its ports, a fake `TimeProvider`, and bUnit's JavaScript stand-in in strict mode. bUnit brings AngleSharp, AngleSharp.Css and AngleSharp.Diffing. The project references the `Microsoft.AspNetCore.App` shared framework, as the host does, so ASP.NET Core comes from the framework rather than from bUnit's package copies (U5 Code Generation, refinement 3). The pages' headers are checked through `WebApplicationFactory`, as in U4 |
| `Airlock.Architecture.Tests` (framework) | — | References the shared framework too, so MudBlazor's dependencies resolve there as in the host (U5 Code Generation) |
| `Airlock.Architecture.Tests` | — | Rule 5 already covers the UI. A new rule: no type in `Airlock.Web.Components`, `Airlock.Web.State` or `Airlock.Web.Interop` depends on `HttpClient`; and a positive control that a page reaches an Application use case |

No JavaScript test runner is added: `airlock.js` is small, and its behaviour is checked by hand in the browser pane
(U5-NFR-13). The accessibility scan of Build and Test injects axe-core into the browser pane only; nothing of it ships.
NSubstitute is still not needed.

## Versions checked (2026-10-26)

- **MudBlazor 9.11.0** on nuget.org, published about a month earlier.
- **bUnit**: 2.13.3 is the latest stable, published 2026-10-25; on the day of the check it had no downloads, and its
  notes fix a fault with AngleSharp 1.8.6. 2.12.5 had been out a week. Code Generation takes 2.13.3 if it has stood a
  few days without a fix following it, and 2.12.5 otherwise. **Taken: 2.12.5** (2026-10-27, when 2.13.3 was under two
  days old).
- **Inter 4.1** (2024-11-16) and **JetBrains Mono 2.304** (2023-01-14), from their official GitHub releases.
- The rest of the stack is unchanged from U4: .NET 10 SDK, ASP.NET Core 10.0, xunit.v3, the coverage extension.

## What Code Generation will download

- MudBlazor from nuget.org: 7.75 MB.
- bUnit and the packages it brings from nuget.org, its AngleSharp packages among them; their size is measured when the
  Code Generation plan is written.
- The WOFF2 files and licence texts of Inter and JetBrains Mono from their GitHub repositories: about 1 MB, taken file
  by file rather than from the 32.1 MB and 5.36 MB release archives.

The Code Generation plan lists them with their sizes and asks before restoring or fetching anything.

**Downloaded at Code Generation (2026-10-27)**: the 7 approved packages, 15.2 MB (MudBlazor 9.11.0; bunit 2.12.5,
AngleSharp 1.8.5, AngleSharp.Css 1.1.3 and three 10.0.13 WebAssembly packages), and 16 more, 3.26 MB — Microsoft's
10.0.1 ASP.NET Core and Microsoft.Extensions packages, which `Airlock.Architecture.Tests` restored for MudBlazor before
it referenced the shared framework (the U5 code generation summary, deviation 1). From GitHub, the 6 font files and
licence texts, 0.64 MB, each matching its Git blob hash at its release tag.
