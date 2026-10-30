# U6 Delivery — Code Generation Plan

**Stage**: CONSTRUCTION — U6 Delivery — Code Generation, Part 1 (Planning) · **Depth**: Standard
**Started**: 2026-10-30T00:30:46Z · **Generated**: 2026-10-30T15:38:24Z, Part 2 (`construction/u6-delivery/code/code-generation-summary.md`)

## Inputs

- `construction/u6-delivery/`, all four approved stages:
  - the functional design: DR-01 to DR-31, the files, the flows and the rule-to-check map;
  - the NFR requirements: U6-NFR-01 to U6-NFR-18;
  - the NFR design: the patterns, and the logical components with their checks;
  - the infrastructure design: the pins, every file's settings, the workflows step by step, the release's mechanics,
    the repository tests, the downloads, and what Code Generation checks again.
- The constraints that U3, U4 and U5 handed to U6, in their code-generation summaries.
- The code: `Program.cs`, `AirlockHost`, the options with their validators, the Web tests' `AirlockWebFactory`, and
  U3's install scripts.
- S0's files in the scratchpad, so nothing more is fetched from Syft's GitHub release:
  - Syft v1.55.0's checksums file. S0 matched its Windows hash with the archive and with GitHub's asset digest.
  - The `LICENSE` from that archive. Its Git blob hash equals that of `LICENSE` at Syft's `v1.55.0` tag
    (`261eeb9e…`).
- This machine, checked on 2026-10-30:
  - .NET SDKs up to 10.0.303; 10.0.403 is not installed yet.
  - Docker Engine 29.1.3 (linux/amd64), Buildx 0.30.1 and Compose 5.0.0.
  - WSL's Ubuntu 24.04, with bash 5.2 and Docker's command line but no jq. Git Bash has neither jq nor shellcheck.
  - No container, volume or compose project named `airlock…`, and no image named `airlock-dotnet`. Ports 18080 and
    15000 are free.
- YamlDotNet 18.1.0, on nuget.org: MIT, no dependencies, no deprecation and no known vulnerability; published
  2026-06-26.

## Ground rules

- Code lives at the repository root; `aidlc-docs/construction/u6-delivery/code/` gets a summary only.
- **Nothing is committed during generation.**
  - At the completion checkpoint I propose two commits, on the branch chosen in Q-CG6-1, and make them only if you say
    so:
    1. the U6 design documents, with the earlier documents they amended and the audit entries since U5's commits;
    2. the U6 code.
  - Nothing is pushed. The workflows first run when you push (DR-26).
- **Before Part 2, you install SDK 10.0.403**: `dotnet-sdk-10.0.403-win-x64.exe`, 205.4 MiB, from
  `builds.dotnet.microsoft.com`. Building inside Visual Studio then needs 18.9 or later. Step A1 checks the SDK, and
  generation stops there without it.
- **Downloads**, made only once you approve this plan, each into a place of its own:
  - **Container images**, about 670 MiB in all (linux/amd64, compressed), each pulled by its pinned digest:

    | Image | From | Size | For |
    |---|---|---|---|
    | SDK `10.0.403-noble` | mcr.microsoft.com | 341.9 MiB | The image's build stage |
    | BuildKit `v0.34.1` | Docker Hub | 106.2 MiB | The builder |
    | ASP.NET Core `10.0.14-noble-chiseled-extra` | mcr.microsoft.com | 69.2 MiB | The runtime stage |
    | Syft `v1.55.0` | ghcr.io | 30.0 MiB, pulled twice: by the builder and into Docker | The image; its SBOM |
    | Grype `v0.121.0` | ghcr.io | 31.2 MiB | The image scan |
    | actionlint `1.7.12` | Docker Hub | 24.1 MiB | actionlint and shellcheck |
    | registry `3.1.1` | Docker Hub | 19.2 MiB | The release rehearsal only (I8) |
    | zizmor `1.31.1` | ghcr.io | 16.9 MiB | zizmor |

  - **Grype's vulnerability database**, 187.3 MiB. Grype downloads and checks it itself, into a scratchpad folder that
    is deleted at the end.
  - **NuGet packages** from nuget.org:
    - inside the image's build, Airlock.Web's 24 packages, about 66.5 MiB (QuestPDF alone is 47.5 MiB), each time
      the builder restores from nothing;
    - on this machine, YamlDotNet 18.1.0 (777 KiB), and `Microsoft.AspNetCore.App.Internal.Assets` 10.0.14 (about
      0.6 MiB). The latter holds the Blazor framework's scripts, and its version follows the SDK's runtime.
  - **jq 1.7.1 for Linux x64** (2.21 MiB), with its release's `sha256sum.txt`, from `github.com/jqlang/jq`.
    - It goes into a new scratchpad folder and is checked against that file.
    - It goes on the path only for my runs of the bash scripts in WSL.
    - 1.7.1 is the version Ubuntu 24.04 ships, so it matches the runner's.
  - Two kinds of request download nothing: A3's registry and GitHub reads, which fetch manifests and tags only, and
    I9's pull of an unpublished tag from ghcr.io, which is meant to fail.
