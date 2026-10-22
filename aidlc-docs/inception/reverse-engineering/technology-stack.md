# Technology Stack — Original Airlock

| Layer | Technology | Version |
|---|---|---|
| Backend language | Python | 3.12 (`python:3.12-slim`) |
| Web framework | FastAPI | 0.135.2 |
| ASGI server | Uvicorn (standard extras) | 0.42.0 |
| Contracts and settings | Pydantic, pydantic-settings, python-dotenv | 2.13.0b2, 2.13.1, 1.2.2 |
| NVD access | nvdlib, httpx | 0.8.3, 0.28.1 |
| Retries | tenacity | 9.1.4 (declared, never used) |
| Server-Sent Events | sse-starlette | 3.3.3 |
| PDF (server) | fpdf2 | 2.8.7 |
| Multipart uploads | python-multipart | 0.0.22 |
| Logging | loguru | 0.7.3 |
| Tests | pytest, pytest-asyncio, anyio[trio] | 9.0.2, 1.3.0, 4.13.0 |
| SBOM generator | Anchore Syft (Go binary), Syft JSON output | v1.0.1 |
| Frontend | React, react-dom | ^19.2.4 |
| Build / dev server | Vite, @vitejs/plugin-react | ^8.0.1, ^6.0.1 |
| Styling | Tailwind CSS, PostCSS, autoprefixer | ^3.4.4, ^8.4.38, ^10.4.19 |
| Fonts and icons | Inter, JetBrains Mono, Material Symbols Outlined | Google Fonts (loaded by the missing `index.html`) |
| PDF (client) | jspdf, jspdf-autotable | ^2.5.1, ^3.8.2 |
| Linting | ESLint + react-hooks / react-refresh plugins | ^9.39.4 |
| Containers | Docker, Docker Compose; `anchore/syft:v1.0.1`, `python:3.12-slim`, `node:22-slim` | — |
| CI | GitHub Actions (build + pytest), CodeQL | actions/checkout@v4, codeql-action@v3 |
| IDE | PyCharm (`.idea/`) | — |
| External service | NVD CVE API 2.0 (`services.nvd.nist.gov`) | — |
