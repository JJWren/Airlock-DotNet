---
status: accepted
---

# One ASP.NET Core host; the Blazor UI works in-process

Airlock runs as a single ASP.NET Core 10 host. The Blazor UI (global Interactive Server) calls the application use
cases in-process and receives progress and the live log over its circuit, while the `/api/v2` Minimal API serves
external clients such as curl, CI pipelines and the API docs. All the heavy work (Syft, NVD) is server-side anyway, so
this gives native streaming and cancellation with one container and no CORS. The exceptions are file transfers.
The browser posts the scan form straight to `POST /api/v2/scans` over HTTP, because binary artifacts (container
images of hundreds of megabytes) are too large to stream through the SignalR circuit. Export PDF and Export JSON are
plain download links to `GET /api/v2/scans/{id}/report.pdf` and `GET /api/v2/scans/{id}`, so the UI and the API serve
identical files from one implementation.

## Considered Options

- Standalone Blazor WebAssembly plus a separate Web API, mirroring the original React + FastAPI split: two
  deployables, CORS, and server-sent events over fetch streaming.
- Hosted Interactive WebAssembly or Interactive Auto: a larger download, prerendering wrinkles, and components that
  must work in two execution modes.

## Consequences

- The UI exercises only the submission and download endpoints, so the rest of the API (events, cancel, stop,
  health, live log) needs its own integration tests.
- The UI depends on a live WebSocket connection; a dropped circuit shows as OFFLINE.
