# U6 Delivery — Functional Design Plan

**Stage**: CONSTRUCTION — U6 Delivery — Functional Design (Minimal depth)
**Started**: 2026-10-28T14:04:16Z

## Inputs

- Requirements NFR-01, NFR-06, NFR-10 and NFR-11, with NFR-03 (where the NVD key comes from) and NFR-07 (`LOG_LEVEL`)
- Stories E14 and E15, which U6 completes, and E13, whose settings U6 documents
- `inception/application-design/unit-of-work.md` (U6) and `inception/plans/workflow-planning.md` (U6: Functional Design
  Minimal, NFR Requirements Standard, NFR Design Minimal, Infrastructure Design Standard, Code Generation Standard)
- The constraints handed to U6:
  - **U3**: Syft v1.55.0 in `bin/` next to the application; a staging root the non-root user can write; CI installs Syft
    with the install script and sets `SYFT_BINARY_PATH`, so the integration tests run on Windows and Linux; the README
    tells developers to point `SYFT_BINARY_PATH` at the repository's `bin/syft`
  - **U4** (`infrastructure-design/deployment-architecture.md`, `nfr-design/logical-components.md`):
    `ASPNETCORE_URLS=http://+:8080`, `EXPOSE 8080`, the .NET image's non-root user; compose publishing
    `127.0.0.1:${AIRLOCK_PORT:-8080}:8080`, `NVD_API_KEY` from `.env`, `SCAN_UPLOAD_PATH` read-only at `/uploads`,
    `Airlock__AllowedScanRoots__0=/uploads`; a health check on `localhost` with a command the runtime image has; a smoke
    test on the published port; the README's address, user-secrets command, configuration table, reverse-proxy and
    `AllowedHosts` guidance; one Airlock per staging root; the API tests on Windows and Linux
  - **U5** (`nfr-design/logical-components.md`, the code generation summary): time-zone data and ICU in the image, so
    not a `chiseled` one; the container's host name, which Active Logs shows; README credits for MudBlazor (MIT) and the
    fonts (SIL OFL 1.1) and the WebSockets note; the component tests on Windows and Linux; a published build for
    compressed static files and no development fallback
- The existing `scripts/install-tools.ps1` and `scripts/install-tools.sh` (U3: Syft v1.55.0 into `bin/`, checksum
  compared before extraction) and the U1 `README.md`
- The original's delivery files (reverse engineering): `Dockerfile` (`anchore/syft:v1.0.1` and `python:3.12-slim`),
  `docker-compose.yml` (the API on 8000 and a Vite web server on 3000, `${SCAN_UPLOAD_PATH}:/uploads`,
  `restart: always`), `ci.yml` (a compose build and pytest inside the container, with an `NVD_API_KEY` secret),
  `codeql.yml` (Python and JavaScript/TypeScript, least-privilege permissions), `install_tools.sh`

## Steps

- [x] Resolve the open questions below (Q-U6-1 = A, Q-U6-2 = B, 2026-10-28T14:21:38Z)
- [x] `construction/u6-delivery/functional-design/domain-entities.md` — what U6 delivers and what each holds: the image,
      the compose service and its variables (`.env.example`), the workflows and their jobs, Dependabot's ecosystems, the
      README's sections
- [x] `construction/u6-delivery/functional-design/business-rules.md` — the DR rules: running locally, the image,
      compose, the health check, CI, code scanning, dependency updates, the README, and nothing pushed without approval
- [x] `construction/u6-delivery/functional-design/business-logic-model.md` — a pull request through CI and
      `docker compose up` as flows (validated Mermaid, with text alternatives); the rule-to-check map; what is left to
      U6's NFR Requirements and Infrastructure Design
- [x] Bring `unit-of-work.md` and, where a decision needs it, the requirements in line (U6 in `unit-of-work.md`;
      NFR-10, NFR-11 and the traceability table in `requirements.md`; stories E14 and E15)
- [x] Present for approval (2026-10-28T14:30:26Z)

## Questions

### Q-U6-1 — NVD in CI
Every test in the suite runs offline: the NVD client is tested against recorded answers, and the Web tests use fakes.
U3's one live NVD test asks the real NVD, without a key, and runs only when `AIRLOCK_LIVE_NVD=1`. The original's CI
needed an `NVD_API_KEY` repository secret on every run. A full scan against the real NVD in CI needs that secret, and a
job that waits on NVD's rate limits.

A) CI stays offline: no NVD key, no secret, and the live test stays skipped. It and a full scan with a real key stay
   with Build and Test on your machine (Recommended)
B) A weekly scheduled job, which can also be started by hand, runs the live NVD test and a smoke scan in the container
   with an `NVD_API_KEY` repository secret; pushes and pull requests stay offline
C) Every push and pull request runs the live test and a smoke scan with the secret; pull requests from forks get no
   secret, so the scan skips there
D) Other

[Answer]: A

### Q-U6-2 — Publishing the image
CI builds the Docker image and starts it once, to prove it builds and answers. It could also publish it, so others pull
it instead of building it.

A) Build only: CI builds and smoke-tests the image and publishes nothing; you build it with
   `docker compose up --build`, as with the original (Recommended)
B) Publish to the GitHub Container Registry when you push a version tag (`v2.0.0`), which gives that workflow
   `packages: write`
C) Publish on every push to `main` as well as on tags
D) Other

[Answer]: B

## Decisions taken in this design (object at the gate if any is wrong)

