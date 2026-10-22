# S0 Spike Results

**Stage**: CONSTRUCTION — S0 spikes (prelude to U1)
**Started**: 2026-10-21T22:02:29Z · **Findings recorded**: 2026-10-21T22:32:07Z

## Plan

- [x] NVD: unknown and malformed `cpeName`
- [x] NVD: version-range matching through `cpeName`
- [x] NVD: keyword search false positives and product verification
- [x] NVD: CVSS metric shapes (versions, Primary and Secondary sources, unfamiliar metric types)
- [x] NVD: throttling without an API key
- [x] Syft: download v1.55.0 (with permission) and verify its checksum
- [x] Syft: one sample per artifact class
- [x] Extra probes prompted by the results: candidate-CPE order, unpacked wheels, product matching with any vendor,
      distribution revisions
- [x] Review the findings against the requirements (questions Q-S0-1 to Q-S0-4 answered 2026-10-21T22:35:25Z)

## Environment

- Syft v1.55.0 for Windows (`syft_1.55.0_windows_amd64.zip`, 30,025,255 bytes), downloaded 2026-10-21 with
  permission from `github.com/anchore/syft/releases/download/v1.55.0/`. SHA-256
  `ada4c9b93af575f479ce80fce6629897272a2d41f300a14bc49bace020767ca2` matched the release's checksums file and GitHub's
  asset digest; the checksums file matched its own digest. Build: Go 1.26.8, JSON schema 16.1.11.
- Samples were built locally (manifests, lock file, minimal `.jar`, `.whl` and `.nupkg` archives, `.zip`, `.tar.gz`),
  plus `alpine:3.20` pulled with permission and saved as a tarball, Syft's own binary (Go), and
  `System.Text.Json.dll` copied from the installed .NET runtime.
- NVD: 24 read-only requests in total, all without an API key.
- Downloads, samples and raw outputs stay in the session scratchpad and are not committed.

## NVD findings

| # | Probe | Observed | Design consequence |
|---|---|---|---|
| N1 | `cpeName` well-formed but unknown (`wrong-vendor:flask:0.12.2`) | 200, `totalResults` 0 | Same answer as a real CPE without CVEs → the candidate has no records; try the next one. |
| N2 | `cpeName` malformed (`cpe:2.3:a:foo`) | 404, empty body, header `message: Invalid cpeName parameter, see documentation.` | Treat as a candidate with no records and log a warning — not a lookup failure, so a bad Syft-generated CPE never makes a package Unverified. |
| N3 | `cpeName` `pyyaml:pyyaml:5.3` | CVE-2020-1747 (< 5.3.1) and CVE-2020-14343 (< 5.4), both 9.8 CRITICAL | NVD applies version ranges itself. |
| N4 | `keywordSearch` "requests 2.25.1" | Exactly CVE-2023-29530 (laminas-diactoros, psr-7) and CVE-2024-36401 (geoserver, geotools) | Reproduces the original's false positives; neither names `requests`. |
| N5 | CVSS sources | Each CVSS version can carry a `Primary` entry (NVD's own score) and `Secondary` entries (the reporting CNA's), which can disagree: CVE-2023-29530 v3.1 is 6.5 MEDIUM (Primary) vs 7.5 HIGH (Secondary). The original took the first entry — here the Secondary one. | Rule needed → Q-S0-1. |
| N6 | CVSS v4.0 | Present for recent CVEs as CNA scores, e.g. CVE-2026-27205: v4.0 2.3 LOW next to v3.1 4.3 MEDIUM | The v3.1 → v3.0 → v4.0 → v2 precedence holds. |
| N7 | Other metric keys | `ssvcV203` appears | The mapper ignores unknown metric keys. |
| N8 | CVE scored only in v3.0 | CVE-2018-1000656: v3.0 7.5 HIGH, v2 5.0 MEDIUM | v3.0 keeps it HIGH; the original's keyword path dropped it (defect 11 confirmed). |
| N9 | Throttling without a key | 10 requests accepted within about 45 s, then Cloudflare `429` (`error code: 1015`, `Retry-After: 0`) | Throttling is 429, not 403; `Retry-After: 0` is no usable delay → retries use their own minimum backoff; the app-wide limiter keeps Airlock under the limit. The keyed limit (documented 50 per 30 s) is checked with your key in Build and Test. |
| N10 | Response envelope | `format NVD_CVE`, `version 2.0`; small result sets fit one page; CORS lists the `apiKey` header | Paginate with `startIndex` when `totalResults > resultsPerPage`; header name `apiKey` confirmed. |
| N11 | `keywordSearch` "jinja2 2.10" and "minimist 1.2.5" | jinja2: CVE-2019-8341 (product `pocoo:jinja2`, 9.8) — kept by the product check. minimist: **no results**, although CVE-2021-44906 (9.8 CRITICAL) affects 1.2.5 (its text reads "<=1.2.5"). | Keyword search misses real CVEs as well as producing false ones. |
| N12 | `virtualMatchString` with any vendor, `cpe:2.3:a:*:<name>:<version>:*:*:*:*:*:*:*` | minimist 1.2.5 → CVE-2021-44906; flask 0.12.2 → the same 4 CVEs as the correct `palletsprojects:flask` CPE; requests 2.25.1 → CVE-2023-32681 and CVE-2026-25645 (both MEDIUM), no Laminas or GeoServer | Matches the package name against the CVEs' own product lists, version-aware — fewer false positives *and* fewer misses than keyword search → Q-S0-2. |
| N13 | Distribution revision: `busybox` "1.36.1-r31" vs "1.36.1" | With the revision: 2 CVEs (open ranges ≤ 1.37.0). Without: 7, including CVE-2022-48174 9.8. | NVD treats the Alpine revision as a newer version. Stripping it would report CVEs Alpine may already have patched; keeping it can miss some. Query versions as Syft reports them, and warn on image scans that distribution backports are not considered. |