- **Versions.** Infrastructure Design checked every pin on 2026-10-29. A3 reads each digest and SHA again before it is
  written. A change is reported, and the 7-day rule decides.
- **Docker on this machine.**
  - Everything I create carries the name `airlock-smoke`, `airlock-cg-…` or `airlock-dotnet`, checked unused first:
    the builder, the containers, the volumes and the local registry.
  - Nothing else is stopped, pruned or removed.
  - At the end, the containers, the volumes and the registry go. The builder is stopped but kept, so Build and Test
    reuses its cache. The pulled images stay.
- **No secret is read or used.**
  - No NVD key: the smoke test's canary is not one.
  - No GitHub token: zizmor runs offline here, so its online audits first run in CI.
  - The smoke test never reads a `.env` (refinement 1).
- **Every build treats warnings as errors.**
  - Analyzer findings are fixed, not suppressed. The only exceptions are the test-project ones U1 set (CA1707,
    CA1515).
  - A warning that SDK 10.0.403 newly raises in U1 to U5's code is fixed there and reported in the summary.
- **No test touches the network.**
  - The repository tests read files only, and the Web tests use loopback ports only.
  - The smoke test's NVD is a closed port on 127.0.0.1.
  - Before the commit, the staged files are checked for keys, cookies and personal paths.
- **The bash scripts are committed executable**, and the workflows still call them through `bash`. Here they run in
  WSL's Ubuntu 24.04, the runner's release.

## Design refinements made while planning

Each keeps the approved behaviour and is recorded in the design documents in step J2.

1. **The smoke test reads no `.env`.**
   - A developer's `.env` may hold a real NVD key, and Compose reads it by default.
   - The smoke test therefore gives Compose an empty env file of its own with `--env-file`, and sets every variable it
     needs in Compose's environment.
   - Naming both compose files with `-f` also keeps a `docker-compose.override.yml` out.
   - Part 2 confirms that `--env-file` replaces `.env`.
2. **The C# scripts turn native AOT off** with `#:property PublishAot=false`.
   - .NET 10 makes a file-based app native-AOT by default, which adds AOT's analyzers to its build. With warnings as
     errors, those fail on reflection-based JSON.
   - These scripts are only ever run, never published.
3. **`Airlock.Delivery.Tests` references `src/Airlock.Web` without its assembly** (`ReferenceOutputAssembly="false"`).
   - Restoring the tests then always writes the `project.assets.json` that `NoticesTests` reads.
   - Like the five other test projects, it carries the coverage extension, so `dotnet test --coverage` runs over the
     whole solution. Its report has no target.

## Steps

### A. Prerequisites, branch and pins
- [x] A1 SDK 10.0.403 is installed (`dotnet --list-sdks`); otherwise stop and ask
- [x] A2 Branch as chosen in Q-CG6-1; the uncommitted U6 design documents come along
- [x] A3 Read every pin again: each of the seven images' index digests and the five actions' commit SHAs against its
      tag, and the five Syft hashes from S0's checksums file
- [x] A4 `global.json`: 10.0.403 with `latestPatch`. Then build and test U1 to U5 unchanged on the new SDK, recording
      the counts, before anything else changes

### B. `src/Airlock.Web`
- [x] B1 `HealthProbe`, and `Program.cs`'s hand-off of `--health-probe` before any host is built (U6-NFR-11)
- [x] B2 The keys' setting, `Airlock:DataProtection:KeysPath` (U6-NFR-09):
      - validated at startup like every other setting: an absolute path or nothing, else Airlock stops and names the
        setting (E13);
      - data protection keeps its keys there, under the application name `Airlock`.