1. **Running locally** stays as U3 and U5 left it: the install script fetches Syft into `bin/`; the README has the
   developer set `SYFT_BINARY_PATH` to it (the application's own folder is its build output), store the key with
   `dotnet user-secrets set Airlock:Nvd:ApiKey <key> --project src/Airlock.Web`, and run
   `dotnet run --project src/Airlock.Web`, which listens on `http://localhost:8080`.
2. **One image**, built from the repository: Syft v1.55.0 next to the application, the release build of Airlock, the
   ASP.NET Core 10 runtime with time-zone data and ICU, the .NET image's non-root user, port 8080 on every interface
   inside the container, and a staging root that user can write. The base images, how Syft gets in with its checksum,
   and the health check's command are Infrastructure Design's.
3. **One compose service**, `airlock`, built from the `Dockerfile`; no second web service, since the UI is in the same
   host (ADR-0001):
   - published on `127.0.0.1:${AIRLOCK_PORT:-8080}`, so only this machine reaches it until the README's steps open it;
   - `NVD_API_KEY` and `LOG_LEVEL` from `.env`, which Git ignores; `.env.example` lists every variable with no values
     that matter;
   - `SCAN_UPLOAD_PATH` mounted read-only at `/uploads`, with `Airlock__AllowedScanRoots__0=/uploads`, so the container
     scans nothing else — Airlock never writes to scan targets;
   - a named volume for the staging root, so a 2 GB upload does not fill the container's own layer;
   - host name `airlock`, which Active Logs shows as the node;
   - `restart: unless-stopped`, so a stop you asked for sticks, where the original used `always`.
4. **The health check** reports the container healthy when `GET /api/v2/system/health` answers 200 on `localhost`,
   DEGRADED included, since a missing key is a configuration matter, not a broken container (U4).
5. **CI** (`ci.yml`) on pushes and pull requests to `main`, and by hand:
   - read-only permissions;
   - on Linux and Windows: restore, build with warnings as errors, install Syft with the install script and set
     `SYFT_BINARY_PATH`, run every test with coverage, and fail when a project falls under its targets (Domain 95 % of
     lines and 90 % of branches; Application 90 % and 85 %; Infrastructure 85 % and 80 %; Web 90 % and 85 %, as U1 to
     U5 set them); the coverage reports kept as the run's artifacts;
   - on Linux: build the image, start it, and check that health answers 200 with DEGRADED (no key in CI) and that
     Control Center's shell answers on the published port, then stop it.
6. **Code scanning** (`codeql.yml`) on pull requests and pushes to `main` and once a week, with least-privilege
   permissions: C#, as E15 says, and JavaScript, which covers `airlock.js` at little cost; the original scanned both of
   its languages.
7. **Dependabot** once a week for NuGet, Docker and GitHub Actions, minor and patch updates grouped per ecosystem.
   Syft's version lives in the install scripts and the image together, so it changes by hand, in one commit, with its
   checksums.
8. **The README** describes what Airlock does (SCA, not SAST), the quick start with Docker Compose, running locally on
   Windows, Linux and macOS, every setting with its default (U4's configuration table), the UI and the API (`/docs`,
   `docs/api/problems.md`, a `curl` session), security (no authentication as an accepted risk, `AllowedHosts`, the
   cross-site refusal, TLS and WebSockets through a reverse proxy with `X-Accel-Buffering: no`, one Airlock per staging
   root), what Syft finds in each kind of artifact (the S0 findings), development (build, test, coverage, the AIDLC
   documents), and licensing: Airlock MIT; QuestPDF's Community licence and its revenue threshold; MudBlazor MIT; Inter
   and JetBrains Mono SIL OFL 1.1; Syft Apache-2.0; and NVD's notice that Airlock uses the NVD API but is not endorsed or
   certified by the NVD, which NVD's terms of use ask for.
9. **Nothing is pushed.** The workflows are written and checked locally; they run only once you create the GitHub
   repository and approve a push (NFR-11, E15). The README's links point at `github.com/JJWren/Airlock-DotNet`, as the
   problem types already do.
10. **Left to Build and Test**: E14's "Same results on both platforms" and a full scan with a real key.

### Added by Q-U6-2 = B

11. **Releases** (`release.yml`) run when the owner pushes a version tag such as `v2.0.0`, and only in
    `JJWren/Airlock-DotNet`:
    - they stop before building unless the tag, without its `v`, equals `Version` in `Directory.Build.props`, so the
      version health and the UI show is the image's tag;
    - CI's checks run on the tagged commit first;
    - the image that then passes the smoke test is the one pushed to `ghcr.io/jjwren/airlock-dotnet`, tagged `2.0.0`
      and, for a version with no pre-release suffix (`-rc.1`), `latest`;
    - it is built for linux/amd64 only, the platform the smoke test runs on (say so at the gate if you need arm64);
    - only the publishing job has `packages: write`, and it signs in with the run's own token, so no personal token is
      stored;
    - OCI labels name the image's source repository, version, commit and licence, which links the package to the
      repository.
12. **Compose runs the published image, or your copy**: the service names the published image
    (`${AIRLOCK_IMAGE_TAG:-latest}`) and also builds from the `Dockerfile`, so `docker compose up` runs the latest
    release and `docker compose up --build` builds your working copy, as E14's scenario says.
13. **Third-party notices**: the published image redistributes Syft (Apache-2.0, whose terms require a copy of its
    licence) and the NuGet packages Airlock ships (MIT, and QuestPDF's licence), so `THIRD-PARTY-NOTICES.md` lists each
    with its licence, and the image carries it with Syft's licence text and the fonts' `OFL.txt`.
14. **The README** gains a Releasing section: bump `Version`, tag the merged commit, push the tag (only the owner
    does that). GitHub creates the package on the first publish, and only the owner can set whether others may pull it
    without signing in.
