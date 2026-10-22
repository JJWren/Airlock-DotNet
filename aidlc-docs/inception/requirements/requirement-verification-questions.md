# Requirement Verification Questions

Asked as grilling rounds during the planning session (2026-10-20). Each question listed its options with the
recommendation first; answers were collected through the question dialog and are recorded verbatim in `audit.md`.

## Round 1

### Q1 — What Airlock is
A) Faithful SCA port; docs and glossary say SCA; SAST out of scope (recommended)
B) SCA port plus a new first-party SAST engine
C) Re-center Airlock on SAST
[Answer]: A

### Q2 — What "same functionalities" means
A) Feature parity: every UI feature and API capability kept, idiomatic .NET, verified defects fixed, no new features (recommended)
B) Bug-for-bug parity
C) Parity plus enhancements
[Answer]: A

### Q3 — Hosting topology
A) One ASP.NET Core host: Blazor Web App (Interactive Server) plus Minimal API in-process (recommended)
B) Separate Web API plus standalone Blazor WebAssembly
C) One host, Interactive WebAssembly
D) Interactive Auto
[Answer]: A

### Q4 — Reverse-engineering sign-off and app-shell reconstruction
A) Accurate; rebuild the shell as described (recommended)
B) I have the original shell files
C) Needs corrections
[Answer]: A

## Round 2

### Q5 — Fix bundle (multi-select)
A) UI correctness · B) Engine robustness · C) Input hardening · D) Data consistency (recommended: all)
[Answer]: A, B, C, D

### Q6 — Packages Airlock could not check
A) Mark Unverified and finish Incomplete; bad path or Syft failure fails the scan (recommended)
B) Abort the scan on any NVD failure that survives retries
C) Keep failing open
[Answer]: A

### Q7 — Keyword fallback accuracy
A) Trustworthy fallback: dictionary CPEs first, keyword only when no CPE returns records, product-verified, low-confidence label (recommended)
B) Minimal fix (trigger + label only)
C) Drop the keyword fallback
D) Keep as is
[Answer]: A

### Q8 — NVD API key
A) Required; scans refuse to start without it (recommended)
B) Optional; run throttled without it
[Answer]: A

### Q9 — Concurrent scans
A) One active scan server-wide; 409 for a second start; Emergency Stop from any client (recommended)
B) Concurrent scans, each with its own stop
C) FIFO queue
[Answer]: A

### Q10 — API contract
A) Compatible-plus: keep the v1 routes and snake_case JSON, additive fields, synchronous scan (recommended)
B) A plus asynchronous endpoints
C) Redesign: camelCase, resource-oriented, asynchronous only, breaking
[Answer]: C

### Q11 — Upload scope
A) Any text manifest Syft understands (recommended)
B) `.txt` / `.json` only
C) Also binary artifacts (archives, package files, container images, binaries)
[Answer]: C

### Q12 — Look and styling
A) Tailwind CSS v4 standalone, exact port (recommended)
B) Tailwind CSS v3 via npm
C) Component library (MudBlazor or Fluent UI Blazor), new look
D) Bootstrap, restyled
[Answer]: C

### Q13 — PDF export
A) One server-side generator, QuestPDF (recommended)
B) One server-side generator, PdfSharp/MigraDoc
C) Keep two generators
[Answer]: A

### Q14 — The word for one run
A) Scan canonical; tactical UI copy kept as UI-only synonyms (recommended)
B) Scan canonical; UI copy renamed
C) Audit canonical
D) Vetting canonical
[Answer]: A

### Q15 — Vocabulary (revised after Q10 and Q11)
A) Artifact ⊃ Manifest; Scan Target; Package; Finding; Vulnerable / Clean / Unverified Package; JSON names follow (recommended)
B) Terms only; keep the old JSON names
C) Leave current usage
[Answer]: A

### Q16 — Solution structure
A) Core + Web (recommended)
B) Clean Architecture: Domain / Application / Infrastructure / Web
C) Single project
[Answer]: B

### Q17 — Engineering defaults
A) Accept the bundle (recommended)
B) Accept with overrides
[Answer]: A

### Q18 — Repo and delivery
A) git init, GitHub repo `JJWren/Airlock-DotNet`, MIT, main + feature branches, Actions + CodeQL + Dependabot, multi-stage Dockerfile + compose (recommended)
B) Same, keeping master / staging / develop
C) Local only
[Answer]: A

## Round 3

### N1 — API resource model
A) `/api/v2` resources, in-memory retention 24 h / 20 scans, Reports at `/reports/{id}` (recommended)
B) Same under `/api/v1`
C) A plus a `GET /scans` listing
[Answer]: A

### N2 — Upload transport
A) Browser uploads over HTTP to `POST /api/v2/scans` with progress; page follows the scan in-process (recommended)
B) Blazor InputFile over the SignalR circuit, 256 MB cap
C) Revisit ADR-0001 (WebAssembly)
[Answer]: A

### N3 — Limits
A) ≤ 2 GB and ≤ 25 files per scan; Syft 15 min; scan 60 min; staging deleted at the end (recommended)
B) Tighter
C) No limits
[Answer]: A

### N4 — Component library
A) MudBlazor 9 (recommended)
B) Fluent UI Blazor 5
C) Radzen Blazor 12
[Answer]: A

### N5 — Visual identity
A) Original tactical identity on MudBlazor (recommended)
B) Stock dark theme
C) Fresh design
[Answer]: A

### N6 — Status pill and health
A) ONLINE / DEGRADED / OFFLINE / LOADING; health endpoint mirrors checks; no periodic NVD probe (recommended)
B) ONLINE / OFFLINE only
C) A plus a periodic NVD probe
[Answer]: A

### N7 — Clean Architecture conventions
A) Plain use-case classes; per-layer tests plus ArchUnitNET architecture tests (recommended)
B) Mediator (source-generated) with the same tests
C) Plain classes, no architecture tests
[Answer]: A

### N8 — AIDLC path after approval
A) Lean (recommended)
B) Full: User Stories plus per-unit Functional / NFR design stages with gates
C) Minimal
[Answer]: B

## S0 review (2026-10-21, after the spikes)

### Q-S0-1 — CVSS source when NVD and the CNA disagree
A) NVD's own (Primary) score; the CNA's (Secondary) only when NVD has not scored the CVE (recommended)
B) The higher of the two
C) The CNA's score first
[Answer]: A

### Q-S0-2 — Fallback when no candidate CPE has records
A) Replace keyword search with product matching (any vendor, `virtualMatchString`) (recommended)
B) Product matching first, keyword search as a third step
C) Keep keyword search with the product check (Q7 as approved)
[Answer]: A

### Q-S0-3 — `.whl` and `.nupkg` files
A) Stage them with a `.zip` suffix so Syft unpacks them (recommended)
B) A, plus read each `.nuspec` to report the NuGet package itself
C) Leave them unsupported
[Answer]: B

### Q-S0-4 — Packages Syft reports without a known version
A) Unverified, reason "version unknown" (recommended)
B) Left out of the inventory, with a scan warning
[Answer]: A
