# U6 Delivery — NFR Requirements Plan

**Stage**: CONSTRUCTION — U6 Delivery — NFR Requirements (Standard depth)
**Started**: 2026-10-28T21:53:29Z

## Inputs

- U6 functional design (DR-01 to DR-26, the files, the image's parts, the compose service, the workflows) and what it
  left to this stage: pinning and attestations, the container's runtime hardening, budgets, scanning the image
- NFR-01, NFR-03, NFR-06, NFR-07, NFR-09, NFR-10 and NFR-11; stories E13 to E15
- The NFRs U6 inherits: U3's (Syft's environment and temporary files, IR-01; Syft's status cached, IR-05; NVD's
  retries), U4's (U4-NFR-09, one Airlock per staging root; the deployment architecture's free-space rule and the
  10-second grace period), U5's (U5-NFR-09, time-zone data in the image)
- Facts checked on 2026-10-28:
  - **.NET 10 images** (dotnet/dotnet-docker, `README.aspnet.md`): `10.0` is Ubuntu 24.04 ("noble"), at 10.0.14.
    Each of Ubuntu 24.04 and Ubuntu 26.04 ("resolute") has plain, `chiseled`, `chiseled-extra` and composite variants;
    there are also Alpine 3.24 (with an `-extra` variant) and Azure Linux 3.0 (with `distroless` variants). The
    `-extra` variants add ICU and time-zone data to the chiseled and Alpine images.
  - **Download sizes**, linux/amd64, from the images' manifests on mcr.microsoft.com:

    | `mcr.microsoft.com/dotnet/aspnet:` | Download |
    |---|---|
    | `10.0` (Ubuntu 24.04) | 93.1 MiB |
    | `10.0-noble-chiseled-extra` | 69.2 MiB |
    | `10.0-noble-chiseled` (no ICU, no time-zone data) | 54.0 MiB |
    | `10.0-alpine-extra` | 68.3 MiB |
    | `10.0-resolute-chiseled-extra` | 71.3 MiB |

  - **QuestPDF 2026.9.1's Linux native code** (ELF dynamic sections read from the NuGet cache):
    `libQuestPdfSkia.so`, which draws the PDF, needs only `libstdc++`, `libm`, `libgcc_s`, `libpthread` and `libc`
    (glibc 2.27 or later). `libqpdf.so` adds zlib, and serves QuestPDF's document operations (merging, encryption), which
    Airlock does not call. Neither needs a font library such as fontconfig. The musl builds need the same libraries.
  - **Sizes**: Syft v1.55.0's binary is about 90 MB (the Windows build measures 89,968,640 bytes). Airlock's build
    output is 101 MB, of which 84 MB is native code for six platforms and 11 MB for linux-x64, so a linux-x64 publish
    is about 28 MB.
  - **Syft in Airlock**: every run gets `TMPDIR`, `TMP` and `TEMP` pointing at its own folder in the scan's staging
    folder (U3 IR-01), so Syft unpacks archives and images on the staging root. Syft's status is checked once and then
    cached (IR-05), so a health check starts no process.
  - **NVD unreachable**: five attempts, waiting 2 seconds and doubling, ±20 % (U3-NFR-02), so a package becomes
    Unverified about 30 seconds after its first lookup fails. `Nvd:BaseUrl` must be an absolute https URL, which a
    closed port on 127.0.0.1 satisfies.
  - **NuGet's audit**: on by default, it reports packages with published advisories during restore, and
    `TreatWarningsAsErrors` turns the report into a failed build.
  - **Dependabot**: the `dotnet-sdk` ecosystem updates the SDK in `global.json` (since 2024-11). `cooldown` (since
    2025-07) delays version updates by 1 to 90 days, never security updates. Dependabot raises no security alerts for
    the .NET SDK or for container images, so their updates are version updates.
  - **Artifact attestations** (GitHub Docs): available in public repositories on every current plan; a private or
    internal repository needs GitHub Enterprise Cloud. The GitHub CLI verifies them (`gh attestation verify`).
  - **Tag hijacking**: on 2026-03-19, attackers with stolen credentials force-pushed 75 of the 76 version tags of
    `aquasecurity/trivy-action` to code that stole CI secrets (CVE-2026-33634); workflows that named a commit SHA kept
    running the real action.

