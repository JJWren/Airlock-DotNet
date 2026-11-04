# Integration Test Instructions

**Stage**: CONSTRUCTION — Build and Test (Standard depth)
**Plan**: `construction/plans/build-and-test-plan.md` (approved 2026-10-30T16:42:36Z)
**Covers**: Syft's integration tests, the API's, the live NVD test, the image's smoke test, E14's three scenarios, the
security checks, stopping during a scan, the end-to-end pass in a browser, accessibility, copying over plain HTTP, and
what to check at CI's first run and the first release

On Windows, put `MSYS_NO_PATHCONV=1` before a Git Bash `curl` that sends a POSIX path such as `targetPath=/uploads`.

## Syft's integration tests

They run the real Syft against the test project's fixtures. With Syft installed (`build-instructions.md`, section 2):

```bash
SYFT_BINARY_PATH="$PWD/bin/syft" dotnet test --project tests/Airlock.Infrastructure.Tests -- --filter-trait "Category=Integration"
```

## The API's integration tests

`Airlock.Web.Tests` runs the whole host through `WebApplicationFactory`, with Syft and NVD faked: the endpoints, the
event streams, uploads, ProblemDetails, the security headers and the cross-site refusals.

```bash
dotnet test --project tests/Airlock.Web.Tests -- --filter-namespace "Airlock.Web.Tests.Endpoints"
```

## The live NVD test

One request to the real NVD, with no key whatever user-secrets hold: pyyaml 5.3's CPE must return CVE-2020-1747 and
CVE-2020-14343.

```bash
AIRLOCK_LIVE_NVD=1 dotnet test --project tests/Airlock.Infrastructure.Tests -- --filter-class "Airlock.Infrastructure.Tests.Nvd.NvdLiveTests"
```

## The image's smoke test

```bash
dotnet run scripts/ci/smoke-test.cs -- --image airlock-dotnet:smoke
```

It runs the image with the repository's `docker-compose.yml` and `tests/smoke/compose.smoke.yml` as Compose project
`airlock-smoke` on port 18080, with a stand-in key and no `.env`. The first phase checks health, the page, every
stylesheet and script on it (`blazor.web.js` among them) and that the stand-in key is in no log line; the second points
NVD at a closed port, scans `/uploads`, and expects Incomplete with every package Unverified and a PDF.

## E14's three scenarios, with a real key

The worked example: a folder holding a `requirements.txt` with `flask==0.12.2`, `jinja2==2.10`, `pyyaml==5.3` and
`requests==2.25.1`.

1. **Run locally on Windows.** Install Syft, set `SYFT_BINARY_PATH`, store the key in user-secrets and run
   `dotnet run --project src/Airlock.Web` (`build-instructions.md`, section 3). Airlock shows ONLINE in the browser.
   Scan the worked example's folder by its path, and keep the scan's JSON (`GET /api/v2/scans/<id>`, or Export JSON).
2. **Run with Docker Compose.** In `.env`, `SCAN_UPLOAD_PATH` names the worked example's folder and `NVD_API_KEY` the
   key; `docker compose up -d --build`; `GET /api/v2/system/health` reads ONLINE with Syft 1.55.0 and the key
   configured; scanning `/uploads` scans that folder. Keep its JSON.
3. **Same results on both platforms.** The two scans list the same packages, the same vulnerable packages with the same
   match (CPE or product) and CVE ids, and the same clean and unverified counts. Run them minutes apart: NVD's answers
   change only when NVD changes its data.

A scan of `/uploads` covers everything below that folder, so keep large test folders out of it when a real key is set:
a 2,000-package lock file costs about two NVD requests a package.

## Security checks

Against the container on 127.0.0.1; each error body is a problem (RFC 9457) with no stack trace.

| Check | Request | Expected |
|---|---|---|
| A path in an upload's name | `curl -F "artifacts=@evil.txt;filename=../../evil.txt" …/api/v2/scans` | 202; staged as `evil.txt` |
| A cross-site request | `-H "Origin: http://evil.example"`, or `-H "Sec-Fetch-Site: cross-site"`, on `POST /scans` and `POST /system/stop` | 403, type `#cross-site-request` |
| An unknown host | `-H "Host: evil.example"` | 400 |
| A target outside the scan roots | `targetPath=/etc`, `targetPath=/uploads/../etc` | 400 "Path is outside the allowed scan roots" |
| A missing or relative target | `targetPath=/uploads/missing`, `targetPath=uploads` | 400 "does not exist", "must be absolute" |
| A JSON body | `-H "Content-Type: application/json" --data '{}'` | 415 |
| An unknown or malformed scan id | `GET /scans/00000000-0000-0000-0000-000000000000`, `GET /scans/not-an-id` | 404 |
| The page's headers | `curl -D - http://127.0.0.1:<port>/` | `Content-Security-Policy`, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff` |

The key in the logs: a key nobody may read cannot be searched for, so the smoke test's stand-in key is, at the default
level and at Trace (`performance-test-instructions.md`, the last paragraph).

## Stopping during a scan

