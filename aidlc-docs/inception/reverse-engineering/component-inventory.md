# Component Inventory — Original Airlock

## Backend (Python, `app/`)

| Component | File | Responsibility | Status |
|---|---|---|---|
| Application factory | `main.py` | Builds FastAPI app, CORS, mounts `/api/v1` | Used |
| Settings | `core/config.py` | `.env`-backed settings; `get_settings()` cached | Used (SyftService bypasses it via `os.getenv`) |
| Logger | `core/logger.py` | loguru stdout sink, level from `LOG_LEVEL` | Used |
| Security | `core/security.py` | — | Empty file |
| Scan state | `core/state.py` | Global `active_scan_pid` | Used |
| Router aggregator | `api/v1/scanner.py` | Mounts system / scan / logs / report routers | Used |
| Scan endpoint | `api/v1/endpoints/scan.py` | Uploads or directory → correlation → summary | Used |
| System endpoints | `api/v1/endpoints/system.py` | Health, Emergency Stop | Used |
| Logs endpoint | `api/v1/endpoints/logs.py` | SSE stream fed by a loguru sink | Used |
| Report endpoint | `api/v1/endpoints/report.py` | ScanResult → PDF | Unused by the UI |
| SyftService | `services/syft_service.py` | Runs `syft … -o json -q`, stores PID | Used |
| CorrelationService | `services/correlation_service.py` | Dedupe, CPE then keyword vetting, Semaphore(2) | Used ("the Brain") |
| NVDService | `services/nvd_service.py` | CPE search (nvdlib), keyword search (httpx), parsing | Used |
| ReportService | `services/report_service.py` | Summary metrics, fpdf2 PDF, JSON helper | Summary used; PDF unused by UI; JSON helper unused |
| CVEData / VulnerabilityReport / ScanResult | `models/vulnerability.py` | API contracts | Used |
| SBOMArtifact / SBOMResponse | `models/sbom.py` | Syft output schema | Unused |
| ScanRequest | `models/scan.py` | JSON scan request | Unused (endpoint uses form fields) |

## Frontend (React, `web/src/`)

| Component | File | Responsibility | Notes |
|---|---|---|---|
| App shell | `App.jsx` (missing) | Shared state, API calls, SSE subscription, timer, tab switching | Never committed |
| Navbar | `components/Navbar.jsx` | Brand; notifications / settings / avatar placeholders | Placeholders reload the page |
| Sidebar | `components/Sidebar.jsx` | Tabs Control Center / Active Logs / Reports; Execute Audit | |
| ConnectionStatus | `components/ConnectionStatus.jsx` | Health pill ONLINE / OFFLINE / LOADING, 10 s polling | |
| Terminal | `components/Terminal.jsx` | Log list, minimize, auto-scroll, Export Logs, Emergency Stop (confirm) | |
| VulnerabilityRow | `components/VulnerabilityRow.jsx` | Risk badge, package, version, method, findings count | Method and Risk bugs |
| ControlCenter | `views/ControlCenter.jsx` | Path input, manifest upload (`.txt,.json`), Execute, timer, mini Terminal | |
| ActiveLogs | `views/ActiveLogs.jsx` | Header ("Node: DC-04"), timer, full Terminal | Hard-coded node name |
| Reports | `views/Reports.jsx` | Loading/empty states, summary cards, filter, table, detail pane, JSON/PDF export, Rescan | |
| SummaryCard | `views/Reports.jsx` | Metric card with optional pulse | |
| Mock data | `mockData.js` | Sample ScanResult | Shows keyword false positives |

## Infrastructure and tooling

| Component | File | Responsibility |
|---|---|---|
| API image | `Dockerfile` | Syft binary + Python runtime |
| Compose | `docker-compose.yml` | api + web services, `/uploads` mount |
| Web dev image | `web/Dockerfile.dev` | Vite dev server |
| Tool installer | `scripts/install_tools.sh` | Syft v1.0.1 download per OS/arch |
| CI | `.github/workflows/ci.yml` | Compose build + pytest |
| CodeQL | `.github/workflows/codeql.yml` | Static analysis for python and JS/TS |
