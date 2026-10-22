# Unit of Work Plan

**Stage**: INCEPTION — Units Generation (Standard depth)
**Started**: 2026-10-21T12:41:19Z

## Inputs

- `../requirements/requirements.md`, `../user-stories/stories.md` (E01–E15)
- `../application-design/` (components, component methods, services, component dependencies)
- `workflow-planning.md` (approved sequence and per-unit stage depths)

## Decomposition approach

- Units follow the Clean Architecture layers and are built inward-out: one production project each for U1 Domain,
  U2 Application and U3 Infrastructure; the Web project is split into U4 Web API (host and endpoints) and U5 Web UI
  (Blazor); U6 Delivery packages and automates the result.
- The S0 spikes run before U1 and feed facts into U2 and U3.
- Every unit ends compiling with warnings as errors, with its tests green and with the architecture rules that apply
  to it enforced; a unit is not started until the previous one passed its Code Generation checkpoint.
- An epic is complete when its last contributing unit finishes and all of its Gherkin scenarios pass.

## Checklist

- [x] Define each unit's purpose, scope, deliverables, tests and done criteria → `unit-of-work.md`
- [x] Define dependencies, sequence and the contracts each unit freezes → `unit-of-work-dependency.md`
- [x] Map every epic to its units → `unit-of-work-story-map.md`
- [x] Validate the dependency diagram
- [x] Approval (2026-10-21T22:02:29Z)

## Refinements to the approved plan

- The Syft install scripts (`scripts/install-tools.ps1`, `scripts/install-tools.sh`) move from U6 to U3, because U3's
  Syft integration tests need the binary on the development machine.
- `Airlock.Architecture.Tests` is created in U1 and gains its rules unit by unit, as each project appears.
- U1 adds a minimal `README.md` (purpose, status, links to the glossary and ADRs); U6 completes it.
