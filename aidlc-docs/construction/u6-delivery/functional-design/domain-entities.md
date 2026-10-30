# U6 Delivery — Domain Entities (delivered files)

**Stage**: CONSTRUCTION — U6 Delivery — Functional Design (Minimal depth)

U6 adds no code entities. It delivers the files that build, run, check, publish and describe Airlock. This document
lists them and what each holds. Rules are DR-nn in `business-rules.md`; the flows and the rule-to-check map are in
`business-logic-model.md`. Q-U6-1 = A keeps CI offline; Q-U6-2 = B publishes the image on version tags. U6's NFR
Requirements chose the chiseled runtime image, the image scan, attestations and pinning (Q-U6-N1 to Q-U6-N4 = A;
DR-27 to DR-31). U6's NFR Design added the smoke test's compose override, the scripts the workflows share and the
list of expected start warnings, and put the data-protection keys on the volume (`../nfr-design/`). Infrastructure
Design named and placed them, moved `global.json` to SDK 10.0.403 (Q-U6-I1 = A) and added the CI tools' Dockerfile and
the repository tests' project (`../infrastructure-design/`).

## The files

| File | What it is | Rules |
|---|---|---|
| `Dockerfile` | The one multi-stage image: a Syft stage, an SDK build stage and a chiseled ASP.NET Core runtime stage, each pinned by digest | DR-02 to DR-05, DR-10, DR-24, DR-27 |
| `.dockerignore` | What never enters the image's build context | DR-06 |
| `docker-compose.yml` | One service, `airlock`, which runs the published image or builds this copy, and runs it hardened | DR-07, DR-08, DR-29 |
| `.env.example` | Every variable the compose file reads, each with a comment, and no secret values | DR-09 |
| `.github/workflows/ci.yml` | The workflow checks; build, test and coverage on Linux and Windows; the image's smoke test and scan on Linux | DR-11 to DR-14, DR-28, DR-30 |
| `.github/workflows/codeql.yml` | Code scanning for C# and JavaScript | DR-15 |
| `.github/workflows/release.yml` | Publishing the tested, scanned image to the GitHub Container Registry with its attestations, on a version tag (Q-U6-2, Q-U6-N3) | DR-19 to DR-25, DR-30, DR-31 |
| `.github/dependabot.yml` | Weekly update proposals for NuGet, Docker, GitHub Actions and the .NET SDK, with a cooldown | DR-16, DR-27 |
| `.github/zizmor.yml` | zizmor's settings: a commit SHA required for every action; the allow-list of GitHub's own actions; each accepted finding with its reason: the .NET SDK's missing cooldown, and the workspace form of `release.yml`'s call of `ci.yml`, which actionlint needs (Code Generation) | DR-28 |
| `.grype.yaml` | Grype's policy (a fixable High or Critical fails) and the accepted findings, each naming its package, with a reason that starts with its review date. Code Generation's first scan found 12 in Syft v1.55.0's binary, accepted to 2026-11-30 (Q-CG6-2) | DR-30 |
| The health probe | `--health-probe`, a mode of `Airlock.Web` that asks the health endpoint without a shell (Infrastructure Design) | DR-10 |
| The keys' setting | `Airlock:DataProtection:KeysPath`, read by `AirlockHost`; the image sets it to the volume (Infrastructure Design) | DR-03 |
| `global.json` | SDK 10.0.403 with `rollForward: latestPatch`, .NET 10's last feature band (Q-U6-I1) | DR-01, DR-12 |
| `.github/ci-tools/Dockerfile` | The CI tools' images, pinned by digest and never built: Grype, actionlint, zizmor and BuildKit | DR-27 |
| `tests/smoke/compose.smoke.yml` | The smoke override: a compose file that only the smoke test adds to `docker-compose.yml`. It sets the built image and the folder behind `/uploads`, and for the second phase the stand-in key and an unreachable NVD; `tests/smoke/uploads/` holds its `requirements.txt` | DR-13 |
| `scripts/ci/` | The workflow scripts: the tool runner, the build, the smoke test, the image scan, the promotion, the release check, the coverage checker and the run summary, which the workflows call and a developer can run | DR-12, DR-13, DR-20 to DR-22, DR-30 |
| `tests/smoke/expected-warnings.txt` | The warnings a clean start may log, each explained in the README | DR-17 |
| `licenses/` | Syft's licence, which its image does not carry; QuestPDF's licence, with the texts its package gives for the projects its native libraries are built from; ASP.NET Core's notices, which two of the packages ship (Code Generation) | DR-05 |
| `tests/Directory.Build.props` | Gives each test project's `dotnet test` run a results folder of its own, `TestResults/<project>/`, so the coverage checker reads each project's coverage from its own tests' report (Code Generation) | DR-12 |
| `README.md` | The complete README, replacing U1's placeholder | DR-01, DR-17, DR-18, DR-25 |
| `THIRD-PARTY-NOTICES.md` | Every component the image redistributes, with its licence (Q-U6-2) | DR-05 |
| `.gitignore` | Gains `docker-compose.override.yml`, where an operator keeps local settings | DR-09 |
| `tests/Airlock.Delivery.Tests` | The repository tests, which read YAML with YamlDotNet: Syft's one version and its hashes; the SDK's band; the notices' completeness; `.env.example` against the compose file; the README's licensing lines and its expected warnings; the pins; the workflows' permissions, time limits and concurrency groups, and the release's guards; compose's hardening and the smoke override's keys; `.grype.yaml`'s entries; the CI tools' versions; Dependabot's LTS rule | DR-04, DR-05, DR-09, DR-16, DR-18, DR-27, DR-29, DR-30 |

