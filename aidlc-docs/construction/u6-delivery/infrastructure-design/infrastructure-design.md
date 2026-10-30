# U6 Delivery — Infrastructure Design

**Stage**: CONSTRUCTION — U6 Delivery — Infrastructure Design (Standard depth)

How Airlock is built into an image, run, checked and published:
- the images and actions it pins;
- each delivery file and its settings;
- the workflows, step by step;
- the release's mechanics.

Rules are DR-nn (`../functional-design/business-rules.md`), targets U6-NFR-nn (`../nfr-requirements/`), and the
patterns are in `../nfr-design/`. Q-U6-I1 = A moves Airlock to the .NET SDK's 4xx band. Where everything runs is in
`deployment-architecture.md`. The values were read on 2026-10-29
(`../../plans/u6-delivery-infrastructure-design-plan.md`). Code Generation reads every digest and SHA again before
writing it.

## Pins (U6-NFR-01)

### Images

Each is pinned by its tag and its index digest, written `name:tag@sha256:…`.

| Image | Tag | Digest | Used by | Kept current by |
|---|---|---|---|---|
| `ghcr.io/anchore/syft` | `v1.55.0` | `sha256:4cc92b64ca74ffca4c94d4c8fcc01f217731375300be14d0b6eeb2e82eaa110a` | `Dockerfile`, stage `syft`; CI's SBOM | Dependabot, in a pull request of its own, which fails DR-04's test until Syft's other places follow |
| `mcr.microsoft.com/dotnet/sdk` | `10.0.403-noble` | `sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b` | `Dockerfile`, stage `build` | Dependabot, with no cooldown |
| `mcr.microsoft.com/dotnet/aspnet` | `10.0.14-noble-chiseled-extra` | `sha256:9b755ea799ae649141a073e30d8b62d8095a5465af2b7694d17866e8c05131d8` | `Dockerfile`, stage `runtime` | Dependabot, with no cooldown |
| `ghcr.io/anchore/grype` | `v0.121.0` | `sha256:621d56d1d68106b787022c81546f7e0a36202a25d1239f0c20195fab8c16f7ea` | `.github/ci-tools/Dockerfile` | Dependabot, after 7 days |
| `rhysd/actionlint` (Docker Hub) | `1.7.12` | `sha256:b1934ee5f1c509618f2508e6eb47ee0d3520686341fec936f3b79331f9315667` | `.github/ci-tools/Dockerfile` | Dependabot, after 7 days |
| `ghcr.io/zizmorcore/zizmor` | `1.31.1` | `sha256:42d409dbb851d4c5d35a058cf042b828bea1e3f2884378c526eb0974780e18d9` | `.github/ci-tools/Dockerfile` | Dependabot, after 7 days |
| `moby/buildkit` (Docker Hub) | `v0.34.1` | `sha256:99368f3e46177c6405b9740e4461d91cee004128daeecca78d4ceac50ec1da5c` | `.github/ci-tools/Dockerfile`: the builder of the image job and the release | Dependabot, after 7 days |

The .NET images are named by version rather than by `10.0-noble`, which is the same image today, so Dependabot's
proposals say which version they bring. Syft and Grype come from `ghcr.io`, where Anchore publishes them as well as on
Docker Hub. actionlint and BuildKit exist only on Docker Hub. Each CI job pulls at most one image from there, and if a
pull fails, so does the job, which can simply be run again.

### Actions

| Action | Version | Commit SHA | Used in |
|---|---|---|---|
| `actions/checkout` | v7.0.1 | `3d3c42e5aac5ba805825da76410c181273ba90b1` | Every job |
| `actions/setup-dotnet` | v6.0.0 | `a98b56852c35b8e3190ac28c8c2271da59106c68` | test, image, publish, and CodeQL for C# |
| `actions/upload-artifact` | v7.0.2 | `cf430e030ddbb5b0abf93d22962f4752f3646cd9` | test, image, publish |
| `actions/attest` | v4.2.2 | `1e69f48acb82d1966a394da916b4c1698aa569d6` | publish |
| `github/codeql-action` (`init`, `analyze`) | v4.39.1 | `9b3254085494b55a0f7c4c2b3d2d52cc712ce370` | CodeQL |

Each is written `uses: owner/repo@<SHA> # vX.Y.Z`, and each release was at least 7 days old when chosen.

### Downloads

Syft is the only tool CI downloads as a file: the test job runs Syft's integration tests on each platform. The
install scripts carry the SHA-256 of each archive they can fetch: `install-tools.sh` for Linux and macOS on amd64 and
arm64; `install-tools.ps1` for those and for Windows on amd64 and arm64, since it runs on PowerShell 7 everywhere and
already supported Windows on arm64. They compare the archive with that SHA-256 and with the release's checksums file. Code Generation
takes the hashes from Syft v1.55.0's checksums file and checks them against the Windows archive that S0 verified.
Grype's vulnerability database is data that Grype downloads and checks itself, 187.3 MiB on 2026-10-29. Grype refuses
a database built more than five days ago.

