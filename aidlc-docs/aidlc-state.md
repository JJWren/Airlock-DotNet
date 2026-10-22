# AIDLC State — Airlock .NET Rebuild

**Project**: Airlock-DotNet — rebuild of github.com/JJWren/Airlock (master @ 0a62ba3) on .NET 10 LTS with a Blazor UI
**Project type**: Greenfield target workspace; brownfield source system (remote repository)
**Workflow path**: Full (decision N8)
**Current phase**: CONSTRUCTION
**Current stage**: U3 Infrastructure — Functional Design (U2 Application approved 2026-10-22T20:06:11Z)
**Last updated**: 2026-10-22T20:06:11Z

## INCEPTION PHASE

- [x] Workspace Detection — greenfield target, brownfield source (`inception/plans/workspace-detection.md`)
- [x] Reverse Engineering — `inception/reverse-engineering/` (approved via round 1 Q4)
- [x] Requirements Analysis — Comprehensive depth; approved 2026-10-20T16:58:35Z (`inception/requirements/`)
- [x] User Stories — Standard depth; 3 personas, 15 epics; approved 2026-10-21T00:41:37Z (`inception/user-stories/`)
- [x] Workflow Planning — approved 2026-10-21T00:48:27Z (`inception/plans/workflow-planning.md`)
- [x] Application Design — Standard; approved 2026-10-21T12:41:19Z with the ADR-0001 exports refinement (`inception/application-design/`)
- [x] Units Generation — Standard; 6 units; approved 2026-10-21T22:02:29Z (`inception/application-design/unit-of-work*.md`)

## CONSTRUCTION PHASE

- [x] S0 spikes — Syft artifact classes, NVD behaviour; review answered 2026-10-21T22:35:25Z
      (`construction/spikes/spike-results.md`; requirements amended, ADR-0006 added)

| Unit | Functional Design | NFR Requirements | NFR Design | Infrastructure Design | Code Generation |
|---|---|---|---|---|---|
| U1 Domain | [x] | [x] | [x] | n/a | [x] |
| U2 Application | [x] | [x] | [x] | n/a | [x] |
| U3 Infrastructure | [ ] | [ ] | [ ] | n/a | [ ] |
| U4 Web API | [ ] | [ ] | [ ] | [ ] | [ ] |
| U5 Web UI | [ ] | [ ] | [ ] | n/a | [ ] |
| U6 Delivery | [ ] | [ ] | [ ] | [ ] | [ ] |

- [ ] Build and Test

Per-unit stage applicability is confirmed during Workflow Planning.

## OPERATIONS PHASE

- [ ] Operations — placeholder

## Skipped stages

- None (Full path chosen).