- [x] B3 Tests in `Airlock.Web.Tests`:
      - the probe against a stub server: a 200 exits 0; another status, a refused connection and the timeout exit 1;
        it prints one line and logs nothing;
      - the keys: written to the folder; a relative path refused; unset, U4's behaviour.

      The architecture tests still pass

### C. The image
- [x] C1 `Dockerfile`: the `syft`, `build` and `runtime` stages, with the environment, health check, entry point and
      labels of the infrastructure design
- [x] C2 `.dockerignore`: the allowlist, `.editorconfig` included
- [x] C3 The notices:
      - `licenses/syft/LICENSE`, from S0's verified archive;
      - `THIRD-PARTY-NOTICES.md`: Airlock.Web's restored packages, transitive ones included, each with its version,
        licence and copyright line read from the package itself; then Syft and the fonts.

### D. Compose
- [x] D1 `docker-compose.yml`, key by key as designed
- [x] D2 `.env.example`: the five variables, each with a comment and no secret value
- [x] D3 `.gitignore` gains `docker-compose.override.yml`

### E. Scripts
- [x] E1 `scripts/install-tools.ps1` and `scripts/install-tools.sh`: the SHA-256 of each of the five archives. The
      download is compared with it as well as with the checksums file (DR-04)
- [x] E2 `scripts/ci/run-tool.sh`, with the tools' entry points read from their images: zizmor's, Grype's, and
      shellcheck in actionlint's
- [x] E3 `scripts/ci/build-image.sh` (`load`, `push`)
- [x] E4 The smoke test: `scripts/ci/smoke-test.cs`, `tests/smoke/compose.smoke.yml`,
      `tests/smoke/uploads/requirements.txt` and `tests/smoke/expected-warnings.txt`
- [x] E5 `scripts/ci/scan-image.sh` and `.grype.yaml`
- [x] E6 `scripts/ci/promote.sh` (`verify`, `tag`)
- [x] E7 `scripts/ci/check-release.sh`
- [x] E8 `scripts/ci/coverage.cs`
- [x] E9 `scripts/ci/summary.cs`

### F. GitHub
- [x] F1 `.github/ci-tools/Dockerfile`
- [x] F2 `.github/workflows/ci.yml`
- [x] F3 `.github/workflows/codeql.yml`
- [x] F4 `.github/workflows/release.yml`
- [x] F5 `.github/dependabot.yml`, with the names Dependabot gives Microsoft's images confirmed from its documentation
- [x] F6 `.github/zizmor.yml`

### G. `tests/Airlock.Delivery.Tests`
- [x] G1 The project (refinement 3), in `Airlock.slnx`; YamlDotNet under Tests in `Directory.Packages.props`
- [x] G2 The infrastructure design's twelve classes: `PinTests`, `SyftVersionTests`, `SdkTests`, `WorkflowTests`,
      `DependabotTests`, `ComposeTests`, `DockerfileTests`, `GrypePolicyTests`, `ZizmorConfigTests`, `NoticesTests`,
      `ReadmeTests` and `ToolTests`

### H. README
- [x] H1 `README.md`, replacing U1's placeholder, with the sections of `domain-entities.md`:
      - the configuration table, from the options' defaults;
      - every expected warning explained;
      - links to `github.com/JJWren/Airlock-DotNet`.

### I. Verify
- [x] I1 `dotnet build` with zero warnings. Then `dotnet test` with `SYFT_BINARY_PATH` set to the scratchpad's Syft:
      every test passes, and the counts are recorded
- [x] I2 The coverage checker against the four targets; the run summary, written to a file as CI's would be
- [x] I3 Each repository test class is shown to fail once: a temporary edit breaks the rule it checks, the test names
      it, and the edit is undone
- [x] I4 actionlint, shellcheck and zizmor (offline) through `run-tool.sh`: no finding but the accepted one
- [x] I5 `build-image.sh load` in the builder:
      - the build, its configuration digest and the archive;
      - the compressed size, against 150 MiB;
      - the licence files actually in the image, correcting the design's table if it differs;
      - the probe's start-up cost.
- [x] I6 The smoke test, both phases:
      - health; the page and its stylesheets; the refused scan; UID 1654;
      - Syft's scan of `/uploads` under the read-only root, ending Incomplete; the PDF;
      - the start's warnings, with the key warning's event id recorded in `expected-warnings.txt`; the canary
        nowhere.
