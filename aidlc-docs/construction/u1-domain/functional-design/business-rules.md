# U1 Domain — Business Rules

**Stage**: CONSTRUCTION — U1 Domain — Functional Design (Standard depth)

Each rule is a pure function of its inputs. "Trace" names the requirement, decision or S0 finding behind it; worked
examples and the test map are in `business-logic-model.md`.

## Scoring

**BR-01 — CVSS version precedence.** A CVE's score comes from CVSS v3.1 if NVD has one, else v3.0, else v4.0, else
v2.0. *Trace*: FR-13; S0 N6, N8.

**BR-02 — Source within the chosen version.** Within that version, NVD's own (`Primary`) score is used. Without one,
the highest `Secondary` score is used; equal scores resolve to the first listed. *Trace*: FR-13; Q-S0-1, Q-U1-2;
S0 N5.

**BR-03 — Severity.** The severity is NVD's label for the chosen score. When NVD gives none, it is derived from the
score: for v3.0, v3.1 and v4.0 — 0.0 None, 0.1–3.9 Low, 4.0–6.9 Medium, 7.0–8.9 High, 9.0–10.0 Critical; for v2.0 —
0.0–3.9 Low, 4.0–6.9 Medium, 7.0–10.0 High. *Trace*: FR-13; defect 11.

## Findings

**BR-04 — Severity threshold.** A CVE matched to a package is a finding only if its preferred score's severity is
High or Critical. *Trace*: FR-12.

**BR-05 — Unscored CVEs.** A matched CVE with no score at all is not a finding. It is reported in a
`CvesWithoutSeverity` warning that names the package and the CVE ids; the package's verdict is decided by its other
CVEs. *Trace*: FR-12; Q-U1-1; S0 N13 (CVE-2025-60876).

**BR-06 — Highest severity.** A vulnerable package's Risk is the highest severity among its findings, whatever order
NVD returned them in. *Trace*: FR-28; defect 2.

**BR-07 — Findings of one package.** Findings are distinct by CVE id and ordered by severity (highest first), then
score (highest first), then CVE id. *Trace*: FR-28.

## Matching inputs

**BR-08 — Candidate CPE ranking.** A package's candidate CPEs are tried in this order, keeping Syft's order within
each tier, dropping exact duplicates, and keeping at most three:
1. candidates from the official CPE dictionary;
2. generated candidates whose vendor and product both equal the package name;
3. generated candidates whose product equals the package name;
4. all other generated candidates.

Names are compared in lower case, with `-`, `_` and `.` treated alike. *Trace*: FR-08; S0 S12.

**BR-09 — Product name for a product match.** The product Airlock asks NVD about is derived from the package name:
- a Go module (`go-module`) uses the last segment of its path (`github.com/gin-gonic/gin` → `gin`);
- a scoped npm package uses the part after the scope (`@angular/core` → `core`);
- every other package uses its name as is;
- the result is lower-cased.

There is no product match — the package stays with its CPE result — when the result is shorter than three characters,
contains whitespace, or is one of these generic names: `api`, `app`, `auth`, `base`, `cli`, `client`, `cmd`,
`common`, `config`, `core`, `data`, `http`, `https`, `internal`, `io`, `json`, `lib`, `libs`, `log`, `logging`,
`main`, `model`, `models`, `net`, `os`, `pkg`, `plugin`, `plugins`, `sdk`, `server`, `stdlib`, `test`, `tests`,
`tool`, `tools`, `types`, `util`, `utils`, `web`, `xml`. *Trace*: FR-10; ADR-0006.

**BR-10 — Versions.** A version is unknown when it is empty, whitespace or `UNKNOWN` (any case); such a package is
not looked up and becomes Unverified with the reason `VersionUnknown`. The version sent to NVD is the reported one,
except that a Go module's leading `v` is removed when a digit follows it (`v0.23.3` → `0.23.3`). Distribution
revisions such as `1.36.1-r31` are kept. *Trace*: FR-10, FR-11; Q-S0-4; S0 N13, S10.

**BR-11 — Package identity and merging.** Packages are the same when their type matches exactly, their name matches
ignoring case and their version matches exactly. When the same package comes from several sources, it is kept once,
with the spelling first seen and the union of its candidate CPEs in first-seen order. *Trace*: FR-07; D5.

**BR-12 — Manifest recognition.** An artifact is a Manifest when its file name (ignoring case) is one of: 
`requirements*.txt`, `Pipfile.lock`, `poetry.lock`, `uv.lock`, `pdm.lock`, `package.json`, `package-lock.json`,
`npm-shrinkwrap.json`, `yarn.lock`, `pnpm-lock.yaml`, `bun.lock`, `go.mod`, `go.sum`, `Cargo.lock`, `pom.xml`,
`gradle.lockfile`, `Gemfile.lock`, `*.gemspec`, `composer.lock`, `packages.lock.json`, `*.deps.json`,
`packages.config`, `Package.resolved`, `Podfile.lock`, `pubspec.lock`, `mix.lock`, `conan.lock`, `conanfile.txt`,
`stack.yaml.lock`, `cabal.project.freeze`. Every other artifact is Other. The list is conservative on purpose: a
manifest treated as Other still scans correctly on its own (S0 S1, S2). U3's integration tests confirm Syft reads
each listed name. *Trace*: D4; FR-05.

## Summary and lifecycle

**BR-13 — Summary.** For a finished scan:
- `TotalPackages` is the number of packages after merging (BR-11);
- `VulnerablePackages` and `UnverifiedPackages` are the sizes of those lists;
- `CleanPackages` = `TotalPackages` − `VulnerablePackages` − `UnverifiedPackages`, never negative;
- `CriticalFindings` and `HighFindings` count findings (CVEs), not packages;
- every package is in exactly one of clean, vulnerable and unverified.

*Trace*: FR-14; E06 "Counts add up"; E10 "Cards count findings and packages".

**BR-14 — Scan state machine.**

| Transition | Allowed when | Effect |
|---|---|---|
| `Start(id, target, now)` | — | Running, phase SbomGeneration, `StartedAt = now`. |
| `EnterPhase(phase)` | Running, and `phase` comes after the current phase | Phase moves forward. |
| `RecordProgress(correlated, total)` | Running in Correlation | 0 ≤ correlated ≤ total; total never changes once set; correlated never decreases. |
| `AddWarning(warning)` | Running | Appended. |
| `Finish(total, vulnerable, unverified, now)` | Running, in any phase | Completed when `unverified` is empty, otherwise Incomplete; summary computed (BR-13); `FinishedAt = now`. |
| `Fail(failure, now)` | Running | Failed with the failure; `FinishedAt = now`; no results. |
| `Cancel(reason, now)` | Running | Cancelled with the reason; `FinishedAt = now`; no results. |

Anything else — including every transition out of a final status, or `now` earlier than `StartedAt` — throws.
*Trace*: FR-15; ADR-0002.

**BR-15 — Duration.** A finished scan's duration is `FinishedAt − StartedAt`. *Trace*: FR-28.

**BR-16 — Warnings while running.** Warnings can only be added while the scan runs; the pipeline adds
`DistributionBackportsNotConsidered` once per scan when any SBOM source is a container image (U2). *Trace*: FR-06;
S0 N13.

## Presentation data

**BR-17 — CVE link.** Every CVE links to `https://nvd.nist.gov/vuln/detail/{CVE id}`. *Trace*: FR-28.

**BR-18 — Unverified reasons.** `LookupFailed` (NVD still failing after retries; the detail is the last failure
reason) or `VersionUnknown` (BR-10). *Trace*: FR-11; ADR-0002.
