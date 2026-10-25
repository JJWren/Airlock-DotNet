# Requirements — Airlock .NET Rebuild

**Depth**: Comprehensive (security tool, full rebuild, 26 decisions)
**Status**: Approved 2026-10-20T16:58:35Z (plan approval); amended 2026-10-21T22:35:25Z by the S0 review
(Q-S0-1…Q-S0-4)
**Sources**: requirement-verification-questions.md (rounds 1–3), reverse-engineering artifacts, GLOSSARY.md, ADR-0001…0005
**Decision IDs**: Q1–Q18 (rounds 1–2), N1–N8 (round 3), D1–D6 (derived). Defect numbers refer to
`../reverse-engineering/code-quality-assessment.md`.

## 1. Intent analysis

- **Request**: rebuild Airlock on the latest LTS .NET with a Blazor frontend, keeping its functionality.
- **Clarified intent**: Airlock is an **SCA** tool (SBOM via Syft → CVE correlation via NVD), not a SAST (Q1). The
  rebuild keeps every feature, fixes every verified defect (Q2, Q5–Q8), and adopts four deliberate changes chosen by
  the user: an asynchronous API redesign (Q10), binary artifact uploads (Q11), a component-library UI (Q12), and
  Clean Architecture (Q16).
- **Target platform**: .NET 10 LTS (supported to November 2028), ASP.NET Core 10, Blazor Web App.
- **Out of scope**: SAST; scan history or any database (D1); authentication (D2); pulling images from registries;
  vulnerability sources other than NVD; changing the severity threshold.

## 2. Functional requirements

### Scan intake
- **FR-01 Scan targets** — A scan inspects either a directory path on the Airlock host or a set of uploaded
  artifacts of any type (manifests, archives, package files, container image archives, binaries). *(Q11, D4; defect 13)*
- **FR-02 Start validation** — A scan is refused, with a reason, when: another scan is active (409); the NVD key is
  missing or Syft is unavailable (503); no target is given, the path does not exist, or it lies outside the optional
  `AllowedScanRoots` (400); upload limits are exceeded — more than 2 GB or 25 files (400/413). *(Q6, Q8, Q9, Q5-C, N3;
  defects 6, 7)*
- **FR-03 Upload safety** — Uploaded file names are sanitized so nothing is written outside the scan's staging
  folder; each scan gets its own staging folder, deleted when the scan ends however it ends. *(Q5-C, N3; defect 5)*
- **FR-04 Single active scan** — At most one scan runs at a time across the whole server. *(Q9; defect 15)*

### SBOM generation
- **FR-05 Syft runs** — The directory target, or all uploaded manifests together, are scanned in one Syft `dir:`
  run; every other artifact is scanned in its own Syft run with automatic type detection; output is Syft JSON; each
  run times out after 15 minutes. Python wheels (`.whl`) and NuGet packages (`.nupkg`) are staged with a `.zip`
  suffix so Syft unpacks them; for each `.nupkg`, Airlock also reads its `.nuspec` and adds the NuGet package
  (type `nuget`, its id and version). *(D4, N3, Q17, Q-S0-3)*
- **FR-06 Syft failures** — A non-zero Syft exit, unparseable output, or a package Airlock cannot represent (Q-U3-3) fails the scan and names the input; an artifact
  in which Syft finds no packages produces a warning, not a failure. A scan that includes a container image carries a
  warning that distribution backports are not considered. *(Q6, S0 N13; defect 6)*
- **FR-07 Package merge** — Packages from all runs are merged and de-duplicated by (type, name, version). *(D5, Q5-B;
  defect 13)*

### Correlation
- **FR-08 Candidate CPE order** — Candidate CPEs from the official CPE dictionary are tried before Syft-generated
  ones, and the generated ones are ranked so that a vendor and product equal to the package name come first; at most
  three per package. *(Q7, S0 S12)*
- **FR-09 CPE match** — The first candidate CPE for which NVD returns at least one CVE record establishes a CPE match;
  that record set's CVEs at or above the severity threshold become the package's findings (none ⇒ clean). A
  candidate NVD does not know, or rejects as malformed, counts as having no records. *(Q7, S0 N1–N2; defect 12)*
