# U1 Domain — Functional Design Plan

**Stage**: CONSTRUCTION — U1 Domain — Functional Design (Standard depth)
**Started**: 2026-10-21T23:07:27Z

## Inputs

- Requirements FR-07 to FR-15, FR-28, FR-31; NFR-08
- Stories E05, E06, E10 (and the scenarios the story map assigns to U1)
- `inception/application-design/component-methods.md` (Domain section)
- `construction/spikes/spike-results.md` (N1–N13, S1–S13); ADR-0002, ADR-0006

## Steps

- [x] Resolve the open questions below (answered 2026-10-21; both A)
- [x] `construction/u1-domain/functional-design/domain-entities.md` — every Domain type: fields, invariants, equality,
      construction
- [x] `construction/u1-domain/functional-design/business-rules.md` — the rule catalogue (CVSS selection, severity,
      threshold, highest severity, candidate ranking, product name and generic-name guard, version handling,
      manifest recognition, package identity, summary, status machine)
- [x] `construction/u1-domain/functional-design/business-logic-model.md` — scan state machine (validated Mermaid),
      summary computation, worked examples from the S0 data, rule-to-test map
- [x] Bring `component-methods.md` and `components.md` in line with the refinements (also FR-10, FR-12, FR-13,
      ADR-0006, E05 and the story map)
- [x] Present for approval (approved 2026-10-22T00:19:46Z)

## Questions

### Q-U1-1 — CVEs that NVD has not scored yet
S0 found matched CVEs with no CVSS score at all (CVE-2025-60876 for busybox, awaiting NVD analysis). A Finding is a CVE
at or above HIGH, so an unscored CVE cannot be one as defined.

A) Not a finding; the scan carries a warning listing unscored CVEs per package
B) The package becomes Unverified ("a matched CVE has no severity yet")
C) A finding with a new UNSCORED severity, shown and counted separately
D) Other

[Answer]: A

### Q-U1-2 — Several CNA scores and no NVD score
A CVE can carry more than one Secondary score for the same CVSS version (the CNA's and, for example, an ADP's), with no
Primary score from NVD.

A) Use the highest Secondary score
B) Use the first Secondary score listed
C) Other

[Answer]: A
