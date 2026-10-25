# U4 Web API — Functional Design Plan

**Stage**: CONSTRUCTION — U4 Web API — Functional Design (Standard depth)
**Started**: 2026-10-24T02:40:04Z

## Inputs

- Requirements FR-02, FR-03, FR-16 to FR-23; NFR-03, NFR-05, NFR-07
- Stories E02, E03 and E11, which U4 completes, and the scenarios of E01, E04, E06, E08, E09, E12, E13 and E14 that the
  story map gives U4
- `inception/application-design/component-methods.md` (Web API section), `services.md` (lifecycle, outcomes, streams)
  and `unit-of-work.md` (U4)
- ADR-0001 (one host; the UI posts uploads and downloads exports over HTTP) and ADR-0003 (the `/api/v2` redesign)
- The U2 use cases, `Outcome<T>` and `RefusalCode`, the scan events, `AirlockOptions`, and the constraints U2 and U3
  hand to U4 (`u2-application/code/code-generation-summary.md`, `u3-infrastructure/code/code-generation-summary.md`)
- The original API (`inception/reverse-engineering/api-documentation.md`) and its Export JSON, checked in
  `web/src/views/Reports.jsx`: `JSON.stringify(result, null, 2)` saved as `airlock_audit_<time>.json`

## Steps

- [x] Resolve the open questions below (answered: A, A, A)
- [x] `construction/u4-web-api/functional-design/domain-entities.md` — the API's own types: the multipart request, the
      scan resource and its parts, the event payloads, the health and Emergency Stop answers, the problem catalogue
- [x] `construction/u4-web-api/functional-design/business-rules.md` — the WR rules: contract-wide conventions, scan
      submission, reading a scan, the event stream, the report, cancel and Emergency Stop, health, the Live Log stream,
      errors, cross-site requests, the OpenAPI document
- [x] `construction/u4-web-api/functional-design/business-logic-model.md` — a submission and an event stream as
      sequences (validated Mermaid, with text alternatives), the status-code table, worked requests and responses, and
      the rule-to-test map with the defect regressions U4 owns
- [x] Bring `component-methods.md`, `services.md` and, where a decision needs it, the requirements in line —
      `components.md`, `component-methods.md`, `services.md`, `unit-of-work.md`, FR-20 and NFR-03 of
      `requirements.md`, and a new ADR-0007 for Q-U4-3
- [x] Present for approval (approved 2026-10-24T03:48:44Z)

## Questions

### Q-U4-1 — How enum values are written in JSON
Property names are camelCase (decided). The values of statuses, phases, severities, match kinds, reasons and codes are
not. The original API answered `ONLINE`, `TERMINATED` and `IDLE`; NVD labels severities `CRITICAL` and `HIGH`; the
screen and the PDF show those labels in capitals.

A) Capitals with underscores for every value: `RUNNING`, `SBOM_GENERATION`, `CRITICAL`, `LOOKUP_FAILED`, `ONLINE`,
   `TERMINATED` — the original's values and NVD's labels, one rule for all (Recommended)
B) camelCase for every value: `running`, `sbomGeneration`, `critical`, `lookupFailed`, `online`, `terminated` — the
   convention of the property names
C) PascalCase, as the glossary writes the terms: `Running`, `SbomGeneration`, `Critical`, `LookupFailed`, `Online`,
   `Terminated`
D) Other

[Answer]: A

### Q-U4-2 — How a client tells refusals apart
Errors are ProblemDetails (RFC 9457). Five refusals share 409 — a scan in progress, an upload stopped by Emergency
Stop, a report not ready, a scan already finished, a scan without results — so a CI script that reacts to them
differently needs a stable identifier. RFC 9457 makes the `type` URI that identifier; without one it is `about:blank`,
which means nothing beyond the status code.

A) A `type` URL per kind of problem, pointing at a problems page in the repository —
   `https://github.com/JJWren/Airlock-DotNet/blob/main/docs/api/problems.md#scan-in-progress` — which resolves once the
   repository is published (Recommended)
B) A `type` URN per kind that resolves nowhere: `urn:airlock:problem:scan-in-progress` (`airlock` is not a registered
   URN namespace)
C) ASP.NET Core's default `type` (the RFC 9110 section of the status code) and an extension member naming the kind:
   `"code": "SCAN_IN_PROGRESS"`
D) Other

[Answer]: A

### Q-U4-3 — Requests from other websites
Airlock has no authentication (D2, an accepted risk) and its API takes no antiforgery token (NFR-03), on the reasoning
that without cookies cross-site request forgery is moot. One case remains: a web page the analyst happens to visit can
make the analyst's browser send `POST` requests to a local Airlock — start a scan of any directory on the host, cancel
a scan, press Emergency Stop — without seeing the answers. A site that re-points its own host name at 127.0.0.1 (DNS
rebinding) can also read the answers, scan results included. Neither protection below affects curl or CI clients,
which are not browsers — except that option A's host list must name any other host name clients use.