- **FR-10 Product match** — Only when no candidate CPE returns any record, NVD is asked for the CVEs whose
  configurations name the package's product, from any vendor, at the package's version (`virtualMatchString`); their
  CVEs at or above the threshold become findings with the Product match method. Versions are sent as Syft reports
  them, except that a leading `v` is removed from Go module versions — in product matches and CPE lookups alike
  (U3 decision 1). Names too generic to identify a product are not
  product-matched (domain rule BR-09, specified in U1's functional design). *(Q-S0-2, superseding the keyword fallback of Q7; ADR-0006;
  defects 1, 10, 12)*
- **FR-11 Unverified packages** — A package whose NVD lookups still fail after retries, or that Syft reports without
  a known version, is Unverified (with the reason) and never counted as clean. *(Q6, Q-S0-4; defects 7, 8)*
- **FR-12 Severity threshold** — Findings are CVEs of severity HIGH or CRITICAL. A matched CVE that NVD has not
  scored at all is not a finding; the scan lists it in a warning for its package. *(Q17, Q-U1-1)*
- **FR-13 CVSS selection** — Score and severity come from CVSS v3.1, else v3.0, else v4.0, else v2; within that
  version, NVD's own (Primary) score is used, and the reporting CNA's (Secondary) score only when NVD has not scored
  it — the highest one when there are several. The version used is recorded and shown. *(Q5-D, Q-S0-1, Q-U1-2;
  defect 11)*
- **FR-14 Summary** — Each scan reports totalPackages, vulnerablePackages, cleanPackages, unverifiedPackages,
  criticalFindings and highFindings (findings count CVEs; the other counts are packages). *(Q15)*
- **FR-15 Scan status** — Running → Completed, Incomplete (finished with ≥1 unverified package), Failed, or
  Cancelled; a scan is cut off after 60 minutes. *(Q6, N3; ADR-0002)*

### Control and monitoring
- **FR-16 Cancel and Emergency Stop** — A specific scan can be cancelled; Emergency Stop cancels the active scan
  whoever started it. Cancellation kills the Syft process tree and cancels pending NVD calls on Windows and Linux.
  *(Q5-B, Q9; defect 3)*
- **FR-17 Progress events** — Each scan publishes phase changes, packages correlated (n of total), scan-scoped log
  lines and its final status. *(N1; defect 14)*
- **FR-18 Live Log** — Airlock's own activity messages (Debug and above, framework noise excluded) are delivered in
  real time to every subscriber through bounded buffers, a new subscriber receiving the last 200 lines first; the
  tactical message wording is kept. *(Q5-B, Q14, Q-U3-1; defect 4)*
- **FR-19 Retention** — Finished scans remain retrievable for 24 hours, at most 20 of them, in memory only; they are
  lost on restart. *(N1, D1)*

### API (`/api/v2`)
- **FR-20 Resources** — `POST /scans` (multipart `targetPath` or `artifacts[]` → 202 + Location + `{id, status}`),
  `GET /scans/{id}`, `GET /scans/{id}/events` (SSE), `GET /scans/{id}/report.pdf` (409 while running),
  `POST /scans/{id}/cancel`, `POST /system/stop`, `GET /system/health`, `GET /logs/stream` (SSE). JSON is camelCase and
  named after the glossary (`vulnerablePackages[].findings[]`, …), with enumeration values in capitals with
  underscores (`INCOMPLETE`, `CRITICAL`; Q-U4-1); errors are RFC 9457 ProblemDetails whose `type` is a URL per kind of
  problem in the repository's `docs/api/problems.md` (Q-U4-2); the OpenAPI document is published and browsable with
  Scalar at `/docs`. *(Q10, N1, D6, Q17; ADR-0003)*

### Reports
- **FR-21 PDF Scan Report** — One server-side generator (QuestPDF) serves both the UI and the API: title with
  completion time (labelled UTC), summary table including Unverified, findings table (package, version, match method,
  CVE, severity, score with CVSS version, NVD link, description), an Unverified section and the scan's warnings.
  *(Q13, Q-U3-2; defect 18)*
- **FR-22 JSON export** — The JSON export is the scan resource as returned by `GET /scans/{id}`. *(parity, Q10)*

### Health
- **FR-23 Health checks** — Syft is resolvable (with its version) and an NVD key is configured; both are reported by
  `/api/v2/system/health`. *(N6)*

### User interface
- **FR-24 Shell and navigation** — Top bar with brand and inert notification/settings/avatar placeholders; navigation
  to Control Center (`/`), Active Logs (`/logs`) and Reports (`/reports/{id?}`); an Execute Audit shortcut to `/`.
  *(Q4, N1, Q5-A; defect 16)*
