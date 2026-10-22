# Personas

Three personas, per story-plan answer Q1-A. They are roles, not people; one person can hold more than one.

## Vetting Analyst

- **Who**: the security analyst who decides whether third-party software may enter a controlled environment
  (the clinic's or an agency's systems), working from the Airlock UI on a workstation.
- **Goals**: get a verdict they can defend — which packages are inside, which carry HIGH or CRITICAL CVEs, how each
  match was made — and attach a report to the admission decision (NIST SP 800-53 CM-8, SI-2).
- **Pain points with the original**: "clean" results when NVD failed, a path was mistyped or the key was missing;
  unrelated CVEs from keyword matches shown as if they were exact; results lost on a page reload; binary deliveries
  (archives, images) could not be uploaded; the Method column always said "CPE".
- **Needs**: honest statuses (Unverified, Incomplete, Failed), clear evidence (CVE links, scores with CVSS version),
  live progress, a reliable Emergency Stop, PDF and JSON exports.
- **Primary epics**: E01, E02, E04–E12.

## Pipeline Integrator

- **Who**: a DevSecOps engineer who wires Airlock into CI so builds or software imports are gated automatically.
- **Goals**: submit a scan target over HTTP, wait for the outcome without long-held requests, fail the pipeline on
  findings or an Incomplete scan, archive the PDF.
- **Pain points with the original**: a synchronous scan call that held the request for minutes; snake_case JSON whose
  `findings` meant vulnerable packages; failures that looked like success; no machine-readable reason.
- **Needs**: a stable, documented, asynchronous contract (`/api/v2`), standard error bodies (ProblemDetails), an
  event stream, predictable status codes (202, 409, 503), retention long enough to collect results.
- **Primary epics**: E03, E04, E06, E09, E11.

## Platform Operator

- **Who**: whoever installs and runs Airlock — on a Windows development machine with `dotnet run`, or on a Linux host
  with Docker Compose — and owns its configuration.
- **Goals**: predictable configuration with safe defaults, visible health, the same behaviour on Windows and Linux,
  low maintenance and clear licensing.
- **Pain points with the original**: Emergency Stop could kill the API server's own process group and failed on
  Windows; a missing NVD key failed silently; no retries or real rate limiting against NVD.
- **Needs**: validated settings (original variable names kept), secrets that never reach the logs, health checks,
  bundled Syft, CI with tests and code scanning, documented licences.
- **Primary epics**: E12, E13, E14, E15.
