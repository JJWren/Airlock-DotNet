---
status: accepted
---

# Clean Architecture despite the small size

Airlock is split into Domain, Application, Infrastructure and Web projects with inward-only dependencies, enforced by
ArchUnitNET tests; use cases are plain classes injected directly, with no mediator library. A two-project Core + Web
split would have been enough for the size, but the original project was designed "following proposal flow chart and
CLEAN architecture", and the rebuild keeps that structure deliberately.

## Considered Options

- Core + Web, two production projects: simpler, rejected for the reason above.
- MediatR for use-case dispatch: now licensed under RPL-1.5 or commercially; a source-generated mediator was also
  unnecessary at this size.