## The SDK (Q-U6-I1 = A)

| Where | What |
|---|---|
| `global.json` | `"version": "10.0.403"`, `"rollForward": "latestPatch"`: any 10.0.4xx patch from 10.0.403 on, and no other band. The test runner stays Microsoft.Testing.Platform |
| The image | `sdk:10.0.403-noble` |
| CI | `actions/setup-dotnet` with `global-json-file: global.json` in every job that builds. It installs 10.0.403 on `windows-2025`, which carries 10.0.402, and finds it already on `ubuntu-24.04` |
| Your machine | SDK 10.0.403, installed before Code Generation (205.4 MiB for Windows x64). Building inside Visual Studio needs 18.9 or later; the command line works either way |
| Updates | Dependabot proposes the SDK image and `global.json` separately. The image's proposal can merge first, because `latestPatch` accepts a newer patch than `global.json` names; the `global.json` proposal then builds. A repository test requires the image's SDK to be in `global.json`'s band and no older than it |

4xx is .NET 10's last feature band, and it is patched until .NET 10's end of support on 2028-11-14.

## The image (`Dockerfile`)

| Stage | From | Does |
|---|---|---|
| `syft` | Syft's image | Only a source for `/syft` |
| `build` | The SDK image | Copies `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig` and the four `src` project files. Restores `src/Airlock.Web` for `linux-x64`, then copies `src/`. Publishes in Release for `linux-x64`, framework-dependent, without an apphost (`-p:UseAppHost=false`), to `/app`. Makes `/rootfs/var/lib/airlock/staging` and `/rootfs/var/lib/airlock/keys`, mode 0700. Copies the notices into `/app`, then sets `/app`'s modes with `chmod -R u=rwX,go=rX`, so that only root may write there whatever modes the build context gave its files (Code Generation) |
| `runtime` | The chiseled-extra image | See the table below |

| Runtime item | Value | Rule |
|---|---|---|
| Application | `/app`, copied from the build stage, owned by root | DR-02, DR-03 |
| Syft | `/app/bin/syft`, owned by root, which U3 IR-06 finds with no setting | DR-02 |
| Notices | `/app/LICENSE`, `/app/THIRD-PARTY-NOTICES.md` and `/app/licenses/`: Syft's licence, QuestPDF's licence with its native libraries' texts, and ASP.NET Core's notices, all copied in the build stage | DR-05 |
| Writable folders | `/var/lib/airlock/staging` and `/var/lib/airlock/keys`, copied with `--chown=1654:1654`, so a new volume on `/var/lib/airlock` takes that owner | DR-03 |
| Environment | `URLS=http://+:8080`; `ASPNETCORE_HTTP_PORTS=` (cleared); `Airlock__Staging__Root=/var/lib/airlock/staging`; `Airlock__DataProtection__KeysPath=/var/lib/airlock/keys` | DR-02, DR-03 |
| User | `USER 1654` | DR-02 |
| Port | `EXPOSE 8080` | DR-02 |
| Health check | `HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --start-interval=2s --retries=3 CMD ["/usr/bin/dotnet", "/app/Airlock.Web.dll", "--health-probe"]` | DR-10 |
| Entry point | `ENTRYPOINT ["/usr/bin/dotnet", "/app/Airlock.Web.dll"]`, working folder `/app` | DR-02 |
| Labels | Written in the file: `org.opencontainers.image.source`, `licenses` (`MIT`), `title`, `description`. From build arguments: `version`, `revision`, `created` | DR-24 |

Notes on the image:
- **No `RUN` in the runtime stage.** The image has no shell to run one, and nothing removes the base image's files.
- **No `# syntax=` line.** BuildKit's built-in Dockerfile frontend is used, so no frontend image is pulled unpinned.
- **`/usr/bin/dotnet`** is the link Microsoft's runtime image makes and uses for its own entry point.
- **`.dockerignore` is an allowlist.** It excludes everything, then lets back in `global.json`, `Directory.Build.props`,
  `Directory.Packages.props`, `.editorconfig`, `src/`, `LICENSE`, `THIRD-PARTY-NOTICES.md` and `licenses/`. It then
  excludes `**/bin/` and `**/obj/` again. So `.env` files, `.git`, the developer's `bin/syft.exe`, the tests, the
  documents and `.claude/` cannot enter the build context (DR-06).
- **`.editorconfig` must be in the context.** `EnforceCodeStyleInBuild` reads it; without it, the image's build would
  apply other style rules than every other build.

### Licence files in the image (U6-NFR-17)