A) Refuse both: a `POST` that the browser marks as coming from another site (the `Sec-Fetch-Site` header, or an
   `Origin` that differs from the host) gets 403; and Airlock answers only to the host names in `AllowedHosts`, by
   default `localhost`, `127.0.0.1` and `[::1]`, which an operator extends — for example with a CI service name such
   as `airlock` (Recommended)
B) Refuse cross-site `POST` requests only; `AllowedHosts` stays `*`, so any host name is answered
C) Neither, as the requirements stand; the README states the risk
D) Other

[Answer]: A

## Decisions taken in this design (object at the gate if any is wrong)

1. **One shape for every scan.** Every property of the scan resource is always present: `null` when it does not apply
   (no summary while Running, no failure unless Failed), `[]` for an empty list. Clients and the OpenAPI schema see one
   stable shape.
2. **JSON bodies are indented by two spaces**, as the original's Export JSON was (`JSON.stringify(result, null, 2)`),
   because FR-22 makes the export the body of `GET /api/v2/scans/{id}`. Event payloads are one compact line each.
3. **Times are UTC**, written as RFC 3339 with milliseconds and `Z` (`2026-10-22T12:00:00.000Z`); a finished scan also
   carries `durationSeconds`.
4. **Packages are flat.** Vulnerable and Unverified packages carry `type`, `name` and `version` themselves rather than
   a nested `package` object, so `jq '.vulnerablePackages[].name'` works.
5. **The match method is one object**: `{ "kind": CPE, "cpe": …, "cpeSource": DICTIONARY or GENERATED }` or
   `{ "kind": PRODUCT, "product": "minimist" }`, the members that do not apply `null`. The value case follows Q-U4-1.
6. **A finding carries the score Airlock uses** — base score, severity, CVSS version written `"3.1"`, and whether NVD
   or a CNA published it — not every score NVD lists, nor the products used for matching.
7. **Multipart fields.** `targetPath` at most once; `artifacts` repeated, also accepted as `artifacts[]`, the spelling
   FR-20 uses; field names compared ignoring case. Any other field is refused with 400 naming it, so a script written
   for the original's `target_path` and `files[]` gets a clear answer instead of "Choose a directory or upload at least
   one artifact". An `artifacts` part without a file name is refused, except the empty, nameless part a browser sends
   for an empty file input, which is skipped.
8. **Multipart only.** Any other content type is refused with 415 before the active slot is claimed. The original's
   README promised a JSON body for directory scans that its code never accepted (defect 18); `curl -F targetPath=…`
   covers that case.
9. **Refusals stop the upload at once.** The slot is reserved before the body is read (services.md), and reading
   stops at the first refusal — including "not both", as soon as a non-blank `targetPath` and an artifact have both
   arrived.
10. **Cancel answers 200 or 202.** 200 with the scan when it finished within the grace period — normally Cancelled, or
    Completed if it finished first; 202 with the scan and a `Location` when the cancellation is still under way.
11. **Health carries Airlock's version**, as the original's `{"status":"ONLINE","version":"1.0.0"}` did, and answers
    200 when ONLINE and when DEGRADED alike; the body says which.
12. **Event streams.** No event ids: a reconnecting client receives a fresh snapshot rather than a replay. `finished`
    carries the final status, and the server then ends the stream; a browser `EventSource` must close on `finished`,
    or it reconnects and receives the snapshot and `finished` again. Log events on a scan's stream leave out the scan
    id; Live Log events carry it (`null` outside a scan).
13. **Every error under `/api` is a ProblemDetails**: unknown routes (404), wrong methods (405), malformed bodies
    (400), other content types (415), the server's own body limit (413) and unexpected errors (500), each with a
    `traceId` and never exception text or internal paths.
14. **API responses carry `Cache-Control: no-store`**, so no browser or proxy shows a stale status.
15. **The OpenAPI document (`/openapi/v2.json`) and Scalar (`/docs`) are served in every environment**, as the
    original served its API docs at `/docs`.
16. **`Location` headers are relative**: `/api/v2/scans/{id}`.
17. **Airlock's version is 2.0.0**, the rebuild of the original's 1.0.0, matching `/api/v2`; health reports it (added
    while writing the design).
18. **Text is written as it is**, escaping only what JSON requires, as the original's `JSON.stringify` did: a CVE
    description's `Flask's` and the Live Log's emoji are not written as `'` or `🚀` (added while writing
    the design).