## Steps

- [x] Resolve the open questions below (Q-U6-N1 to Q-U6-N4 = A, 2026-10-28T22:40:34Z)
- [x] `construction/u6-delivery/nfr-requirements/nfr-requirements.md` — U6-NFR-01 onwards, each with its target, how
      it is checked and its trace
- [x] `construction/u6-delivery/nfr-requirements/tech-stack-decisions.md` — the images, the build, the probe, the
      runners, the actions, the scanners and linters, Dependabot; the versions as checked, to be checked again at
      Infrastructure Design and Code Generation
- [x] Bring the functional design and the requirements in line where a decision needs it (DR-13's smoke test; NFR-11)
      — DR-02 to DR-04, DR-07, DR-10 to DR-14, DR-16, DR-22 and DR-23 amended and DR-27 to DR-31 added; the CI and
      release flows redrawn and validated with Mermaid 11.4.1; NFR-03, NFR-11 and the traceability table; stories E14
      and E15; U6 in `unit-of-work.md`; U5's note on chiseled images
- [x] Present for approval (2026-10-28T22:51:57Z)

## Questions

### Q-U6-N1 — The runtime image
The functional design took the full Ubuntu image, following U5's note that the image needs ICU and time-zone data,
which the plain chiseled image lacks. Microsoft's `chiseled-extra` variant carries both, so that reason no longer rules
chiseled out. QuestPDF's drawing library needs only the C and C++ runtime libraries, which every option below carries.

A) Chiseled Ubuntu 24.04 with ICU and time-zone data (`aspnet:10.0-noble-chiseled-extra`, 69.2 MiB): only the
   libraries .NET needs, no shell, no package manager, non-root by default, so fewer packages to patch and to scan.
   With no shell for a one-liner, the health check runs a small probe that ships with Airlock, and you debug from
   outside (`docker compose logs`, the API) rather than with `docker compose exec … sh` (Recommended)
B) Full Ubuntu 24.04 (`aspnet:10.0`, the .NET 10 default, 93.1 MiB): bash and the usual tools, so the health check is a
   shell one-liner and you can open a shell in the container; the whole Ubuntu base comes along, with more packages to
   patch and more for the image scan to report
C) Alpine with ICU and time-zone data (`aspnet:10.0-alpine-extra`, 68.3 MiB): small and with a shell; built on musl
   rather than glibc, which .NET and QuestPDF support but which is less common for .NET in production
D) Other

[Answer]: A

### Q-U6-N2 — Scanning the image for vulnerabilities
Two layers catch known vulnerabilities today: NuGet's audit fails the build when a package Airlock uses has a
published advisory, and Dependabot proposes updates. Neither looks at the image as a whole — its Ubuntu libraries, the
.NET runtime, or Syft's own Go libraries. Grype, made by Anchore as Syft is, scans an image without an account or key,
and can read the SBOM Syft makes of it.

A) Grype scans the image in CI and again before every publish. The run fails on a High or Critical vulnerability that
   has a fixed version, and the full report is kept with the run; an accepted finding goes in `.grype.yaml` with its
   reason and a date to review it. A fix in Go itself can fail CI until Syft is updated or the finding is accepted
   (Recommended)
B) As A, and also a weekly scan of the published `latest` image, which fails, so GitHub e-mails you, when a fixable
   High or Critical vulnerability appears after a release
C) Grype reports, but never fails the run
D) No image scan
E) Other

[Answer]: A

### Q-U6-N3 — Proof of origin for published images
Q-U6-2 publishes the image to GHCR. GitHub's artifact attestations can sign, for every published version, which
workflow and commit built it (build provenance) and what it contains (an SBOM, which Syft can make). Anyone can then
check an image with `gh attestation verify`. They are free for a public repository; a private one needs GitHub
Enterprise Cloud. The publishing job would gain `id-token: write` and `attestations: write`.

A) Signed build provenance and a signed SBOM made by Syft, for every published version; the README shows how to
   verify both (Recommended)
B) Signed build provenance only
C) No attestations: the OCI labels only
D) Other

[Answer]: A

### Q-U6-N4 — Pinning what CI runs
A version tag such as `@v4` or `10.0` can be moved to other code, as the trivy-action tags were in March 2026; a commit
SHA or an image digest cannot. GitHub's hardening guide recommends full-length commit SHAs for actions.

