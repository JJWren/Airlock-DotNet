# U6 Delivery — Infrastructure Design Plan

**Stage**: CONSTRUCTION — U6 Delivery — Infrastructure Design (Standard depth)
**Started**: 2026-10-29T22:17:19Z

## Inputs

- U6's functional design, NFR requirements and NFR design: the rules, the targets, the patterns, and what each left
  to this stage
- U4's infrastructure design and deployment architecture; `global.json`, `Directory.Build.props`, `Program.cs`,
  `AirlockHost.cs`, `Airlock.Web.csproj` and U3's install scripts
- Facts checked on 2026-10-29. Registry manifests, release lists and release metadata were read; no image was
  pulled and nothing was installed.
  - **The runtime image.**
    - `mcr.microsoft.com/dotnet/aspnet:10.0.14-noble-chiseled-extra` is the same image as
      `10.0-noble-chiseled-extra`.
    - dotnet-docker's Dockerfile for it installs these Ubuntu 24.04 slices: base-files, ca-certificates, libc6,
      libgcc-s1, libicu74, libssl3t64, libstdc++6, tzdata and tzdata-legacy.
    - It writes a dpkg status file for scanners.
    - It creates `/home/app` owned by UID 1654, and sets `ASPNETCORE_HTTP_PORTS=8080` and `USER $APP_UID`.
  - **The SDK images.**
    - `sdk:10.0-noble` carries SDK 10.0.403 and runtime 10.0.14; it is the same image as `sdk:10.0.403-noble`.
    - The 3xx tags stop at `10.0.302-noble`: there is no 10.0.303 image.
  - **.NET 10's release metadata.**
    - SDK 10.0.303 shipped with 10.0.11 in August and is the last 3xx SDK.
    - The September and October releases, both security releases, shipped 10.0.401 to 10.0.403, plus 1xx SDKs.
    - 10.0.400 needs Visual Studio 18.9; 10.0.403 pairs with 18.11.2.
  - **The runners.**
    - `ubuntu-24.04` (image 20261026) has Docker 28.0.4, Buildx 0.38.1, Compose 2.38.2, `gh` 2.103.2, jq 1.7 and
      skopeo 1.13.3, and SDKs including 10.0.303 and 10.0.403.
    - `windows-2025` (image 20261021) has SDKs including 10.0.303 and 10.0.402, Docker 29.7.2 and bash 5.3.
    - With `global.json` as it is (10.0.303, `latestFeature`), Linux CI would build with 10.0.403 and Windows CI with
      10.0.402.
  - **The tool images.**
    - `ghcr.io/anchore/syft:v1.55.0` (the same digest as on Docker Hub), `ghcr.io/anchore/grype:v0.121.0` and
      `ghcr.io/zizmorcore/zizmor:1.31.1`.
    - actionlint (`rhysd/actionlint`, which carries shellcheck and pyflakes) and BuildKit (`moby/buildkit`) are on
      Docker Hub only.
    - Syft's and Grype's images are built `FROM scratch`: the binary, CA certificates and `/tmp`, with no licence
      file.
  - **The actions**, each a full release at least 7 days old: `actions/checkout` v7.0.1, `actions/setup-dotnet`
    v6.0.0, `actions/upload-artifact` v7.0.2, `actions/attest` v4.2.2 and `github/codeql-action` v4.39.1. Their
    commit SHAs were read from each repository's tags.
  - **`actions/attest`** makes build provenance by default, and an SBOM attestation with `sbom-path`.
    `create-storage-record` defaults to true, so Airlock must set it to false.
  - **`docker buildx imagetools create --prefer-index=false`** copies a single source instead of wrapping it in an
    index. The documentation does not promise the same digest, so the release checks it.
  - **zizmor**:
    - `GH_TOKEN` or `GITHUB_TOKEN` turns on its online audits;
    - it collects Dependabot's configuration with the workflows;
    - an accepted finding is `rules.<audit>.ignore: [file:line]`, and inline `# zizmor: ignore[…]` comments also
      exist.
  - **YamlDotNet** is at 18.1.0 on NuGet.

## Steps

- [x] Check the facts (above)
- [x] Resolve Q-U6-I1 (A, 2026-10-29T22:40:40Z)
- [x] `construction/u6-delivery/infrastructure-design/infrastructure-design.md` — the image, compose, the workflows
      and the release's mechanics, the pins, the settings, the scripts and the tests
- [x] `construction/u6-delivery/infrastructure-design/deployment-architecture.md` — where everything runs, the build
      and release pipeline and the running container (validated Mermaid, with text alternatives)
- [x] Bring the earlier documents in line: DR-01, DR-02, DR-10 and DR-13; the domain entities; the CI flow and the
      list of what was left; the tech-stack decisions; the NFR requirements' and NFR design's hand-over lists; U4's
      configuration table, health wiring and data protection; U1's SDK row; U6 in `unit-of-work.md`; NFR-01 and the
      traceability table
- [x] Present for approval (2026-10-29T22:55:39Z)

## Question

### Q-U6-I1 — Which .NET SDK feature band should Airlock build with?

