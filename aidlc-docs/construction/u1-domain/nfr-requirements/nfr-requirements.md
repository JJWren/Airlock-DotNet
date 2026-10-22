# U1 Domain — NFR Requirements

**Stage**: CONSTRUCTION — U1 Domain — NFR Requirements (Minimal depth)

Only what is specific to `Airlock.Domain`; system-wide NFRs stay in `inception/requirements/requirements.md`.

| ID | Quality | Requirement | Check | Trace |
|---|---|---|---|---|
| U1-NFR-01 | Dependencies | `Airlock.Domain` references no NuGet packages and no other project. | Architecture rule 1; the project file has no references | NFR-02 |
| U1-NFR-02 | Immutability and thread safety | Every public type is immutable — no public setters at all, `init` included, so `with` cannot bypass validation; collections copied on construction — so instances can be shared across threads without locks. | A reflection test over the assembly: no public property has a public setter, `init` included (refined in NFR Design) | NFR-02; services.md (single writer, immutable readers) |
| U1-NFR-03 | Determinism and culture independence | The same inputs always give the same outputs: no clock (times are passed in), no I/O, no environment. String comparisons are ordinal or ordinal-ignore-case; lower-casing uses the invariant culture. | Name-normalization tests also run under the `tr-TR` culture (the dotted/dotless I) | NFR-08 |
| U1-NFR-04 | Untrusted text | Text that comes from uploaded artifacts (package type, name and version, candidate CPEs, file names) or from NVD (CVE id, description) is bounded and free of control characters, so it cannot forge Live Log lines or spoof the Terminal. Limits: type 64, name 512, version 256, CPE 1,024, file name 255, CVE description 16,384 characters. No C0 or C1 control characters (CR, LF, ESC included) and no bidirectional formatting characters (U+200E, U+200F, U+202A–U+202E, U+2066–U+2069; added in NFR Design), except that a CVE description may contain line feeds and tabs. Constructors throw on violations; U3 cleans Syft and NVD data before constructing, so the throw is only a backstop. | Tests: each limit and each control character class | NFR-03 |
| U1-NFR-05 | Performance | Rules are linear in their input. `ScanSummary.From` and `Finish` handle 10,000 packages in well under 50 ms; every other transition is constant-time apart from list copies. | Measured in Build and Test (performance instructions) | NFR-05 |
| U1-NFR-06 | Testability and coverage | Every rule BR-01 to BR-18 has table-driven tests (the rule-to-test map). Coverage of `Airlock.Domain` at least 95 % of lines and 90 % of branches. | Coverage report from `dotnet test`; enforced in CI from U6 | NFR-09 |
| U1-NFR-07 | Maintainability | Type and member names follow GLOSSARY.md; each rule's implementation carries a one-line comment naming its BR id; warnings are errors under the recommended analyzers. | Build | NFR-08 |
| U1-NFR-08 | Error semantics | Invalid data throws `ArgumentException` (or a subclass) naming the parameter; an illegal transition throws `InvalidOperationException` naming the current status and phase. Messages contain no stack data or secrets — only the offending values, already bounded by U1-NFR-04. | Tests assert exception types and messages | services.md (unexpected errors → 500 or Failed) |
