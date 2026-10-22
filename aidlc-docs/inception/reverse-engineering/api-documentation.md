# API Documentation — Original Airlock (`/api/v1`)

Base URL in development: `http://localhost:8000/api/v1`. Interactive docs: Swagger UI at `/docs`, schema at
`/openapi.json`. No authentication. CORS allows `http://localhost`, `http://localhost:3000`, `http://localhost:8000`
with credentials (hard-coded in `app/main.py`; the `ALLOWED_HOSTS` setting is parsed but ignored).

## GET `/system/health`
Heartbeat for the ConnectionStatus pill.
- **200** `{"status": "ONLINE", "version": "1.0.0"}`

## POST `/system/stop`
Emergency Stop. Kills the process group of the stored Syft PID with SIGKILL.
- **200** `{"status": "TERMINATED", "message": "Vetting group destroyed."}` when a Syft PID was stored.
- **200** `{"status": "IDLE", "message": "No active vetting sequence detected."}` when none was stored (including while
  NVD correlation is still running).
- **500** `{"detail": "System termination failure"}` (for example on Windows, where `os.killpg` does not exist).

## POST `/scan/`
Runs a whole scan synchronously and returns its result.
- **Request:** `multipart/form-data`
  - `target_path` (string, **required** even when files are uploaded)
  - `files` (zero or more files; read and decoded as UTF-8 text)
- **200** `ScanResult` (below).
- **400** `{"detail": "Invalid file encoding. Use UTF-8 .txt or .json"}` — an upload is not UTF-8.
- **404** `{"detail": "Target directory does not exist on host."}` — declared but unreachable (Syft errors are
  swallowed and produce an empty result instead).
- **500** `{"detail": "Internal Engine Error: <ExceptionTypeName>"}`
- README drift: it documents a JSON body `{ "target_path": "/app" }`, but the endpoint only accepts form fields.

## GET `/logs/stream`
Server-Sent Events. Each loguru message (DEBUG and above) is sent as `data: <message>`. One global queue serves all
subscribers.

## POST `/report/pdf`
- **Request:** `application/json` body = `ScanResult`.
- **200** `application/pdf`, `Content-Disposition: attachment; filename=Airlock_Audit_Report.pdf` — Courier layout:
  title, vulnerable-package count, per package "PACKAGE: name vversion", per CVE id/severity/score and the description
  truncated to 150 characters. Not used by the UI.

## Schemas

```json
{
  "summary": { "total_packages": 5, "vulnerable_packages": 3, "critical_count": 4, "high_count": 1 },
  "findings": [
    {
      "package_name": "pyyaml",
      "installed_version": "5.3",
      "cpe": "NVD-KEYWORD-MATCH",
      "vulnerabilities": [
        {
          "id": "CVE-2020-1747",
          "source_url": "https://nvd.nist.gov/vuln/detail/CVE-2020-1747",
          "description": "A vulnerability was discovered in the PyYAML library …",
          "base_score": 9.8,
          "severity": "CRITICAL"
        }
      ],
      "total_findings": 1
    }
  ]
}
```

- `CVEData`: `id`, `source_url`, `description`, `base_score` (float, optional), `severity` (string, optional).
- `VulnerabilityReport`: `package_name`, `installed_version`, `cpe` (the matching CPE, or `"NVD-KEYWORD-MATCH"`),
  `vulnerabilities[]`, computed `total_findings`.
- `ScanResult`: `summary` (`total_packages` = unique packages, `vulnerable_packages` = packages with any finding,
  `critical_count` / `high_count` = number of CVEs at that severity), `findings[]` (vulnerable packages only).

## Upstream API used: NVD CVE API 2.0

- `GET https://services.nvd.nist.gov/rest/json/cves/2.0?cpeName=<cpe>` (through `nvdlib.searchCVE`)
- `GET https://services.nvd.nist.gov/rest/json/cves/2.0?keywordSearch=<name> <version>` (through `httpx`)
- Header `apiKey: <NVD_API_KEY>`. Published limits: 50 requests per rolling 30 s with a key, 5 without.