A) Every action pinned to a full commit SHA, with its version as a comment, and every image to a digest, with its tag.
   Dependabot proposes updates to both, and waits 7 days after a release before proposing it, except for security
   updates and Microsoft's .NET images, whose updates carry security fixes that Dependabot does not flag. CI rejects an
   unpinned reference (Recommended)
B) The same pins, with no waiting period
C) Actions pinned to commit SHAs; images by tag, so a rebuild picks up .NET patches without a pull request
D) Version tags everywhere (`@v4`, `10.0`)
E) Other

[Answer]: A

## Targets taken by default (object at the gate if any is wrong)

1. **Workflow security.** Each job gets only the permissions it needs; checkout keeps no credentials; no
   `pull_request_target`; values from outside, such as branch and tag names, reach scripts through environment
   variables, never pasted into them; the release runs only in `JJWren/Airlock-DotNet`; every job has a time limit; the
   release restores no cache. actionlint and zizmor check the workflows in a lint job in CI.
2. **GitHub's own actions only** (`actions/*`, `github/*`). Docker, Syft, Grype, actionlint and zizmor run as
   command-line tools at pinned versions, each download compared with its published checksum, so no third-party action
   runs in CI.
3. **Secrets.** The NVD key is never in the image, the build context, a build argument, a label, a log or CI. CI holds
   no key: the smoke test's second phase uses a fixed stand-in value against an NVD that cannot be reached.
4. **Runtime hardening** in compose: the non-root user; a read-only root filesystem, with writable mounts only for the
   staging volume (Syft's temporary files included), `/tmp` and the data-protection keys; every Linux capability
   dropped; `no-new-privileges`; an init process to forward signals and reap child processes; no Docker socket, no
   privileged mode, the port on 127.0.0.1 (DR-07).
5. **Disk.** Uploads and Syft's temporary files land on the staging volume, never in memory (`tmpfs`) or the
   container's own layer; the README asks for about three times the largest upload in free space (U4).
6. **Health and restarts.** The container reports healthy within 30 seconds of starting; a health check starts no Syft
   process and logs nothing at Information; `restart: unless-stopped`; a stop during a scan cancels it within the
   10-second grace period, and the next start sweeps what was left (U3).
7. **A real scan in CI's smoke test** (amends DR-13). After the keyless checks DR-13 lists, the container restarts with
   the stand-in key and NVD pointed at a closed local port, and scans a small `requirements.txt` from `/uploads`. The
   scan must end Incomplete with every package Unverified — ADR-0002's fail-closed path — and its PDF must download.
   This proves Syft, the staging volume, the hardening and QuestPDF's native code work in the image, without NVD or
   a secret, and adds about a minute.
8. **Budgets.** The image downloads in at most 150 MiB (linux/amd64; 175 MiB if Q-U6-N1 picks the full image),
   measured at Code Generation. A pull request's checks finish within 20 minutes; a release within 30.
9. **Portability.** The image is linux/amd64 (DR-22); Docker Engine or Docker Desktop with Compose v2. Running from
   source works on Windows, Linux and macOS, with CI covering Windows and Linux. CI names its runner images
   (`ubuntu-24.04`, `windows-2025`) rather than `-latest`, so a runner change arrives as a reviewed edit.
10. **Maintainability.** Dependabot also updates the .NET SDK in `global.json`. Each tool's version lives in one place,
    or a repository test checks its other places (DR-04's rule, extended to Grype, actionlint and zizmor).
    `release.yml` runs `ci.yml` rather than repeating it.
11. **Observability.** The container logs to standard output in Airlock's single-line UTC format, read with
    `docker compose logs`. A clean start logs no warning the README does not explain. Each CI run writes a summary: the
    tests, each project's coverage against its target, the image's size and the scan's counts. Run artifacts are kept
    14 days.
12. **Licensing.** As DR-05 and DR-18; the base image's own licence files stay in the image.
13. **Documentation.** The quick start takes at most three commands. The README also explains `docker compose pull`,
    to return to the release after `--build`; the hardening and the writable paths; and, where the answers call for
    them, verifying the attestations and debugging without a shell.