`scripts/install-tools.ps1` and `scripts/install-tools.sh` gain the SHA-256 of each Syft archive they can fetch (NFR
Design). Otherwise they change only with Syft's version (DR-04).

## The image

| Part | What | Rule |
|---|---|---|
| The application | A Release `dotnet publish` of `src/Airlock.Web`, run in the Production environment. A published build serves the compressed static files and the packages' assets, which a run from the build output does not (U5) | DR-02 |
| Syft | v1.55.0 from Anchore's own image, pinned by digest, at `bin/syft` next to the application, where U3 IR-06 finds it with no setting | DR-02, DR-04, DR-27 |
| Runtime | Chiseled Ubuntu 24.04 with ICU and time-zone data (U5): `mcr.microsoft.com/dotnet/aspnet:10.0.14-noble-chiseled-extra`, pinned by digest, with no shell and no package manager (Q-U6-N1) | DR-02, DR-27 |
| User | The chiseled image's default non-root `app` user, UID 1654. The application and Syft belong to root, so that user cannot change them | DR-02, DR-03 |
| Writable places | The staging root and, beside it, the folder where ASP.NET Core keeps its data-protection keys, both on the volume's mount and created owned by `app`; and `/tmp`. Under compose, the rest is read-only | DR-03, DR-29 |
| Settings | `URLS=http://+:8080`, the base image's `ASPNETCORE_HTTP_PORTS` cleared, and `EXPOSE 8080` (U4; NFR Design); `Airlock__Staging__Root=/var/lib/airlock/staging` and `Airlock__DataProtection__KeysPath=/var/lib/airlock/keys`, on the volume (Infrastructure Design) | DR-02, DR-03 |
| Notices | `LICENSE`, `THIRD-PARTY-NOTICES.md` and Syft's licence text; the fonts' `OFL.txt` files are already among the static files | DR-05 |
| Health check | `/usr/bin/dotnet /app/Airlock.Web.dll --health-probe` asks `/api/v2/system/health` on `localhost:8080`, without a shell; a 200 answer means healthy. Every 30 seconds, and every 2 seconds during a 30-second start period | DR-10 |
| Labels | OCI labels for the source repository, version, commit, licence, title, description and creation time | DR-24 |

Not in the image: the SDK, the source code, the tests, `.git`, any `.env` file, and the developer's own `bin/` folder
(DR-06).