- [x] I7 The image scan: both SBOMs, and Grype under `.grype.yaml`. If Grype reports a fixable High or Critical, I stop
      and show it. The fix is then a newer image or Syft, if one exists, or an acceptance with its reason and review
      date, which is yours to approve
- [x] I8 The release rehearsal against a local registry:
      - `build-image.sh push`, whose configuration digest must equal the tested one's, also as read back with
        `imagetools inspect --raw`;
      - `promote.sh tag`, each tag naming D. If one does not, the registry-API fallback is written and rehearsed
        instead;
      - `check-release.sh`, refusing a tag that differs from `Version`, and a version already published.

      Attesting and verifying need GitHub, so they first run in the release
- [x] I9 Compose's fallback: `docker compose up` with an unpublished tag builds from source. If it does not, the README
      says to use `--build`
- [x] I10 Clean up as the ground rules say

Left for Build and Test, and for the first push:
- what needs GitHub: zizmor's online audits, CodeQL, the Windows runner, the push to GHCR, the attestations and their
  verification, and the tags on the real registry;
- E14 with a real NVD key, and the quick start from a fresh clone;
- stopping the container during a scan, and time zones in the container;
- U5's remaining checks.

### J. Documentation
- [x] J1 `aidlc-docs/construction/u6-delivery/code/code-generation-summary.md`: the files, the test counts, coverage,
      deviations, and what is left for Build and Test and the first push
- [x] J2 Update the design documents: refinements 1 to 3, the licence table, the event id, Compose's fallback, and any
      name changed during implementation
- [x] J3 Tick this plan; update `aidlc-state.md` and `audit.md`

## Questions

### Q-CG6-1 — Branch for U6
U5 is approved and committed on `feature/u5-web-ui`, and `main` holds U4. U6 builds on the U5 code and changes
`Airlock.Web` a little (B1, B2).

A) Fast-forward `main` to `feature/u5-web-ui`, then branch `feature/u6-delivery` from `main`, as for U2 to U5
   (Recommended)
B) Branch `feature/u6-delivery` from `feature/u5-web-ui`; `main` stays at U4
C) Keep working on `feature/u5-web-ui`
D) Other

[Answer]: A (2026-10-30T00:43:49Z)

### Plan approval
A) Request Changes
B) Approve and generate, once SDK 10.0.403 is installed. This downloads about 670 MiB of images, Grype's 187.3 MiB
   database, about 66.5 MiB of NuGet packages for each image build from nothing, and 3.6 MiB of small files

[Answer]: B (approved 2026-10-30T00:43:49Z)

### Q-CG6-2 — The image scan's findings (I7, asked 2026-10-30T14:17:12Z)
Grype, under `.grype.yaml`, fails the image on 12 vulnerabilities that have a fix, all in `/app/bin/syft`, Syft
v1.55.0's binary:
- 11 in Go's standard library, `stdlib` go1.26.8, fixed in Go 1.26.9: GO-2026-6612 and GO-2026-6604 (Critical);
  GO-2026-6603, -6605, -6607, -6608, -6609, -6610, -6611, -6613 and -6617 (High). They concern Go's HTTP/1 and HTTP/2
  server and client, multipart forms, TLS's ECH, and `Root.Mkdir` on Windows.
- 1 in `github.com/go-jose/go-jose/v4` v4.1.5, fixed in 4.1.6: GHSA-mgcc-cww5-fp2q (High), a denial of service when
  parsing JWKs with RSA keys.

Nothing else fails the gate: .NET, Airlock's packages and the Ubuntu base have only Medium and Low findings without a
fix. Syft v1.55.0 (2026-10-19) is still Syft's newest release, so no newer Syft fixes these yet. Airlock runs Syft on
local files only: it serves no HTTP and parses no JWKs.

A) Accept the 12 in `.grype.yaml`, each naming its vulnerability, its package and the location `/app/bin/syft`, with
   the reason "review-by 2026-11-30: in Syft v1.55.0's binary (Go 1.26.8, go-jose 4.1.5); gone when a Syft release
   built with Go 1.26.9 or later replaces it" (Recommended)
B) The same acceptances, to be reviewed by 2026-11-13, two weeks away
C) Accept nothing: CI's image job and any release fail until a fixed Syft is released and adopted
D) Other

[Answer]: A (2026-10-30T15:31:22Z)