| From | Files |
|---|---|
| Airlock | `/app/LICENSE`, `/app/THIRD-PARTY-NOTICES.md` |
| Syft | `/app/licenses/syft/LICENSE`, committed from Syft v1.55.0's repository, because Syft's image carries none |
| QuestPDF | `/app/licenses/questpdf/`: its licence, and the texts its package gives for the projects its native libraries are built from (Skia, HarfBuzz, FreeType, libpng, libjpeg-turbo, libwebp, zlib, Expat, Wuffs, qpdf, libgrapheme, Lato, and the Emscripten SDK and Ninja it builds with) |
| ASP.NET Core packages | `/app/licenses/microsoft-aspnetcore/THIRD-PARTY-NOTICES.TXT`, which `Microsoft.AspNetCore.App.Internal.Assets` and `Microsoft.AspNetCore.OpenApi` both ship |
| .NET | `LICENSE.txt` and `ThirdPartyNotices.txt` in `/usr/share/dotnet`, from Microsoft's runtime archive |
| Ubuntu | The copyright file of each slice the chiseled-extra image installs, under `/usr/share/doc/`: base-files, ca-certificates, gcc-14-base (for libgcc-s1 and libstdc++6), libc6, libicu74, libssl3t64, tzdata and tzdata-legacy |

Code Generation listed the files in the built image: the table is what it found.

## Changes to Airlock.Web

1. **The health probe** (U6-NFR-11).
   - `Program.cs` hands `--health-probe` to `HealthProbe` before it builds any host.
   - The probe sends one `GET http://localhost:8080/api/v2/system/health` with a 3-second timeout. It exits 0 on a
     200, and 1 on any other answer, an error or the timeout.
   - It prints one line, `healthy` or the reason. Docker keeps that line in the container's health record
     (`docker inspect`), not in its log.
   - It builds no configuration, logging or services, so it writes nothing to Airlock's logs or the staging root.
   - Airlock.Web.Tests runs it against a stub server.
   - `AllowedHosts` must keep `localhost`, as the default does; the README says so.
2. **The data-protection keys' folder** (U6-NFR-09).
   - `AirlockHost` reads `Airlock:DataProtection:KeysPath`. When it is set, data protection keeps its keys there,
     under the application name `Airlock`.
   - The path must be absolute; otherwise Airlock stops at startup and names the setting (E13).
   - When it is unset, as when running from source, the keys stay where ASP.NET Core puts them (U4). The image sets
     it.
   - Code Generation named the pieces `HealthProbe` and `KeyStorageOptions`, with `KeyStorageOptionsValidator` and
     `KeyStorageSetup`, in `src/Airlock.Web`. The setup reads the setting unvalidated, because antiforgery reads data
     protection's options while the pipeline is built, before startup validation runs.
3. **Syft's status, found while checking U6-NFR-11.** Every health request after Syft's 30-second status cache ran
   `syft version` again and logged its result at Debug, which the Live Log keeps. The image's health check, every 30
   seconds, would so have added a line to the Live Log every half minute. `SyftStatusProbe` (U3) now logs only a
   change of status.

## The container's settings

| Variable | Set by | Value | Why |
|---|---|---|---|
| `URLS` | The image | `http://+:8080` | Listen on every interface inside the container. An unprefixed variable outranks `appsettings.json`; `ASPNETCORE_URLS` would not (U6 NFR Design) |
| `ASPNETCORE_HTTP_PORTS` | The image | Empty | The base image's `8080` would make every start warn that `URLS` overrides it |
| `Airlock__Staging__Root` | The image | `/var/lib/airlock/staging` | On the volume, never `/tmp`, which is in memory (U6-NFR-10) |
| `Airlock__DataProtection__KeysPath` | The image | `/var/lib/airlock/keys` | On the volume, outside the staging sweep |
| `Airlock__AllowedScanRoots__0` | Compose | `/uploads` | The container scans nothing else (DR-07) |
| `NVD_API_KEY` | Compose, from `.env` | Blank by default | DEGRADED without it (E13) |
| `LOG_LEVEL` | Compose, from `.env` | `Information` | Console verbosity (NFR-07) |
| `DOTNET_RUNNING_IN_CONTAINER`, `APP_UID` | The base image | `true`, `1654` | Microsoft's own |

## Compose (`docker-compose.yml`)