`global.json` pins 10.0.303 with `rollForward: latestFeature`, which lets any newer 10.0 feature band build Airlock.
Microsoft's SDK images carry only the newest band, 10.0.403, and the runners hold both bands. So today your machine
would build with 10.0.303, Linux CI and the image with 10.0.403, and Windows CI with 10.0.402. The 3xx band also gets
no more patches: its last SDK, 10.0.303, shipped in August, and the September and October security releases shipped
4xx and 1xx SDKs only. 4xx is .NET 10's last feature band, patched until .NET 10's end of support in November 2028.

A) **Move to 10.0.4xx** (recommended). `global.json` becomes 10.0.403 with `rollForward: latestPatch`. The image
builds from `sdk:10.0.403-noble`, and CI installs 10.0.403 on both runners, so every build uses one SDK, and Dependabot
keeps all of them current. You install SDK 10.0.403 before Code Generation. Building inside Visual Studio then needs
18.9 or later; the command line works either way.

B) **Stay on 10.0.3xx.** `global.json` becomes 10.0.300 with `latestPatch`. Your machine and CI use 10.0.303, and the
image uses `sdk:10.0.302-noble`, the last 3xx image. Nothing in the band will be patched again, and Dependabot's 4xx
proposals for the SDK and the image would have to be ignored.

C) **Keep `global.json` as it is.** No change on your machine. CI and the image build with whichever newer band they
hold: 10.0.403 on Linux and in the image, 10.0.402 on Windows. A difference between bands, such as a new analyzer
warning, which fails the build because warnings are errors, would first appear in CI.

D) Other (describe)

[Answer]: A

## Choices made in this design (object at the gate if any is wrong)

1. **The CI tools come as images**, pinned by digest in `.github/ci-tools/Dockerfile`, which is never built. Grype,
   actionlint, zizmor and BuildKit are pinned there; Syft's reference is read from `Dockerfile`.
   - Dependabot keeps them current after its 7-day cooldown. Release archives with committed hashes would need
     updating by hand.
   - actionlint and BuildKit exist only on Docker Hub; Syft, Grype and zizmor come from `ghcr.io`.
2. **The .NET images are pinned by version tag**: `10.0.403-noble` and `10.0.14-noble-chiseled-extra`, not
   `10.0-noble`. Dependabot's proposals then say which version they bring.
3. **The build runs in a BuildKit container pinned by digest**: BuildKit's documented way to push by digest alone. One
   build, with `SOURCE_DATE_EPOCH` from the commit, is exported twice:
   - into Docker, for the tests;
   - as an OCI archive, for the SBOM and the size.

   After the tests, the same cache is exported again to GHCR by digest. The release stops unless that export's
   configuration digest is the tested one's.
4. **Tags are added with `docker buildx imagetools create --prefer-index=false`**, and each tag must name D.
   - Code Generation rehearses this against a local registry.
   - If the rehearsal shows a new manifest being written, the tags are instead written as D's own manifest bytes
     through the registry's API.
5. **The health probe is a mode of `Airlock.Web`**: `/usr/bin/dotnet /app/Airlock.Web.dll --health-probe`, with a
   3-second timeout. It runs every 30 seconds, and every 2 seconds during a 30-second start period, with 3 retries.
6. **The keys' folder is a setting**, `Airlock:DataProtection:KeysPath`, which the image points at the volume.
7. **The paths**: the volume `airlock-data` on `/var/lib/airlock`, holding `staging/` and `keys/`, and a 64 MiB
   `noexec` tmpfs on `/tmp`.
8. **`.dockerignore` is an allowlist**, and it includes `.editorconfig`, which the build's style checks read.
9. **One Syft run writes two SBOMs**: Syft's own JSON for Grype, which keeps the distribution's details, and SPDX JSON
   for the attestation.
10. **No caching in any job.**
11. **Scripts.**
    - bash for the steps that only drive Docker or the registry, on the Linux runner.
    - .NET 10 file-based C# apps for what also runs on Windows or parses files: the smoke test, the coverage checker
      and the run summary.
12. **The repository tests** go in a new project, `tests/Airlock.Delivery.Tests`, which reads YAML with YamlDotNet
    18.1.0 (MIT, tests only, a download to approve).
13. **CodeQL**:
    - builds `src/Airlock.Web` for C# (`manual`), so the Razor components' generated code is analysed;
    - reads JavaScript as it is (`none`);
    - runs every Monday at 06:17 UTC.
14. **The smoke test** uses port 18080 and the compose project `airlock-smoke`, so a developer's own Airlock is not
    disturbed. NVD is unreachable at `https://127.0.0.1:9/rest/json/cves/2.0`.
15. **Dependabot.**
    - Syft's image is left out of the Docker group, so its update comes alone.
    - The SDK image's proposal can merge before `global.json`'s.
16. **Time limits** start at about twice each job's expected length:

    | Job | Limit |
    |---|---|
    | lint | 10 min |
    | test | 30 min |
    | image | 20 min |
    | CodeQL | 30 min |
    | version | 5 min |
    | publish | 30 min |