**Published as** (DR-22): `ghcr.io/jjwren/airlock-dotnet:X.Y.Z` for every release, and `:latest` for a release with
no pre-release suffix. The image is built for linux/amd64 only. Registry names are lowercase, so the repository's
`JJWren/Airlock-DotNet` becomes `jjwren/airlock-dotnet`. Each published version carries signed build provenance and an
SBOM as GitHub artifact attestations, and its tags are set only after they are stored (DR-22, DR-31).

## The compose service — `airlock`

| Setting | Value | Why |
|---|---|---|
| Image | `ghcr.io/jjwren/airlock-dotnet:${AIRLOCK_IMAGE_TAG:-latest}` | `docker compose up` runs a published release |
| Build | The `Dockerfile` in this folder | `docker compose up --build` builds this copy instead (E14) |
| Port | `127.0.0.1:${AIRLOCK_PORT:-8080}:8080` | Only this machine reaches it until the README's steps open it (U4); the port can move without editing the file |
| `NVD_API_KEY` | From `.env`, blank by default | Without it Airlock runs DEGRADED, as locally (E13) |
| `LOG_LEVEL` | From `.env`, `Information` by default | Console verbosity (NFR-07) |
| `Airlock__AllowedScanRoots__0` | `/uploads` | The container scans nothing else (U4) |
| Scan folder | `${SCAN_UPLOAD_PATH}` mounted read-only at `/uploads`; required | Airlock never writes to what it scans (DR-08) |
| Staging | The named volume `airlock-data` on `/var/lib/airlock`, holding the staging root and the data-protection keys | A 2 GB upload does not fill the container's own layer, and the keys survive a restart |
| Host name | `airlock` | Active Logs shows it as the node (U5) |
| Restart | `unless-stopped` | It comes back after a crash or a reboot, but a stop you asked for sticks. The original used `always` |
| Root filesystem | Read-only, with writable mounts for the volume (staging and keys) and `/tmp` (in memory, 64 MiB) | Nothing else can change while it runs (DR-29) |
| Capabilities | All dropped, and `no-new-privileges` | Airlock needs none: it listens above port 1024 and runs Syft as its own user (DR-29) |
| Init | `init: true` | Docker's init process forwards stop signals and reaps child processes (DR-29) |

The stop grace period stays at Docker's 10 seconds, which covers Airlock's graceful stop (U4).

## Compose variables (`.env`)

| Variable | Required | Default | Meaning |
|---|---|---|---|
| `SCAN_UPLOAD_PATH` | Yes | — | The host folder that scans of `/uploads` read, mounted read-only. The original's name |
| `NVD_API_KEY` | No | Blank: DEGRADED | The NVD API key. The original's name |
| `AIRLOCK_PORT` | No | `8080` | The port on `127.0.0.1` |
| `LOG_LEVEL` | No | `Information` | Console verbosity; the Live Log is unaffected (E13). The original's name |
| `AIRLOCK_IMAGE_TAG` | No | `latest` | Which release `docker compose up` runs; set `2.0.0` to pin one |

Any other setting in the README's configuration table goes in a `docker-compose.override.yml` under `environment:`,
for example `AllowedHosts` or `Airlock__Limits__MaxArtifactsPerScan`. Compose merges that file on its own, and Git
ignores it.

## The workflows

| Workflow | Runs on | Jobs | Permissions |
|---|---|---|---|
| `ci.yml` | Pushes and pull requests to `main`; by hand; when `release.yml` calls it | **lint** on `ubuntu-24.04`; **test** on `ubuntu-24.04` and `windows-2025`; **image** on `ubuntu-24.04`, skipped when `release.yml` calls it | `contents: read` |
| `codeql.yml` | Pull requests and pushes to `main`; once a week | **analyze** for `csharp` and `javascript-typescript` | `contents: read`, `security-events: write`, `actions: read` |
| `release.yml` | A pushed tag `vX.Y.Z` or `vX.Y.Z-suffix`, in `JJWren/Airlock-DotNet` only | **version**, then CI's lint and test jobs, then **publish** | `contents: read`; the publish job adds `packages: write`, `id-token: write` and `attestations: write` |