| Key | Value |
|---|---|
| `name` | `airlock`, so the volume is always `airlock_airlock-data` |
| `services.airlock.image` | `ghcr.io/jjwren/airlock-dotnet:${AIRLOCK_IMAGE_TAG:-latest}` |
| `build` | `context: .` |
| `hostname` | `airlock` |
| `restart` | `unless-stopped` |
| `init` | `true` |
| `read_only` | `true` |
| `cap_drop` | `[ALL]` |
| `security_opt` | `["no-new-privileges:true"]` |
| `tmpfs` | `/tmp:rw,noexec,nosuid,nodev,size=64m` |
| `ports` | `"127.0.0.1:${AIRLOCK_PORT:-8080}:8080"` |
| `environment` | `NVD_API_KEY: ${NVD_API_KEY:-}`, `LOG_LEVEL: ${LOG_LEVEL:-Information}`, `Airlock__AllowedScanRoots__0: /uploads` |
| `volumes` | A bind mount of `${SCAN_UPLOAD_PATH:?…}` on `/uploads`, read-only, written in the long syntax so that a missing folder is an error and not a new empty one; and the named volume `airlock-data` on `/var/lib/airlock` |

`SCAN_UPLOAD_PATH`'s message reads "Set SCAN_UPLOAD_PATH in .env to the folder Airlock may scan" (DR-08).

What happens when the image cannot be pulled, for example before the first release: the Compose Specification says a
service with both `image` and `build` is pulled first, and built from source when the pull fails. Code Generation
confirmed both halves with Compose 5.0.0: an unpublished tag was built from this copy and ran healthy, and an image
present in a registry but not on the machine was pulled, not built.

## The workflows

### `ci.yml`

**Triggers:**
- pushes and pull requests to `main`;
- `workflow_dispatch`;
- `workflow_call`, with one input, `image` (boolean, default `true`).

**Settings:** `permissions: contents: read`, and `concurrency: ci-${{ github.ref }}` with `cancel-in-progress: true`.

| Job | Runner | Time limit | Steps |
|---|---|---|---|
| **lint** | `ubuntu-24.04` | 10 min | Checkout. actionlint. shellcheck, from actionlint's image, on `scripts/**/*.sh`. zizmor online, with `GH_TOKEN` set to the job's token, `--config .github/zizmor.yml` and `--format github` |
| **test** | `ubuntu-24.04` and `windows-2025`, `fail-fast: false` | 30 min | Checkout. `setup-dotnet`. Syft through the platform's install script, with `SYFT_BINARY_PATH` set. `dotnet test` with Cobertura coverage and TRX reports. The coverage checker. The run summary. Upload the results, always |
| **image** | `ubuntu-24.04`, when `toJSON(inputs.image) != 'false'` | 20 min | Checkout. `setup-dotnet`. Build. Smoke test. Image scan. Run summary and uploads, always |

`inputs.image` is empty for a push or a pull request, so the image job runs then. Only `release.yml` passes `false`.
Code Generation found that `inputs.image != false`, as first designed, would skip the job on every push and pull
request: GitHub compares loosely, turning both null and false into 0. `toJSON` tells the empty input, `null`, from
`false`.

### `codeql.yml`

**Triggers:** pushes and pull requests to `main`, and every Monday at 06:17 UTC (`17 6 * * 1`).

**Settings:**
- the workflow has `contents: read`;
- the job has `security-events: write`, `actions: read` and `contents: read`;
- `concurrency` is `codeql-${{ github.ref }}`.

**Jobs:**
- one **analyze** job on `ubuntu-24.04`, with a 30-minute limit and a two-language matrix:
  - `csharp` in build mode `manual`: `setup-dotnet`, then `dotnet build src/Airlock.Web/Airlock.Web.csproj
    --configuration Release`, so the Razor components' generated code is seen;
  - `javascript-typescript` in build mode `none`;
- each uploads its results with the category `/language:<language>`.

### `release.yml`

**Triggers:** pushed tags matching `v[0-9]+.[0-9]+.[0-9]+` and `v[0-9]+.[0-9]+.[0-9]+-*`. There is no
`workflow_dispatch` (DR-19).

**Settings:** `permissions: contents: read`, and `concurrency: release-${{ github.ref }}` with
`cancel-in-progress: false`. Every job also carries `if: github.repository == 'JJWren/Airlock-DotNet'`.

| Job | Needs | Runner | Time limit | Permissions | Steps |
|---|---|---|---|---|---|
| **version** | — | `ubuntu-24.04` | 5 min | `contents: read`, `packages: read` | Checkout. `check-release.sh`: the tag as strict SemVer, equal to `Version` (DR-20), and `X.Y.Z` not yet in GHCR (DR-21). Outputs `version` and `latest` |
| **ci** | version | — | — | `contents: read` | `uses: ./.github/workflows/ci.yml` with `image: false` |
| **publish** | version, ci | `ubuntu-24.04` | 30 min | `contents: read`, `packages: write`, `id-token: write`, `attestations: write` | See "Building and promoting the image" |

## Building and promoting the image (U6-NFR-07)