- **FR-25 Control Center** — Status pill (ONLINE / DEGRADED with reason / OFFLINE / LOADING); Local Directory Path
  (default `/uploads`); artifact picker (multiple files, staged list with remove); HTTP upload with a progress bar;
  Execute (disabled while a scan runs or when DEGRADED; tactical relabel while active); elapsed timer; phase and
  n-of-total progress; docked mini Terminal; inline error on failure. *(Q4, N2, N6)*
- **FR-26 Active Logs** — Header with the real machine name, elapsed timer, full-height Terminal. *(Q4)*
- **FR-27 Terminal** — Minimize toggle, auto-scroll, empty state, Export Logs (`.log` file), Emergency Stop behind a
  confirmation dialog; cleared when a scan starts. *(Q4)*
- **FR-28 Reports** — Spinner while running; empty state; header with completion time and duration; Export JSON,
  Export PDF, Rescan; summary cards Total, Critical findings (pulses when > 0), High findings, Clean; Unverified banner
  when > 0; inventory grid with Risk (highest severity, or UNVERIFIED), Package, Version, Method (CPE match or
  Product match) and Findings count, filterable by package name or CVE id; detail pane with CVE link to NVD, copy
  description, CVSS bar with version, description and package type; successful scans open `/reports/{id}`, which
  survives a page refresh while the scan is retained. *(Q4, Q5-A, Q6, N1; defects 1, 2)*
- **FR-29 Rescan** — Re-submits the same target; for upload scans it needs the files still staged in this browser
  session, otherwise it opens Control Center with a hint to re-select them. *(derived)*
