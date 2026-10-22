# U2 Application — Functional Design Plan

**Stage**: CONSTRUCTION — U2 Application — Functional Design (Comprehensive depth)
**Started**: 2026-10-22T01:26:28Z

## Inputs

- Requirements FR-01 to FR-19 and FR-21 to FR-23 (their Application side); NFR-03, NFR-04
- Stories E01, E02, E04, E05, E06, E08, E09, and the scenarios of E03, E10, E12 and E13 that the story map gives U2
- `inception/application-design/component-methods.md` (Application section), `services.md`, `component-dependency.md`
  (architecture rules 2, 4 and 6)
- U1's functional design (BR-01 to BR-18) and the U1 code on `feature/u1-domain`
- `construction/spikes/spike-results.md` (N1–N13, S1–S13, review decisions); ADR-0002, ADR-0006
- The original's `correlation_service.py`, `syft_service.py`, `nvd_service.py` and `scan.py` (log wording), its pytest
  scenarios and `mockData.js`

## Steps

- [x] Resolve the open questions below (answered 2026-10-22T01:31:34Z: A, B, B, B)
- [x] `construction/u2-application/functional-design/domain-entities.md` — the Application types: outcomes and
      refusals, the reservation, events and log entries, port records, options
- [x] `construction/u2-application/functional-design/business-rules.md` — the AR rules: readiness and reservation,
      target validation, staging limits, Syft run grouping and failure mapping, package merge, correlation, Unverified
      marking, warnings, progress and event streams, cancellation and limits, retention, health, report refusals
- [x] `construction/u2-application/functional-design/business-logic-model.md` — pipeline flowchart, reservation-to-finish
      sequence, correlation decision table, log message catalogue in the original's wording, worked examples (the
      original's tests, the `mockData.js` false positives, S0 data), rule-to-test map with the defect regressions U2 owns
- [x] Bring `component-methods.md`, `services.md` and, where an answer needs it, the U1 Domain in line
- [x] Present for approval (approved 2026-10-22T02:19:36Z)

## Questions

### Q-U2-1 — A lookup fails before any candidate CPE has matched
Correlation tries up to three candidate CPEs in order, then a product match. Suppose the first candidate's lookup still
fails after its retries (NVD error or timeout). Carrying on can report a different match than an error-free run would —
a later, less likely candidate — or report clean where the failed candidate had findings.

A) Stop there: the package is Unverified with that failure; nothing after the failure decides it
B) Carry on with the next candidates and the product match; Unverified only if nothing after the failure returns records
C) Other

[Answer]: A

### Q-U2-2 — NVD is unreachable for the whole scan
Each package's lookups are retried with backoff before it becomes Unverified. With NVD down, a 281-package scan would
wait out every package's retries and probably hit the 60-minute limit.

A) Keep going: every package gets its own retries; the scan ends Incomplete or hits the limit and fails
B) After a run of consecutive failed lookups (the number is set in NFR Design), stop calling NVD: the remaining packages
   are Unverified with the reason "NVD unavailable" and the scan finishes Incomplete
C) The same trigger, but the whole scan fails with "NVD unavailable"
D) Other

[Answer]: B

### Q-U2-3 — No CPE records, and a name too generic to product-match
Example: cloud.google.com/go/auth v0.23.3. None of its candidate CPEs has NVD records, and BR-09 withholds the product
match because "auth" is generic, so correlation has nothing more to ask.

A) Clean, with no match method
B) Clean, and the scan carries one warning naming such packages ("not product-matched: name too generic"); this adds a
   fourth `ScanWarningCode` to the Domain
C) Unverified with the reason "no reliable identifier"; the scan becomes Incomplete
D) Other

[Answer]: B

### Q-U2-4 — A directory path and uploaded artifacts in one request
The original's form always sent its path (pre-filled with `/uploads`) and scanned the uploaded files whenever there were
any. The new API takes `targetPath` or `artifacts`.

A) Artifacts win: a path sent with them is ignored, as in the original
B) Refuse the request with "Choose a directory or upload artifacts, not both"; the UI sends the artifacts when any are
   staged, otherwise the path
C) Other

[Answer]: B

## Decisions taken in this design (object at the gate if any is wrong)

1. A failed Syft run (non-zero exit, unreadable output, the 15-minute limit) fails the scan at once and names its input;
   the remaining runs are not started (FR-06).
2. Cancel, Emergency Stop, the 60-minute limit and unexpected errors keep no partial results (U1 BR-14 to BR-16).
3. Emergency Stop also aborts an upload that holds the active slot; nothing is recorded for it.
4. A directory target must be an absolute path to an existing directory. When `AllowedScanRoots` is set, the path is
   checked after following symbolic links, so a link inside a root cannot point the scan outside it.
5. Two uploaded files with the same name are both scanned, each under its own name.
6. When the same package comes out of several Syft runs, its candidate CPE lists are combined before ranking.
7. A CPE or product match that returns only unscored CVEs leaves the package clean, with the unscored-CVE warning (FR-12).
8. Exporting the report of a Failed or Cancelled scan is refused (409, "The scan ended without results"), which adds
   the refusal code `ScanHasNoResults`.
9. Watching an unknown or pruned scan ends at once; the endpoints check `GetScan` first so they can answer 404.
10. Live Log messages keep the original's emoji-prefixed wording, extended to the new events.