No workflow reads a repository secret. Each run uses only its own token, so pull requests from forks get the same
checks (Q-U6-1 = A).

## Coverage targets that CI enforces (DR-12)

| Project | Lines | Branches | Set by |
|---|---|---|---|
| `Airlock.Domain` | 95 % | 90 % | U1-NFR-06 |
| `Airlock.Application` | 90 % | 85 % | U2-NFR-13 |
| `Airlock.Infrastructure` | 85 % | 80 % | U3-NFR-13, with the Syft integration tests included |
| `Airlock.Web` | 90 % | 85 % | U4-NFR-14, U5-NFR-14, Razor components included |

Each target is the line and branch coverage of that project's own code, read from its own test project's report. The
generated code is excluded, as each unit measured it. `Airlock.Delivery.Tests` tests files, not code, so it has no
target.

## Dependabot (DR-16)

| Ecosystem | Watches | Schedule | Grouping | Waits after a release |
|---|---|---|---|---|
| `nuget` | `Directory.Packages.props` (Central Package Management) | Weekly | Minor and patch updates in one pull request; each major update on its own | 7 days |
| `docker` | The `Dockerfile`'s base images, Syft's among them, and the CI tools' images in `.github/ci-tools/Dockerfile`, by digest | Weekly | As for NuGet, except that Syft's update comes alone | 7 days, except Microsoft's .NET images |
| `github-actions` | The actions the three workflows use, by commit SHA | Weekly | As for NuGet | 7 days |
| `dotnet-sdk` | The SDK in `global.json` | Weekly | As for NuGet | None |

Security updates never wait (U6-NFR-02). Major updates of the .NET SDK, Microsoft's .NET images and the
`Microsoft.Extensions.*` and `Microsoft.AspNetCore.*` packages are ignored, so Airlock stays on .NET 10 LTS (NFR
Design). zizmor reports that the SDK's entry has no cooldown; `.github/zizmor.yml` accepts that one finding, with its
reason.

## The README's sections (DR-17)

| Section | Says | From |
|---|---|---|
| What Airlock is | It gates third-party software. It is SCA: an SBOM from Syft, CVEs from NVD, findings at HIGH and above. It is not SAST. What changed from the original | Requirements; GLOSSARY.md |
| Quick start | `.env` from `.env.example`, then `docker compose up`, then open `http://localhost:8080` | DR-07 to DR-09 |
| Running from source | The SDK, the install script, `SYFT_BINARY_PATH`, the user-secrets key, `dotnet run` | DR-01 |
| Configuration | Every setting, with its key, environment variable, default and range; the original names first | U4 infrastructure design; the options' defaults |
| Using Airlock | Control Center, Active Logs, Reports, the exports; DEGRADED and OFFLINE; one scan at a time | U5 |
| The API | `/docs`, a `curl` session (submit, follow, fetch, PDF), the problem types in `docs/api/problems.md` | U4 |
| What Syft finds | Which kinds of artifact give packages and which give none (the S0 findings) | `construction/spikes/spike-results.md` |
| Security | No authentication, as an accepted risk; loopback by default; `AllowedHosts`; the cross-site refusal (ADR-0007); TLS and WebSockets through a reverse proxy, with `X-Accel-Buffering: no` for the event streams; one Airlock per staging root; the container's hardening and its writable paths | U4, U5; DR-29 |
| Troubleshooting | `docker compose logs`, and the warnings a start may log; the API and `/docs`; Syft run directly with `docker compose exec`, since the image has no shell; `docker compose pull` to return to the release after `--build` | DR-29; Q-U6-N1 |
| Releasing | How the owner cuts a release, and the package's visibility; verifying a published image's attestations with `gh attestation verify` | DR-25, DR-31 |
| Development | Build, test, coverage, the architecture tests, the ADRs, the AIDLC documents; accepting an image-scan finding in `.grype.yaml`, with its review date; running the smoke test and the image scan locally | U1 to U5; DR-30 |
| Licensing and credits | Airlock MIT; QuestPDF's Community licence and its revenue threshold; the third-party notices; the NVD notice; credit to the original Airlock | DR-18 |
