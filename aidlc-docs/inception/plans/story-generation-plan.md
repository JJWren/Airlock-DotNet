# Story Generation Plan

**Stage**: INCEPTION — User Stories, Part 1 (Planning)
**Started**: 2026-10-20T17:07:00Z
**Answers recorded and plan approved**: 2026-10-21T00:39:19Z (Q8-A)

## Assessment

- **High-priority indicators present**: new and changed user-facing behaviour (binary artifact uploads, HTTP upload
  with progress, Unverified / Incomplete results, new status-pill states, a component-library UI), a redesigned
  client-facing API (`/api/v2`), and more than one kind of user (the analyst in the UI, the integrator calling the API
  from CI, whoever installs and configures Airlock).
- **Decision**: EXECUTE (Full AIDLC path chosen in decision N8). Depth: Standard.

## Inputs

- `../requirements/requirements.md` (FR-01…FR-31, NFR-01…NFR-12)
- `../reverse-engineering/` artifacts (UI inventory, business transactions BT-1…BT-7)
- `/GLOSSARY.md`, `/docs/adr/0001…0005`

## Plan

- [x] Answer the planning questions below
- [x] Analyze the answers for ambiguities (resolved below; no follow-up questions needed)
- [x] Get approval of this plan (Q8-A)
- [x] Generate `../user-stories/personas.md`
- [x] Generate `../user-stories/stories.md` with acceptance criteria and traceability per the answers
- [x] Verify every FR / NFR in scope is covered by at least one story (coverage table in `stories.md`)
- [x] Present the stories for approval (approved 2026-10-21T00:41:37Z)

## Ambiguity analysis

Coarse epics (Q7-B) combined with user-story sentences plus Gherkin (Q3-A) and MoSCoW (Q5-A) left the shape of each
entry open. Resolved as:

- Each epic has one user-story sentence, a bullet list of its capabilities, and three to seven Gherkin scenarios
  covering the key behaviours and the regressions it fixes.
- Priority is set per epic (the highest of its items); an item is tagged only when its priority is lower than its
  epic's. Security hardening counts as **Must**, alongside fail-closed behaviour.
- Feature areas follow Q2-A; the Pipeline Integrator's end-to-end API epic sits under Scan intake.

## Questions

### Q1 — Personas
A) Three: Vetting Analyst (runs scans and reviews results in the UI), Pipeline Integrator (calls `/api/v2` from CI),
   Platform Operator (installs and configures Airlock: NVD key, Syft, limits, Docker)
B) Two: Vetting Analyst and Pipeline Integrator, with operator concerns folded into the analyst
C) One: Vetting Analyst only
D) Other

[Answer]: A

### Q2 — Story organization
A) By feature area: Scan intake · Correlation results · Monitoring and control · Reports and exports · Health and
   configuration · Delivery
B) By user journey (submit → monitor → review → export), with API stories alongside
C) By persona
D) By unit of work (U1–U6)
E) Other

[Answer]: A

### Q3 — Story and acceptance-criteria format
A) "As a <persona>, I want <capability>, so that <benefit>" with Gherkin Given / When / Then acceptance criteria
B) The same story sentence with checklist acceptance criteria
C) Job stories ("When <situation>, I want to <motivation>, so I can <outcome>") with Gherkin criteria
D) Other

[Answer]: A

### Q4 — Non-functional requirements as stories
A) NFRs with user-visible effects become stories with measurable criteria (fail-closed results, rate limiting,
   limits, cancellation, portability, delivery); purely internal NFRs (architecture tests, warnings as errors) stay in
   requirements.md
B) Every NFR becomes a story
C) No NFR stories
D) Other

[Answer]: A

### Q5 — Prioritization
A) MoSCoW labels for ordering only — Must: parity features, fail-closed behaviour, approved changes; Should: remaining
   fixes; Could: cosmetic items. All approved scope stays in.
B) No prioritization
C) Numeric order (1…n)
D) Other

[Answer]: A

### Q6 — Traceability
A) Each story lists its FR / NFR IDs, decision IDs and the original defect numbers it fixes; a coverage table shows
   every FR / NFR has at least one story
B) FR / NFR IDs only
C) None
D) Other

[Answer]: A

### Q7 — Granularity
A) INVEST-sized: one story per independently testable behaviour (roughly 30–45 stories)
B) Coarse epics with sub-bullets (roughly 10–15)
C) One story per requirement (about 43)
D) Other

[Answer]: B

### Q8 — Plan approval
A) Approve this plan with the answers above and generate the stories now
B) Show me the completed plan first; I will approve it separately

[Answer]: A
