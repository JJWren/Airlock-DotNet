# Code Quality Assessment — Original Airlock

## Tests

| Test file | Covers | Notes |
|---|---|---|
| `tests/test_correlation.py` | CPE hit; keyword fallback | Mocks Syft and NVD; the strongest tests in the repo |
| `tests/test_nvd.py` | nvdlib result mapping | Mocks `asyncio.to_thread` |
| `tests/test_syft.py` | Real Syft scan of the repo | Integration; needs the binary |
| `tests/test_config.py` | Settings load | Needs a real `.env` with an NVD key (CI secret) |

No API tests, no frontend tests, no test of the summary math, the PDF, the log stream or the Emergency Stop.

## Verified defects and documentation drift

The rebuild addresses every item (requirements decisions Q5–Q11).

| # | Defect | Where | Effect |
|---|---|---|---|
| 1 | Method column always shows "CPE" | `VulnerabilityRow.jsx` checks `KEYWORD_MATCH`; `correlation_service.py` emits `NVD-KEYWORD-MATCH` | Keyword (low-confidence) matches look like CPE matches |
| 2 | Risk badge uses the first CVE's severity | `VulnerabilityRow.jsx` `vulnerabilities[0]?.severity` | Package can show HIGH while holding a CRITICAL |
| 3 | Emergency Stop is partial and unsafe | `system.py`, `syft_service.py`, `state.py` | Only Syft is killed; the NVD phase keeps running and the API answers IDLE. Syft is spawned without its own process group, so `killpg` targets the server's own group: outside Docker it kills the API server too; inside Docker the server survives only because PID 1 ignores SIGKILL from its namespace; on Windows `os.killpg` does not exist (HTTP 500) |
| 4 | Live log is shared, unbounded and not thread-safe | `logs.py` | One global asyncio queue: each message reaches only one subscriber; grows without bound with no subscriber; `put_nowait` is called from worker threads (`asyncio.to_thread`), which asyncio queues do not support |
| 5 | Upload path traversal | `correlation_service.py` `os.path.join(tmp, filename)` | A crafted filename can write outside the temp directory |
| 6 | Bad path reported as clean | `syft_service.py` swallows errors → `{"artifacts": []}`; 404 branch in `scan.py` unreachable | A mistyped path yields "0 packages, all clean" |
| 7 | Missing NVD key reported as clean | `nvd_service.py` raises; `correlation_service.py` catches and returns nothing | Every package silently clean |
| 8 | NVD failures reported as clean | `nvd_service.py` returns `[]` on any error or non-200 | Outages and rate limiting produce false "clean" results (fails open) |
| 9 | Rate limiting approximate; retries missing | `nvd_service.py` 0.6 s sleep × Semaphore(2); tenacity unused | Can exceed 50 requests / 30 s; no retry on 403/429/5xx |
| 10 | Keyword search false positives | `get_cves_by_keyword("name version")` | `mockData.js`: `requests 2.25.1` reported with CVE-2023-29530 (Laminas Diactoros) and CVE-2024-36401 (GeoServer) |
| 11 | CVSS parsing inconsistent | `_parse_nvd_response` (v3.1 only) vs `_process_nvdlib_results` (v3.1 → v3.0 → v2); UI label "CVSS v3.1 Score" | CVEs without v3.1 metrics drop below the threshold on the keyword path; label can be wrong |
| 12 | Keyword fallback fires after a valid CPE match | `_vet_package` | A correctly matched package with only MEDIUM CVEs gets keyword false positives |
| 13 | Uploads scanned per file without cross-file dedupe; binaries rejected | `scan.py` | Duplicate packages counted twice; README's "software artifacts" not supported |
| 14 | Synchronous scan, no progress | `scan.py` | The request stays open for minutes; README's "scan progress via SSE" is only the raw log |
| 15 | Single global PID | `state.py` | Concurrent scans overwrite each other's PID |
| 16 | Navbar placeholders reload the page | `Navbar.jsx` `window.location.reload()` | Clicking them wipes the scan result |
| 17 | Entry animations do nothing | `animate-in`, `fade-in`, `slide-in-from-right` classes | `tailwindcss-animate` is not installed |
| 18 | README drift | README vs code | CycloneDX claimed (Syft JSON used); `ALLOWED_HOSTS` ignored; server PDF endpoint unused; JSON scan payload documented but form fields required |

## Additional observations

- Reports header shows `new Date()` at render time, not the scan's time; row selection compares `package_name` only.
- `SyftService` reads `SYFT_BINARY_PATH` with `os.getenv`, bypassing `Settings`.
- The fpdf2 report truncates every description to 150 characters and always appends "...".
- Test dependencies are installed in the runtime image; the container runs as root; the source tree is bind-mounted.
- `test_main.http` targets routes (`/`, `/hello/User`) that no longer exist.

## Security posture

- No authentication or authorization (`core/security.py` is empty).
- `target_path` lets any caller scan any path the server process can read.
- Upload path traversal (defect 5); fail-open results (defects 6–8).
- CORS allows credentials from fixed localhost origins.
- The NVD key lives in `.env` (git-ignored); CodeQL runs weekly and on PRs.

## Maintainability

- Clear service separation and readable code; Pydantic contracts.
- Global mutable state and module-level singletons make testing and concurrency fragile.
- The frontend cannot be rebuilt from the repository (missing entry files).