## Syft findings

| # | Sample | Run as | Result |
|---|---|---|---|
| S1 | `requirements.txt` | single file | 4 python packages (flask 0.12.2, jinja2 2.10, pyyaml 5.3, requests 2.25.1) |
| S2 | `package-lock.json` | single file | 3 npm packages, including the lock file's own project (`fixture-app` 1.0.0) |
| S3 | Folder with both manifests in subfolders | `dir:` | 7 packages — confirms "all manifests in one directory run" (D4) |
| S4 | `.jar` with Maven metadata | single file, and `dir:` | log4j-core 2.14.1 (`java-archive`, `pkg:maven/...`) either way |
| S5 | `.whl` | single file, and `dir:` | **0 packages**; 1 (pyyaml 5.3) once unpacked into a folder or given a `.zip` extension |
| S6 | `.nupkg` | single file, and `dir:` | **0 packages**; with a `.zip` extension Syft catalogs the DLLs inside (assembly name and version), not the NuGet identity from the `.nuspec` |
| S7 | `.zip` of both manifests | single file | 7 packages (unpacked automatically) |
| S8 | `.tar.gz` of both manifests | single file | 7 packages |
| S9 | `docker save alpine:3.20` | single file | Detected as an image, distro Alpine Linux v3.20, 14 `apk` packages |
| S10 | Go binary (Syft's own) | single file | 281 packages (280 `go-module`, 1 `binary` reported as `syft@UNKNOWN`); 10 without any candidate CPE |
| S11 | .NET assembly | single file | System.Text.Json 10.0.11 (`dotnet`), no candidate CPEs |
| S12 | Candidate CPEs | — | `cpes[]` entries carry `source`: `nvd-cpe-dictionary` or `syft-generated`. requests and lodash: one correct dictionary CPE. flask and jinja2: 12 generated guesses, NVD's real CPE absent. pyyaml: the correct `pyyaml:pyyaml` is guess 12 of 12. log4j-core: `apache:log4j` is guess 3 of 3. minimist: only `minimist:minimist` (NVD's real CPE is not among the guesses). |
| S13 | Runtime | — | Every run exited 0 in about a second; `SYFT_CHECK_FOR_APP_UPDATE=false` stops Syft's update check. |

## Review decisions (2026-10-21T22:35:25Z)

| Question | Decision | Changes |
|---|---|---|
| Q-S0-1 — CVSS source within a version (N5) | NVD's own (Primary) score; the CNA's (Secondary) only when NVD has not scored the CVE | FR-13 |
| Q-S0-2 — Fallback when no candidate CPE has records (N4, N11, N12, S12) | Product matching (any vendor, `virtualMatchString`) **replaces** keyword search; match methods are CPE match and Product match | FR-10, FR-28, glossary, E05, E10, ADR-0006 (supersedes the fallback part of Q7) |
| Q-S0-3 — `.whl` and `.nupkg` (S5, S6) | Stage them with a `.zip` suffix so Syft unpacks them, **and** read each `.nuspec` so the NuGet package itself is reported | FR-05, E02 |
| Q-S0-4 — Packages with an unknown version (S10) | Unverified, reason "version unknown" | FR-11, glossary, E06 |

## Design notes applied in the unit designs unless you object

- An unknown CPE (N1) and a malformed one (N2) both mean "no records for this candidate".
- Candidate order: dictionary CPEs first; then Syft's guesses ranked so vendor and product equal to the package name
  come first (pyyaml's correct CPE moves from 12th to 1st); at most three per package.
- Strip the leading `v` from Go module versions before querying NVD (Go always writes `v1.2.3`; NVD does not).
- The Syft runner sets `SYFT_CHECK_FOR_APP_UPDATE=false`.
- Retries on 429 use their own minimum backoff and ignore `Retry-After: 0`; results are paginated.
- Image scans carry a warning that distribution backports are not considered (N13); versions are queried as Syft
  reports them.
- A lock file's own project (S2) stays in the inventory, as Syft reports it — same as the original.
- Scale: a Go binary like Syft's own (281 packages) needs up to about 1,100 NVD requests in the worst case — roughly
  11 minutes at the keyed rate — inside the 60-minute scan limit.
