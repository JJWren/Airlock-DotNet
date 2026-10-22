---
status: accepted
---

# Breaking API redesign: asynchronous scan resources under /api/v2

The Python API's `POST /scan` held the HTTP request open for the whole scan, used snake_case, and named vulnerable
packages `findings`, contradicting the domain language. It was replaced without a compatibility layer: scans are
resources (`POST /api/v2/scans` returns 202 and an id; clients then poll, stream events, cancel, or fetch the PDF),
JSON is camelCase and named after the glossary, and errors are ProblemDetails. The `v2` prefix signals the break to
anyone who knew the original contract.

## Consequences

- Finished scans must be kept so clients can fetch results later; they live in memory (24 hours, at most 20) and are
  lost on restart.
- Scripts written against the Python API must be rewritten.
