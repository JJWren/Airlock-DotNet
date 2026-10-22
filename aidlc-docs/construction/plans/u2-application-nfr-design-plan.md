# U2 Application — NFR Design Plan

**Stage**: CONSTRUCTION — U2 Application — NFR Design (Standard depth)
**Started**: 2026-10-22T10:44:54Z

## Inputs

- `construction/u2-application/nfr-requirements/` (U2-NFR-01 to U2-NFR-14, tech-stack decisions)
- `construction/u2-application/functional-design/` (AR-01 to AR-35)
- `inception/application-design/services.md`, `component-methods.md` (Application section)

## Steps

- [x] Choose a pattern for each U2 NFR, and the values left to this stage →
      `construction/u2-application/nfr-design/nfr-design-patterns.md`
- [x] Lay out the Application's namespaces, their connections (validated Mermaid, with a text alternative), the checks
      and the constraints handed to U3 → `construction/u2-application/nfr-design/logical-components.md`
- [x] Feed back two refinements found while designing: AR-05 refuses a path the Domain would reject ("Target path is
      not valid"), and `IHostFileSystem` gains `IsAbsolute` so the absolute-path rule follows the host (U2-NFR-12) —
      in `business-rules.md`, `domain-entities.md`, `business-logic-model.md`, `component-methods.md` and `services.md`
- [x] Present for approval (approved 2026-10-22T13:13:52Z)

No open questions. The defaults chosen here are listed for review at the checkpoint: `MaxParallelism` 4,
`OutageThreshold` 5, `CancelGracePeriod` 5 seconds, 1,000 events kept per subscriber, and the allowed range of every
option.
