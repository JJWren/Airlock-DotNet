# U6 Delivery — Tech Stack Decisions

**Stage**: CONSTRUCTION — U6 Delivery — NFR Requirements (Standard depth)

U6 adds nothing to Airlock's code dependencies. It chooses the images the `Dockerfile` builds from, the tools CI runs
and the GitHub features it uses. The versions below were checked on 2026-10-28. Infrastructure Design pins the exact
digests and commit SHAs, and Code Generation checks them again. As before, the Code Generation plan lists every download
with its size, and nothing is fetched before you approve it.

## The image

| Item | Decision |
|---|---|
| Runtime base | `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra` (Q-U6-N1), pinned by digest (Q-U6-N4); .NET 10.0.14 on 2026-10-28, a 69.2 MiB download for linux/amd64. It runs Ubuntu 24.04, the distribution of .NET 10's default `10.0` tag and of the `ubuntu-24.04` runner. Ubuntu 26.04 variants exist, but are not needed: Ubuntu 24.04's standard support runs to 2029, past .NET 10's end of support in November 2028 |
| Build stage | `mcr.microsoft.com/dotnet/sdk:10.0.403-noble`, by digest: the SDK `global.json` names, 10.0.403 in the 4xx band (Q-U6-I1). It publishes `src/Airlock.Web` in Release for `linux-x64`, framework-dependent, which keeps only linux-x64's native code: QuestPDF's 11 MB of the 84 MB it ships for six platforms |
| Syft | Anchore's image at v1.55.0, by digest (DR-02, DR-04). It comes from `ghcr.io/anchore/syft`, and its binary is copied to `bin/` next to the application. Its image carries no licence file, so Syft's `LICENSE` is committed under `licenses/syft/` and copied into the image (Infrastructure Design) |
| Health probe | A small program that ships with Airlock and asks the health endpoint. `HEALTHCHECK` runs it as an executable with arguments, since the image has no shell (Q-U6-N1). It is a mode of `Airlock.Web`, `--health-probe`, with a 3-second timeout (Infrastructure Design) |
| Native code | QuestPDF's `libQuestPdfSkia.so` needs only `libstdc++`, `libm`, `libgcc_s`, `libpthread` and `libc`, which the chiseled image carries. `libqpdf.so` adds zlib, but only for QuestPDF's document operations (merging, encryption), which Airlock does not call. Neither needs a font library. The smoke test's PDF proves the drawing library loads (U6-NFR-12) |
| Build tool | BuildKit through `docker buildx`, preinstalled on GitHub's Linux runners; linux/amd64 only (DR-22); no Docker action (U6-NFR-03). CI and the release build in a BuildKit container pinned by digest, which can push an image by its digest alone (Infrastructure Design) |

## Compose

| Item | Decision |
|---|---|
| Format | The Compose Specification, run with `docker compose` (Compose v2) |
| Hardening | `read_only: true`; a `tmpfs` at `/tmp`; the data-protection keys' folder on the volume, beside the staging root (NFR Design); `cap_drop: [ALL]`; `security_opt: [no-new-privileges:true]`; `init: true` (U6-NFR-09). The volume is `airlock-data` on `/var/lib/airlock`, and `/tmp` is 64 MiB (Infrastructure Design) |

## CI and releases