- **FR-30 Visual identity** — MudBlazor components themed with the original palette (#09090B background, #18181B
  surfaces, #3B82F6 primary, #EF4444 error, #71717A secondary), Inter and JetBrains Mono, uppercase monospace labels,
  and the original layout; the tactical on-screen copy is kept. *(N4, N5, Q14; ADR-0005)*
- **FR-31 Time display** — Times are stored in UTC and shown in the browser's time zone; elapsed time is `mm:ss`
  (`h:mm:ss` past an hour). *(derived)*

## 3. Non-functional requirements

- **NFR-01 Platform** — .NET 10 LTS, C# 14, ASP.NET Core 10; Blazor Web App with global Interactive Server rendering;
  one host and one container image. *(Q3, D3; ADR-0001)*
- **NFR-02 Architecture** — Clean Architecture (Domain, Application, Infrastructure, Web) with inward-only
  dependencies enforced by ArchUnitNET tests; use cases are plain classes. *(Q16, N7; ADR-0004)*
- **NFR-03 Security** — Fail closed (ADR-0002); input hardening (FR-02, FR-03); no authentication, documented as an
  accepted risk for a local single-user tool; the NVD key is never logged and comes from user-secrets in development
  and environment variables in containers. API endpoints take no antiforgery token (no cookies or credentials);
  instead, their `POST` endpoints refuse requests a browser marks as coming from another site (403), and Airlock
  answers only the host names in `AllowedHosts`, by default `localhost`, `127.0.0.1` and `[::1]`, against DNS
  rebinding. *(Q-U4-3; ADR-0007)*
- **NFR-04 Reliability** — App-wide NVD sliding-window limit of 50 requests per 30 seconds; retries with exponential
  backoff and jitter on 429, 5xx and timeouts, honouring `Retry-After`; per-attempt and total timeouts; Syft and scan
  timeouts; staging cleanup on every exit path.
- **NFR-05 Performance** — Bounded correlation parallelism under the shared limiter; uploads streamed to disk rather
  than buffered in memory; the UI stays responsive during scans; uploads up to 2 GB.
- **NFR-06 Portability** — Identical behaviour on Windows (development) and Linux (container); Syft provided by
  install scripts locally and by a Docker build stage.
- **NFR-07 Observability** — Console logging honouring `LOG_LEVEL`; the Live Log; the health endpoint.
- **NFR-08 Maintainability** — Nullable reference types, warnings as errors, Central Package Management, `.slnx`;
  names follow GLOSSARY.md.
- **NFR-09 Testability** — xUnit v3, NSubstitute, bUnit, WebApplicationFactory, ArchUnitNET; no network access in unit
  tests; Syft-dependent tests opt-in by category; a regression test for every fixed defect.
- **NFR-10 Licensing** — Airlock is MIT-licensed; dependencies are MIT or Apache-2.0 except QuestPDF (Community
  licence, free below USD 1M annual revenue), which is documented in the README.
- **NFR-11 Delivery** — Git with `main` plus feature branches; GitHub Actions for build, test and Docker image build;
  CodeQL for C#; Dependabot; a multi-stage Dockerfile and docker-compose with the `/uploads` mount; nothing is pushed
  without the owner's explicit approval. *(Q18)*
- **NFR-12 Usability and accessibility** — Standard MudBlazor components; every action reachable by keyboard; severity
  is never conveyed by colour alone (badges carry text).

## 4. Constraints and assumptions

- The NVD CVE API 2.0 stays available and requires an API key for practical throughput.
- Syft v1.55.0 behaviour per artifact type and NVD's response to unknown `cpeName` values are verified by spikes at
  the start of unit U3 before the correlation rules are finalized.
- No data survives a restart (in-memory retention only).
- Implementation-level choices (container base image and user, staging volume, multipart streaming, CVSS v4 details,
  time-zone plumbing) are settled in the per-unit design stages, each with its own approval.

## 5. Traceability (decision → requirements)

| Decision | Requirements |
|---|---|
| Q1 SCA identity | Intent; GLOSSARY |
| Q2 Parity + fixes | FR-01…FR-31 (scope) |
| Q3 Single host | NFR-01 |
| Q4 App shell | FR-24…FR-29 |
| Q5 Fix bundle | FR-02, FR-03, FR-07, FR-13, FR-16, FR-18, FR-24, FR-28 |
| Q6 Unverified / Incomplete | FR-02, FR-06, FR-11, FR-15, FR-28 |
| Q7 Correlation order (fallback method superseded by Q-S0-2) | FR-08, FR-09, FR-10 |
| Q8 NVD key required | FR-02, FR-23 |
| Q9 One active scan | FR-02, FR-04, FR-16 |
| Q10 API redesign | FR-20, FR-22 |
| Q11 Binary artifacts | FR-01, FR-05 |
| Q12 / N4 / N5 Component library and identity | FR-30, NFR-12 |
| Q13 PDF | FR-21 |
| Q14 Scan term | FR-18, FR-30; GLOSSARY |
| Q15 Vocabulary | FR-14, FR-20; GLOSSARY |
| Q16 / N7 Clean Architecture | NFR-02, NFR-09 |
| Q17 Engineering defaults | FR-05, FR-12, FR-20, NFR-04, NFR-08, NFR-09 |
| Q18 Delivery | NFR-11 |
| N1 Resource model | FR-17, FR-19, FR-20, FR-28 |
| N2 HTTP uploads | FR-25, NFR-05 |
| N3 Limits | FR-02, FR-03, FR-05, FR-15 |
| N6 Status pill | FR-23, FR-25 |
| N8 Full AIDLC path | Process (aidlc-state.md) |
| D1–D6 Derived | FR-01, FR-07, FR-19, FR-20; NFR-01, NFR-03 |
| Q-S0-1 CVSS source | FR-13 |
| Q-S0-2 Product match | FR-10, FR-28; ADR-0006 |
| Q-S0-3 Wheels and NuGet packages | FR-05 |
| Q-S0-4 Unknown versions | FR-11 |
| S0 findings (N1–N2, N13, S12) | FR-06, FR-08, FR-09 |
| Q-U1-1 Unscored CVEs | FR-12 |
| Q-U1-2 Several CNA scores | FR-13 |
| Q-U2-1 Lookup failure before a match | FR-11 (U2 AR-14) |
| Q-U2-2 NVD unreachable for a whole scan | FR-11 (U2 AR-18) |
| Q-U2-3 Generic names without CPE records | FR-12 warnings (U2 AR-15, AR-19) |
| Q-U2-4 A path and artifacts together | FR-02 (U2 AR-04) |
| Q-U3-1 Live Log replay | FR-18 |
| Q-U3-2 Warnings in the PDF | FR-21 |
| Q-U3-3 Packages Airlock cannot represent | FR-06 |
| Q-U4-1 Enumeration values in JSON | FR-20 |
| Q-U4-2 Problem types | FR-20 |
| Q-U4-3 Requests from other websites | NFR-03; ADR-0007 |
