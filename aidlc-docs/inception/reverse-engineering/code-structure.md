# Code Structure — Original Airlock

Source: `JJWren/Airlock` @ `0a62ba3`. Sizes in bytes.

```text
Airlock/
├─ .github/workflows/ci.yml          compose build + pytest inside the api container (needs NVD_API_KEY secret)
├─ .github/workflows/codeql.yml      CodeQL for python and javascript-typescript, least-privilege permissions
├─ .env.example                      configuration template (NVD key, app name/version, paths, ports, CORS)
├─ Dockerfile                        anchore/syft:v1.0.1 stage + python:3.12-slim runtime
├─ docker-compose.yml                api (:8000, /uploads mount) + web (:3000 Vite dev server)
├─ README.md                         product description, architecture, usage, contribution rules
├─ requirements.txt                  Python dependencies (pinned)
├─ scripts/install_tools.sh          downloads Syft v1.0.1 for the host OS/arch into ./bin
├─ test_main.http                    stale HTTP scratch file (/ and /hello/User routes that no longer exist)
├─ app/
│  ├─ main.py               (852)    app factory, CORS (hard-coded origins), router mount at /api/v1
│  ├─ core/config.py        (1347)   Settings (pydantic-settings), get_settings() cached
│  ├─ core/logger.py        (688)    loguru to stdout, level from LOG_LEVEL
│  ├─ core/security.py      (0)      empty
│  ├─ core/state.py         (50)     active_scan_pid global
│  ├─ api/v1/scanner.py     (477)    aggregates routers: /system, /scan, /logs, /report
│  ├─ api/v1/endpoints/scan.py   (2242)  POST /scan/ (form target_path + optional files)
│  ├─ api/v1/endpoints/system.py (1483)  GET /health, POST /stop
│  ├─ api/v1/endpoints/logs.py   (677)   GET /stream (SSE) + loguru sink into a global queue
│  ├─ api/v1/endpoints/report.py (718)   POST /pdf
│  ├─ models/sbom.py        (521)    SBOMArtifact, SBOMResponse (unused)
│  ├─ models/scan.py        (328)    ScanRequest (unused)
│  ├─ models/vulnerability.py (1114) CVEData, VulnerabilityReport, ScanResult
│  ├─ services/syft_service.py (1587)        runs syft, registers PID
│  ├─ services/correlation_service.py (3870) dedupe + CPE/keyword vetting orchestration
│  ├─ services/nvd_service.py (5780)         nvdlib CPE search + httpx keyword search + parsers
│  └─ services/report_service.py (2459)      summary math + fpdf2 PDF
├─ tests/
│  ├─ test_config.py        settings load (requires a real .env with an NVD key)
│  ├─ test_correlation.py   CPE hit and keyword fallback with mocked Syft/NVD
│  ├─ test_nvd.py           nvdlib result mapping with mocked to_thread
│  └─ test_syft.py          integration: real syft binary scans the repo
└─ web/
   ├─ Dockerfile.dev, package.json, package-lock.json, postcss.config.js, tailwind.config.js
   └─ src/
      ├─ components/ConnectionStatus.jsx  health pill, polls /system/health every 10 s
      ├─ components/Navbar.jsx            brand + placeholder buttons (reload the page)
      ├─ components/Sidebar.jsx           tabs: control / logs / reports + Execute Audit
      ├─ components/Terminal.jsx          log viewer, minimize, export .log, Emergency Stop
      ├─ components/VulnerabilityRow.jsx  inventory table row
      ├─ views/ControlCenter.jsx          path input, manifest upload, execute, timer, mini terminal
      ├─ views/ActiveLogs.jsx             full-height terminal + timer
      ├─ views/Reports.jsx                summary cards, filter, table, detail pane, JSON/PDF export
      └─ mockData.js                      sample ScanResult (development fixture)
```

**Missing on every branch:** `web/index.html`, `web/src/main.jsx`, `web/src/App.jsx`, `web/vite.config.js` (and any
global CSS). `App.jsx` owned the shared state (active tab, path, staged files, logs, scanning flag, elapsed time,
result, scan time) and the callbacks (`onExecute`, `onStop`, `onRescan`) that every view receives as props. The
frontend cannot be built from the repository as committed.

## Conventions observed

- Python: service classes per integration, Pydantic models per contract, emoji-prefixed "tactical" log messages.
- React: function components with hooks, Tailwind utility classes, Material Symbols icon font, uppercase monospace
  labels; no routing library (tab state), no state library, no tests.
- Git: branching model `master` / `staging` / `develop` / `feature/*`, conventional commit prefixes.
