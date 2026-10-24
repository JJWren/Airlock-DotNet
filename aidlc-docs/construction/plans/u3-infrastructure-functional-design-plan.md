# U3 Infrastructure — Functional Design Plan

**Stage**: CONSTRUCTION — U3 Infrastructure — Functional Design (Comprehensive depth)
**Started**: 2026-10-22T20:08:43Z

## Inputs

- Requirements FR-05, FR-06, FR-09 to FR-13, FR-16, FR-18, FR-19, FR-21, FR-23; NFR-03, NFR-04, NFR-06, NFR-07
- Stories E01, E02, E05, E08, E09, E11, E13, and the scenarios of E03, E04, E06, E12 and E14 that the story map gives U3
- `inception/application-design/component-methods.md` (Infrastructure section) and `components.md`
- The U2 ports, their records and the constraints U2 hands to U3 (`u2-application/code/code-generation-summary.md`)
- `construction/spikes/spike-results.md` (Syft v1.55.0 and NVD behaviour) and its recorded raw outputs

## Steps

- [x] Resolve the open questions below (answered: B, A, A)
- [x] `construction/u3-infrastructure/functional-design/domain-entities.md` — the Infrastructure's own types: Syft and
      NVD wire formats as read, options, the staging layout, the Live Log subscriber
- [x] `construction/u3-infrastructure/functional-design/business-rules.md` — the IR rules: Syft invocation and output
      mapping, the `.nuspec` reader, NVD requests, answers and mapping, staging, the registry, the Live Log, the host
      file system, the PDF layout, options and the install scripts
- [x] `construction/u3-infrastructure/functional-design/business-logic-model.md` — a Syft run and an NVD lookup as
      sequences (validated Mermaid, with text alternatives), the mapping tables, worked examples from the recorded S0
      outputs, the PDF layout, and the rule-to-test map with the defect regressions U3 owns
- [x] Bring `component-methods.md`, `components.md` and, where a decision needs it, the U1 and U2 documents in line
- [x] Present for approval (approved 2026-10-23T13:56:41Z)

## Questions

### Q-U3-1 — What a new Live Log subscriber sees first
The original streamed only what happened after a tab connected. A tab opened in the middle of a scan therefore shows
an empty terminal until the next line.

A) Live lines only, as the original
B) The last 200 lines first, then live lines
C) Other

[Answer]: B

### Q-U3-2 — Warnings in the PDF Scan Report
FR-21 lists the title, summary, findings and Unverified section. A scan's warnings — no packages in a file,
distribution backports not considered, names not product-matched, CVEs not yet scored — appear in the API and the UI.

A) Add a Warnings section to the PDF
B) Keep the PDF to the FR-21 sections
C) Other

[Answer]: A

### Q-U3-3 — A package that Syft reports but Airlock cannot represent
A Syft package whose name, version or type contains control characters, or exceeds the Domain's limits (names
512, versions 256 characters), cannot become a `Package`. That should never happen with a genuine artifact.

A) Treat the run's output as unreadable: the scan fails, naming the input
B) Clean the text (remove the characters, cut to the limit) and keep the package
C) Leave the package out and add a warning
D) Other

[Answer]: A

## Decisions taken in this design (object at the gate if any is wrong)

1. Go module versions lose their leading `v` in CPE lookups too, not only in product matches: Syft writes `v0.23.3`
   inside the CPE and NVD stores `0.23.3`. A Domain helper, `CandidateCpe.ForNvd(Package)`, rewrites the version part,
   and U2's correlator uses it before each CPE lookup — a small change to U1 and U2, made in U3 Code Generation.
2. Rejected CVEs are excluded from every NVD answer (the API's `noRejected` parameter): a rejected record is not a
   vulnerability.
3. A CVE record that NVD returns but Airlock cannot read makes that lookup fail, so the package is Unverified rather
   than judged on part of its records.
4. The product match asks for `cpe:2.3:a:*:<product>:<version>:*:*:*:*:*:*:*`, escaping the characters CPE 2.3
   requires in the product and version.
5. A directory is scanned as `dir:<path>`; an artifact by its path alone, so Syft detects archives and image archives
   itself, as in S0.
6. Staging lives under `StagingOptions.Root`, by default a folder `airlock-staging` in the system's temporary
   directory; each scan gets `<root>/<scan id>/` with `manifests/<n>/<name>` and `artifacts/<n>/<name>`. On Linux the
   folders are readable by the Airlock user only.
7. Syft is found at `SyftOptions.BinaryPath` — the original's `SYFT_BINARY_PATH` — and otherwise as `syft` on the path,
   then in `./bin` next to the application, where the install scripts put it.
8. HTTP logging redacts every header, so the `apiKey` header never reaches a log.