| Item | Decision |
|---|---|
| Runners | GitHub-hosted `ubuntu-24.04` (what `ubuntu-latest` names today) and `windows-2025` (today's `windows-latest`), named rather than `-latest` (U6-NFR-03). `ubuntu-26.04` exists, but is not needed |
| Actions | GitHub's own only, each pinned to a commit SHA: `actions/checkout`, `actions/setup-dotnet` (with `global-json-file`), `actions/upload-artifact`, `actions/attest@v4` and `github/codeql-action`. Since v4, `actions/attest-build-provenance` and `actions/attest-sbom` only wrap `actions/attest`, and `attest-sbom` is being deprecated, so Airlock uses `actions/attest` directly |
| Registry | GHCR, `ghcr.io/jjwren/airlock-dotnet`. The publishing job signs in with `GITHUB_TOKEN` through `docker login --password-stdin` |
| SBOM | Syft from the image the `Dockerfile` pins. In one run it writes SPDX JSON for the attestation — one of the two formats `actions/attest` accepts, and well below its 16 MB limit — and its own JSON for Grype (Infrastructure Design) |
| Image scan | Grype (Anchore, Apache-2.0), 0.121.0 (2026-10-19), run from its image pinned by digest (Infrastructure Design). Its vulnerability database comes from Anchore, with no account or key. It reads the SBOM rather than the image. `.grype.yaml` holds the policy — `only-fixed`, `fail-on-severity: high` — and the accepted findings (Q-U6-N2; NFR Design) |
| Attestations | `actions/attest@v4`: build provenance, and an SBOM attestation from `sbom-path`, both for `subject-name` `ghcr.io/jjwren/airlock-dotnet` and the pushed `subject-digest`, with `push-to-registry: true`. A public repository signs with Sigstore's public-good instance. Storage records are off (`create-storage-record: false`), so the job needs only `id-token: write` and `attestations: write` beside `packages: write`, as Q-U6-N3 said; with them on, it would also need `artifact-metadata: write`. The publishing job verifies both with `gh attestation verify`, GitHub's own CLI on the runner, before it tags (NFR Design) |
| Workflow checks | actionlint 1.7.12 (2026-03-30, MIT) and zizmor 1.31.1 (2026-10-15, MIT), each run from its image pinned by digest (Infrastructure Design). Both run in the lint job (U6-NFR-04), and zizmor's `.github/zizmor.yml` requires a commit SHA for every action and allows only GitHub's own actions. zizmor runs online, with the job's read-only token (NFR Design) |
| Coverage | Microsoft.Testing.Extensions.CodeCoverage's Cobertura reports, read against the targets in `domain-entities.md` by a small checker committed under `scripts/`: `scripts/ci/coverage.cs`, a .NET 10 file-based app (Infrastructure Design) |
| CodeQL | `github/codeql-action` for `csharp` and `javascript-typescript` (DR-15); C# in build mode `manual`, JavaScript in `none` (Infrastructure Design) |
| Dependabot | `nuget`, `docker`, `github-actions` and `dotnet-sdk` (supported since 2024-11), weekly and grouped. Version updates wait out a 7-day `cooldown` (since 2025-07), which never applies to security updates; Microsoft's .NET images and the SDK are excluded from it (U6-NFR-02). Major updates of the SDK, the .NET images and the `Microsoft.Extensions.*` and `Microsoft.AspNetCore.*` packages are ignored, keeping Airlock on .NET 10 LTS (NFR Design) |

## Repository tests

| Item | Decision |
|---|---|
| Where | `tests/Airlock.Delivery.Tests`, a new project in the suite (Infrastructure Design). The tests read the delivery files, run offline and need no Docker, so they run on both runners |
| What | DR-04, DR-05, DR-09 and DR-18 from the functional design, and U6-NFR-01 to U6-NFR-03, U6-NFR-05 to U6-NFR-07, U6-NFR-09, U6-NFR-10 and U6-NFR-15 |
| YAML | YamlDotNet 18.1.0 (MIT), in that project only (Infrastructure Design); a download to approve at Code Generation |

## Licences

Grype (Apache-2.0), actionlint (MIT) and zizmor (MIT) run in CI only, and nothing of them ships in the image. Syft
(Apache-2.0) ships, as DR-05 says. The base image brings the .NET runtime (MIT) and Ubuntu's libraries, each under its
own licence; U6-NFR-17 keeps their licence files in the image.

## Versions checked (2026-10-28)

- **.NET images**: 10.0.14, from dotnet/dotnet-docker's `README.aspnet.md`. Download sizes come from the images'
  manifests on mcr.microsoft.com: 93.1 MiB for `10.0`, 69.2 MiB for `10.0-noble-chiseled-extra`, 68.3 MiB for
  `10.0-alpine-extra`.
  Airlock's Microsoft packages are at 10.0.13. A newer runtime runs them, and Dependabot's first NuGet proposal will
  bring them level.
- **Grype** 0.121.0, published 2026-10-19, with `grype_0.121.0_checksums.txt`.
- **actionlint** 1.7.12, published 2026-03-30, with `actionlint_1.7.12_checksums.txt`.
- **zizmor** 1.31.1, published 2026-10-15, with binaries for x86_64 Linux and Windows.
- **actions/attest** v4. Its permissions are `id-token: write` and `attestations: write`, plus `artifact-metadata: write`
  for storage records. It accepts SPDX or CycloneDX JSON up to 16 MB.
- **Runners**: `ubuntu-24.04` (= `ubuntu-latest`), `windows-2025` (= `windows-latest`); `ubuntu-26.04` is also
  available.
- **Dependabot**: `dotnet-sdk` (2024-11); `cooldown` (2025-07), from 1 to 90 days, never for security updates.
- **Artifact attestations**: in public repositories on every current GitHub plan; private repositories need GitHub
  Enterprise Cloud.
- **Infrastructure Design** read again, on 2026-10-29, every version, digest and SHA it pins, and chose the tools'
  images over their release archives (`../infrastructure-design/infrastructure-design.md`).