Scan something Syft takes a while over (Syft's own binary), and once `GET /scans/<id>` reads `SBOM_GENERATION`, run
`docker compose stop`. Expected: the stop within Docker's 10 s with exit code 0; the log reads "Syft was stopped … the
scan was stopped" and "Sequence aborted"; the staging root, copied out with
`docker cp <container>:/var/lib/airlock/staging <folder>`, holds only `.airlock-lock`, after the stop and after the
next start.

## The end-to-end pass in a browser

With the key, against the container, scanning the worked example:

- [ ] Control Center: ONLINE; Execute shows SBOM Generation, then "Correlating n of 4"; the tab moves to the report.
- [ ] Reports: Total 4, Critical findings pulsing above 0, High findings, Clean; the grid's Risk (the highest
      severity), Package, Version, Method ("CPE match" or "Product match") and Findings.
- [ ] Filter by a package name and by a CVE id; a filter matching nothing says "No matching assets found in project
      inventory."
- [ ] A row selected: "Active Analysis: <package> <version> (<type>)", the match, each CVE linked to its NVD page in a
      new tab (`noopener noreferrer`), "CVSS V3.1 SCORE · NVD <score> <severity>", the description, and COPY, which
      says "Description copied".
- [ ] Export JSON and Export PDF: the scan resource, and a PDF whose times read in UTC, labelled, and whose CVE ids are
      each drawn whole.
- [ ] Rescan scans the same target again; reloading a report's address shows it again while it is kept (24 hours,
      the last 20).
- [ ] Emergency Stop asks "CRITICAL: Terminate the active scan?"; Terminate cancels the scan ("Scan cancelled:
      Emergency Stop"); and a scan that ends while the question is open takes the tab to its report and leaves the page
      working.
- [ ] Times on the page in the browser's zone (`UTC±hh:mm`), while the container runs in UTC.
- [ ] With NVD failing (a closed port, or NVD's own 503s), packages are Unverified with the reason and the scan
      Incomplete, never clean.

## Accessibility

Target (U5-NFR-07): WCAG 2.2 AA, no serious or critical axe-core finding, a keyboard pass, reduced motion honoured.

1. **axe-core.** Take `axe.min.js` from the npm tarball of the chosen version, checked against the registry's
   `dist.integrity` (SHA-512). The page's CSP allows only its own scripts, so run axe through the browser's DevTools
   protocol (`Runtime.evaluate` with the file's text), which the CSP does not bind, then
   `axe.run(document, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa', 'best-practice'] } })`.
   Check Control Center idle and with a file staged, Active Logs with lines arriving, Reports with and without the
   detail pane, and Reports with no scan. Items axe cannot decide ("incomplete") are measured by hand.
2. **The overrides.** Where `airlock.css` overrides MudBlazor, a MudBlazor selector of higher specificity can win in the
   browser while bUnit, which draws no styles, cannot tell. For each of Airlock's rules on a `.mud-` class, mark each
   declaration `!important` for a moment with transitions off: a computed value that changes means another rule was
   winning.
3. **Keyboard.** With real key presses: Tab order from the page's heading (Blazor focuses the `h1` on navigation),
   a visible focus on every stop, Enter and Space on the upload panel open the file dialog, Enter selects a row,
   Escape clears it, Space selects without scrolling.
4. **Reduced motion.** Emulate `prefers-reduced-motion: reduce` (DevTools protocol `Emulation.setEmulatedMedia`) and
   check the computed `animation-name` of `.airlock-fade` and `.airlock-slide` turns `none`; the stylesheet's rule also
   covers `.airlock-pulse` and `.airlock-pill__dot`.

## Copying over plain HTTP from another host name

Browsers allow the clipboard only on secure origins, and `localhost` is one. For this check only, publish the container
on this machine's network address with a Compose override, then put it back:

```yaml
services:
  airlock:
    ports: !override
      - "<address>:<port>:8080"
    environment:
      AllowedHosts: "localhost;127.0.0.1;[::1];<address>"
```

Load `http://<address>:<port>/` (`window.isSecureContext` is false, `navigator.clipboard` absent), scan, select a row,
press COPY: the page's fallback copies and says "Description copied", or says "Copy failed — select the text and copy
it". While the port is published there, an unauthenticated Airlock is reachable from the network; run
`docker compose up -d` without the override straight afterwards.

## After the push

Once `JJWren/Airlock-DotNet` exists and the branch is pushed:

- **CI**: Lint (actionlint, shellcheck, zizmor), Test on Ubuntu and on Windows (Syft installed by the scripts, coverage
  against the targets), and Image (build, smoke test, scan) all pass; the job summaries show the counts, the coverage
  and the scan.
- **CodeQL** analyses C# and JavaScript (`airlock.js`) with no open alert; **Dependabot** opens its first updates as
  configured.
- **The first release**: tag `vX.Y.Z` on `main`. `release.yml` checks the tag against `Version`, runs the checks, pushes
  the image untagged, attests and verifies its provenance and SBOM, and only then tags it; the README's
  `gh attestation verify` commands then pass from any machine.