The builder is BuildKit in a container: `docker buildx create --driver docker-container`, with the image pinned in
`.github/ci-tools/Dockerfile`. That driver is BuildKit's documented way to push an image by its digest alone, with no
tag. Both builds use these settings:
- `--platform linux/amd64`;
- `--provenance=false --sbom=false`, so BuildKit adds no attestations of its own and the pushed digest names a plain
  image manifest;
- `SOURCE_DATE_EPOCH` from the commit's time, so the image's timestamps and its `created` label come from the commit
  and not from the clock.

| Step | Command | Gives | Stops the release when |
|---|---|---|---|
| 1. Build | `build-image.sh load`: `docker buildx build` with two outputs, `type=docker` (into Docker as `airlock-dotnet:smoke`) and `type=oci` (an archive in the run's temporary folder) | The configuration digest C, read from the archive's manifest, which must be the one `--metadata-file` names: with two outputs the metadata names the manifest only; the archive, for the SBOM and the download size | The build fails |
| 2. Test | The smoke test, then the image scan | — | Any check fails |
| 3. Sign in | `docker login ghcr.io --username "$GITHUB_ACTOR" --password-stdin`, with the job's token through an environment variable | — | The sign-in fails |
| 4. Push | `build-image.sh push`: the same build from the builder's cache, exported with `type=image,name=ghcr.io/jjwren/airlock-dotnet,push-by-digest=true,name-canonical=true,push=true` | The manifest digest D | The export's configuration digest, or the one `docker buildx imagetools inspect --raw` reads back for D, is not C |
| 5. Attest | `actions/attest` twice, for `subject-name: ghcr.io/jjwren/airlock-dotnet` and `subject-digest: D`, with `push-to-registry: true` and `create-storage-record: false`: once with no predicate (build provenance), once with `sbom-path` (the SPDX SBOM) | Two attestations, in GitHub and as referrers of D in GHCR | Either action fails |
| 6. Verify | `promote.sh verify`: `gh attestation verify oci://ghcr.io/jjwren/airlock-dotnet@D --repo JJWren/Airlock-DotNet --signer-workflow JJWren/Airlock-DotNet/.github/workflows/release.yml`, then again with `--predicate-type https://spdx.dev/Document/v2.3` | — | Either verification fails |
| 7. Tag | `promote.sh tag`: `docker buildx imagetools create --prefer-index=false --tag …:X.Y.Z` (and `--tag …:latest` without a pre-release suffix) `ghcr.io/jjwren/airlock-dotnet@D`, then `imagetools inspect` of each tag | Tags on D | A tag names anything but D |

The second build reuses every layer of the first from the builder's cache, so the export produces the same image.
Step 4 proves it rather than assuming it. Code Generation rehearsed steps 1, 4 and 7 against a local registry: the
push kept the tested configuration digest, and `imagetools create --prefer-index=false` put both tags on D without
writing a new manifest, so the registry-API fallback was not needed. `promote.sh tag` also refuses to move a version
tag that already names another digest, whatever the version job could read (DR-21).

A failed step leaves D untagged, and a new attempt simply runs again (U6 NFR Design).

## The image scan (U6-NFR-06)

- **SBOM.** Syft runs from the `syft` stage's reference in `Dockerfile`, so it uses the same pin as the image. It runs
  with `--network none` on the OCI archive, and writes `sbom.syft.json` and `sbom.spdx.json`.
  - Grype reads Syft's own JSON, which keeps everything Syft knows about each package, the distribution included.
  - The release attests the SPDX JSON, which `actions/attest` accepts and `gh attestation verify` checks.
  - Both formats come from one run, so they describe the same catalogue.
- **Grype.** It runs from its pinned image, with the repository mounted read-only as its working folder, so it finds
  `.grype.yaml`. Its database is in a folder of the run (`GRYPE_DB_CACHE_DIR`), and it writes a table to the log and
  `grype.json`. `GRYPE_CHECK_FOR_APP_UPDATE=false`.
- **`.grype.yaml`.** `only-fixed: true`, `fail-on-severity: high`, and `ignore:`.
  - Each acceptance names `vulnerability` and `package.name`.
  - Each `reason` begins `review-by YYYY-MM-DD:`.
  - Code Generation's first scan failed on 12 vulnerabilities with a fix, all in Syft v1.55.0's binary, built with Go
    1.26.8 and go-jose 4.1.5. No newer Syft existed, and Q-CG6-2 = A accepted the 12, each limited to the location
    `/app/bin/syft`, to be reviewed by 2026-11-30.
- **The verdict.** The scan step exits with Grype's code after writing its files. The summary and upload steps run
  `always()`.

## The smoke test (U6-NFR-12)

**What it uses:**
- `scripts/ci/smoke-test.cs`, run as `dotnet run scripts/ci/smoke-test.cs -- --image airlock-dotnet:smoke`. Compose
  reads no `.env`, which may hold a real key: the test gives it an empty `--env-file` of its own, which replaces
  `.env` (checked at Code Generation), names both compose files with `--file`, and sets every variable they read;
- the compose project `airlock-smoke`, from `docker-compose.yml` and `tests/smoke/compose.smoke.yml`;
- `SCAN_UPLOAD_PATH` set to `tests/smoke/uploads`, which holds a `requirements.txt` pinning `jinja2==2.10` and
  `pyyaml==5.3`;
- `AIRLOCK_PORT` 18080 by default, so a developer's own Airlock on 8080 is not disturbed.

**The smoke override** sets only:
- `image: ${AIRLOCK_SMOKE_IMAGE:?}`;
- `pull_policy: never`;
- `environment: Airlock__Nvd__BaseUrl: ${AIRLOCK_SMOKE_NVD_URL:-https://services.nvd.nist.gov/rest/json/cves/2.0}`.

**The phases:**

| Phase | Starts with | Checks |
|---|---|---|
| Keyless | `up -d --wait`, with `NVD_API_KEY` blank | <ul><li>Healthy within 30 seconds.</li><li>Health answers 200, DEGRADED, the Syft check passing with `1.55.0` and the key check failing.</li><li>`/` answers 200, and so does every stylesheet it links.</li><li>A scan of `/uploads` is refused with 503 and the missing-key problem.</li><li>The process is UID 1654 (`docker compose top`).</li></ul> |
| NVD unreachable | `up -d --wait` again, with `NVD_API_KEY` set to the canary `airlock-smoke-canary-not-an-nvd-key` and `AIRLOCK_SMOKE_NVD_URL` set to `https://127.0.0.1:9/rest/json/cves/2.0` | <ul><li>A scan of `/uploads` is accepted with 202.</li><li>It ends Incomplete within 3 minutes, with every package Unverified and none vulnerable.</li><li>Its PDF answers 200 as `application/pdf` and starts with `%PDF-`.</li></ul> |
| End | — | <ul><li>Every `warn:` line in the log matches `tests/smoke/expected-warnings.txt`.</li><li>No log line mentions the health path.</li><li>The canary appears neither in the log nor in `docker image inspect` (configuration and history).</li></ul> |

On a failure the test prints `docker compose logs`. It always ends with `down -v`.

**The expected warnings** are lines of `category[event id]`. The list has one entry,
`Microsoft.AspNetCore.DataProtection.KeyManagement.XmlKeyManager[35]`: the warning ASP.NET Core writes when it creates
a key unencrypted, which it does at a volume's first start. Code Generation found no other warning at start, and the
README explains this one. A start is the log up to "Application started". Before it, a warning not on the list, or
any error, fails the test; after it, the second phase's scan logs the warnings and errors of its fail-closed path.

## CI tools (`.github/ci-tools/Dockerfile`)

The file holds four stages, `grype`, `actionlint`, `zizmor` and `buildkit`, each `FROM` the pinned image. It is never
built. `scripts/ci/run-tool.sh <tool>` reads the tool's image from this file, and `run-tool.sh --ref <stage>` prints
one, which is how `build-image.sh` finds BuildKit's. For `syft`, it reads the reference from `Dockerfile` instead.
shellcheck runs from actionlint's image, as `/usr/local/bin/shellcheck` (0.11.0). It then runs the tool:
- with `docker run --rm`;
- with the repository mounted read-only on `/repo` as the working folder, and an output folder mounted where the tool
  writes;
- with `--network none`, except for zizmor, whose online audits need the network, and Grype, whose database needs it.

Dependabot reads this file like any Dockerfile.

## Dependabot (`.github/dependabot.yml`)

| Ecosystem | Directories | Schedule | Groups | Cooldown | Ignored |
|---|---|---|---|---|---|
| `nuget` | `/` | Weekly | `patterns: ["*"]`, minor and patch | 7 days | Major updates of `Microsoft.Extensions.*` and `Microsoft.AspNetCore.*` |
| `docker` | `/`, `/.github/ci-tools` | Weekly | Minor and patch of everything but Syft (`exclude-patterns: ["*anchore/syft*"]`), so a Syft update comes alone | 7 days, excluding `*dotnet/*` | Major updates of `*dotnet/*` |
| `github-actions` | `/` | Weekly | Minor and patch | 7 days | — |
| `dotnet-sdk` | `/` | Weekly | — | None | Major updates |

Security updates never wait. The `*dotnet/*` pattern covers Microsoft's images however Dependabot names them, with or
without the registry. Code Generation confirms the name from Dependabot's documentation.

## zizmor (`.github/zizmor.yml`)

- `unpinned-uses` with the policy `"*": hash-pin`.
- `forbidden-uses` with `allow: [actions/*, github/codeql-action/*]`.
- `dependabot-cooldown` ignores the `dotnet-sdk` entry's line in `dependabot.yml`. A comment above the entry gives the
  reason: SDK updates carry security fixes for which Dependabot raises no alert (U6-NFR-02). zizmor 1.31.1 does not
  report this entry while the other entries have cooldowns, though it does when the entry stands alone. The
  acceptance stays, so a zizmor that reports it finds it accepted.
- `self-repository` ignores `release.yml`'s call of `./.github/workflows/ci.yml`. zizmor 1.31 asks for GitHub's newer
  `$/` form, which actionlint 1.7.12 rejects, so the workspace form stays until a pinned actionlint accepts `$/`.

A repository test checks that each acceptance still names the line it was written for.

## No caching

No job caches NuGet packages, Docker layers or Grype's database.
- **The release** builds from nothing, as U6-NFR-04 requires.
- **CI's jobs** download what they need. That is about a minute per job: well inside U6-NFR-13's budgets, with no
  cache for an attacker to poison.

If the first runs show the pull-request checks near 20 minutes, a NuGet cache for the test job is the first thing to
add.

## Scripts

| Script | Language | Does | Called by |
|---|---|---|---|
| `scripts/ci/run-tool.sh` | bash | Runs a pinned tool image (above) | The lint, image and publish jobs; a developer |
| `scripts/ci/build-image.sh` | bash, jq | Creates the builder. With `load`, the build into Docker plus the OCI archive. With `push`, the export by digest and the configuration check | The image and publish jobs |
| `scripts/ci/smoke-test.cs` | C#, a .NET 10 file-based app | Both phases, through compose | The image and publish jobs; a developer |
| `scripts/ci/scan-image.sh` | bash | The SBOM and Grype's scan | The image and publish jobs; a developer |
| `scripts/ci/promote.sh` | bash, jq, `gh` | `verify` and `tag` (steps 6 and 7) | The publish job |
| `scripts/ci/check-release.sh` | bash | DR-20 and DR-21 | The version job |
| `scripts/ci/coverage.cs` | C#, a file-based app | The coverage checker | The test job; a developer |
| `scripts/ci/summary.cs` | C#, a file-based app | The run summary, from TRX, Cobertura, Grype's JSON, the OCI archive's layer sizes and the build metadata | The test, image and publish jobs |

- **bash** is for the steps that only drive Docker or the registry, on the Linux runner, where jq and `gh` are already
  installed.
- **C#** is for what runs on both runners or on a developer's Windows machine, and for what parses XML, JSON or tar
  files. `dotnet run <file>.cs` needs only the SDK that `global.json` names.
- **`Directory.Build.props` applies to them**, so warnings are errors there too.
- **They turn native AOT off** with `#:property PublishAot=false`. .NET 10 makes a file-based app native-AOT by
  default, and AOT's analyzers then fail the build on reflection-based JSON (IL2026, IL3050, seen at Code Generation).
  These scripts are only ever run.

## Repository tests (`tests/Airlock.Delivery.Tests`)

A new xUnit v3 project on Microsoft Testing Platform, in `Airlock.slnx`.
- It reads the delivery files from the repository root, which it finds by walking up to `Airlock.slnx`.
- It needs no Docker and no network, so it runs on both runners.
- It reads YAML with YamlDotNet 18.1.0 (MIT), a dependency of this project only.
- It has no production code, so it has no coverage target.
- It references `src/Airlock.Web` without its assembly (`ReferenceOutputAssembly="false"`), so restoring it always
  writes the `obj/project.assets.json` that `NoticesTests` reads, and carries the coverage extension like every test
  project, so that `dotnet test --coverage` runs over the whole solution.

| Class | Checks | Rules |
|---|---|---|
| `PinTests` | Every `uses:` is a 40-character SHA with a `# v` comment; every `FROM` in both Dockerfiles carries a digest; the install scripts carry a SHA-256 for each archive | DR-27, U6-NFR-01 |
| `SyftVersionTests` | One Syft version in the scripts, `Dockerfile` and README | DR-04 |
| `SdkTests` | `global.json` is 10.0.403 with `latestPatch`; the SDK image is in that band and no older | Q-U6-I1 |
| `WorkflowTests` | <ul><li>Permissions at the top and per job.</li><li>`runs-on` labels; time limits; concurrency names and `cancel-in-progress`.</li><li>`persist-credentials: false` on every checkout; no `pull_request_target` or `workflow_run`; no `${{` inside a `run:`.</li><li>The release's guards, triggers and step order; `ci.yml`'s input.</li></ul> | DR-11, DR-19, DR-22, DR-23, U6-NFR-03, U6-NFR-04, U6-NFR-07 |
| `DependabotTests` | The table above | DR-16, U6-NFR-02 |
| `ComposeTests` | <ul><li>The hardening keys; the port on 127.0.0.1.</li><li>The volumes, and the staging root and keys under the volume's mount; `SCAN_UPLOAD_PATH` required.</li><li>`.env.example` names every variable; the smoke override keeps to its three keys.</li></ul> | DR-07 to DR-09, DR-29, U6-NFR-09, U6-NFR-10, U6-NFR-12 |
| `DockerfileTests` | <ul><li>The stages; no `RUN` in the runtime stage; exec-form `ENTRYPOINT` and `HEALTHCHECK`; `USER 1654`; the environment.</li><li>No `ARG` or `ENV` naming a key; `.dockerignore`'s allowlist.</li></ul> | DR-02, DR-03, DR-06, U6-NFR-05, U6-NFR-08 |
| `GrypePolicyTests` | The gate's two settings; each acceptance names a vulnerability and a package, and has a review date still to come (UTC) | DR-30, U6-NFR-06 |
| `ZizmorConfigTests` | The hash-pin policy and the allow-list; no inline `zizmor: ignore` comment in any workflow | DR-28, U6-NFR-04 |
| `NoticesTests` | Every package that `src/Airlock.Web` restores, transitive ones included (from `obj/project.assets.json`), is in `THIRD-PARTY-NOTICES.md` | DR-05 |
| `ReadmeTests` | MIT; QuestPDF's licence and its threshold; the NVD notice; every expected warning explained | DR-18, U6-NFR-16 |
| `ToolTests` | Each CI tool's version is written in one place | U6-NFR-15 |

## The coverage checker (`scripts/ci/coverage.cs`)

It measures each project from its own test project's Cobertura report, as the units measured it, and fails the job
below a target. Generated files are left out: those under `obj/`, which include the Razor components' generated code.
`tests/Directory.Build.props` gives each test project's `dotnet test` run the results folder `TestResults/<project>/`,
which is how the checker knows whose report is whose. It writes `coverage.json` there for the run summary. It reads
each line's branch counts from the parentheses of `condition-coverage`, such as `66.67% (4/6)`. The units' own script
read only whole percentages, so it skipped some lines' branches. Their branch figures were therefore a little high:
Airlock.Web 86.6 % here against U5's 88.1 %, Airlock.Infrastructure 85.5 % against U3's 87.2 %, both still above
their targets.

| Project | Report | Lines | Branches |
|---|---|---|---|
| `Airlock.Domain` | `Airlock.Domain.Tests` | 95 % | 90 % |
| `Airlock.Application` | `Airlock.Application.Tests` | 90 % | 85 % |
| `Airlock.Infrastructure` | `Airlock.Infrastructure.Tests`, with Syft's integration tests | 85 % | 80 % |
| `Airlock.Web` | `Airlock.Web.Tests` | 90 % | 85 % |

## Downloads for Code Generation

Code Generation's plan lists these, and none is fetched before you approve it:
- **SDK 10.0.403 for Windows x64**, which you install: 205.4 MiB, from `builds.dotnet.microsoft.com`.
- **Images** for the local build, the smoke test and the release rehearsal (linux/amd64, compressed), about 620 MiB in
  all:

  | Image | Size |
  |---|---|
  | SDK | 341.9 MiB |
  | Runtime | 69.2 MiB |
  | Syft | 30.0 MiB |
  | Grype | 31.2 MiB |
  | zizmor | 16.9 MiB |
  | actionlint | 24.1 MiB |
  | BuildKit | 106.2 MiB |

  Plus a small local registry image for the rehearsal.
- **Grype's database**, 187.3 MiB, which Grype downloads itself.
- **YamlDotNet 18.1.0**, 777 KiB, from nuget.org.
- **From Syft v1.55.0's GitHub release and repository**, both small text files: its checksums file and its `LICENSE`.

## Checked at Code Generation

What was checked, and what it found (details in `../code/code-generation-summary.md`):
- **Every pin**: each digest and SHA read again on 2026-10-30, unchanged.
- **The release's mechanics**, rehearsed against a local registry: the second export kept the configuration digest,
  and the tags named D without a new manifest.
- **Compose's fallback**: an unpublished tag is built from this copy; a published image is pulled.
- **The image**: 112.7 MiB to download, in 9 layers.
  - At start, only `XmlKeyManager[35]` warns.
  - Syft scans `/uploads` under the read-only root, ending Incomplete as designed.
  - The licence files are those in the table above.
  - `/app` is writable by root only, after the build stage set its modes.
- **Dependabot's names** for Microsoft's images: an image is named by its repository without the registry, `dotnet/sdk`,
  so `*dotnet/*` matches.
- **The tools' entry points**: `/usr/bin/zizmor`, `/grype` and `/syft`; shellcheck at `/usr/local/bin/shellcheck` in
  actionlint's image.
- **The probe's start-up cost**: 369 ms per check, and the container was healthy about 3 seconds after starting.
