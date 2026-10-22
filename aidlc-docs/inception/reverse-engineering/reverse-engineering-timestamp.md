# Reverse Engineering — Timestamp and Provenance

- **Performed**: 2026-10-20T15:20:40Z (planning session)
- **Approved**: 2026-10-20T15:45:56Z (requirements round 1, Q4 "Accurate — rebuild as described")
- **Source**: https://github.com/JJWren/Airlock, default branch `master` @ `0a62ba314221f5635e82ebd9bcadcfe5c3fecf66`
  (2026-08-28T14:55:47Z)
- **Branches inspected**: `master`, `staging`, `develop`, `JJWren-patch-1` (identical `web/` trees)
- **Method**: read-only GitHub REST API (repository tree, commit history) and raw file content; no clone, no code
  execution
- **Coverage**: every committed file except `web/package-lock.json` (dependency lock only) and `.idea/`
- **Limitations**: the frontend entry files (`web/index.html`, `src/main.jsx`, `src/App.jsx`, `vite.config.js`) were
  never committed and no local copy exists; their behaviour was reconstructed from the views' props and confirmed by
  the user (round 1, Q4)
