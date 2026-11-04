# U6 Delivery — Business Rules

**Stage**: CONSTRUCTION — U6 Delivery — Functional Design (Minimal depth)

The delivery rules (DR) say how Airlock is run, built into an image, checked, published and described. They change
nothing about what Airlock does: U1 to U5 own its behaviour, and U6 packages it. "Trace" names the requirement, story,
decision or unit behind a rule. The files are in `domain-entities.md`; the flows and the rule-to-check map are in
`business-logic-model.md`. U6's NFR Requirements (Q-U6-N1 to Q-U6-N4 = A) amended DR-02 to DR-04, DR-07, DR-10 to
DR-14, DR-16, DR-22 and DR-23, and added DR-27 to DR-31; their targets are U6-NFR-01 to U6-NFR-18
(`../nfr-requirements/nfr-requirements.md`). U6's NFR Design then amended DR-02 to DR-04, DR-11, DR-13, DR-16 and
DR-22 (`../nfr-design/nfr-design-patterns.md`), and Infrastructure Design amended DR-01, DR-02, DR-10 and DR-13
(`../infrastructure-design/infrastructure-design.md`; Q-U6-I1 = A).

## Running from source

**DR-01 — Running locally.** A developer installs the .NET SDK that `global.json` names, 10.0.403 or a later 4xx
patch (Q-U6-I1); building inside Visual Studio needs 18.9 or later. The developer then runs
`scripts/install-tools.ps1` on Windows or `scripts/install-tools.sh` on Linux and macOS. The script puts Syft v1.55.0 in
the repository's `bin/` folder. Airlock looks for Syft as U3 IR-06 says: `SYFT_BINARY_PATH` first, then `syft` on the
path, then `bin/syft` next to the application. Next to the application means the build output, not the repository, so
the README has the developer point `SYFT_BINARY_PATH` at the repository's `bin/syft`. The developer then stores the key
with `dotnet user-secrets set Airlock:Nvd:ApiKey <key> --project src/Airlock.Web` and starts Airlock with
`dotnet run --project src/Airlock.Web`, which listens on `http://localhost:8080` in Development. Without a key it
starts DEGRADED (E13). *Trace*: E14 "Run locally on Windows"; NFR-06; U3 IR-06, IR-25; U4 infrastructure design.

## The image

**DR-02 — One image.** One multi-stage `Dockerfile` builds the only image:

- a Syft stage, Anchore's own image at v1.55.0;
- a build stage, the .NET SDK that `global.json` names (`sdk:10.0.403-noble`, Q-U6-I1) running a Release
  `dotnet publish` of `src/Airlock.Web`;
- a runtime stage on chiseled Ubuntu 24.04 with ICU and time-zone data (`aspnet:10.0-noble-chiseled-extra`,
  Q-U6-N1), which holds only the published application, Syft at `bin/syft` next to it, the health probe (DR-10) and
  the notices (DR-05).

Each stage's image is pinned by digest (DR-27). The runtime stage runs in Production, as the chiseled image's
non-root `app` user (UID 1654), with `URLS=http://+:8080` and `EXPOSE 8080`. It clears the base image's
`ASPNETCORE_HTTP_PORTS`, which would otherwise make every start warn that `URLS` overrides it. `ASPNETCORE_URLS` would
not do: `appsettings.json` outranks it, so the container would listen on its own loopback (NFR Design checked both). It
has no shell, so nothing in it runs through one. *Trace*: NFR-01, NFR-06, NFR-11; E14; U3 handover; U4 deployment
architecture; U5 handover; Q-U6-N1.

**DR-03 — What the running user can write.** It can write only `/tmp` and two folders that the image creates for it
on the volume's mount: the staging root, and beside it the folder where ASP.NET Core keeps its data-protection keys,
which therefore survive a restart (NFR Design). Under compose the rest of the filesystem is read-only (DR-29). The
application and Syft belong to root, so the user that runs them cannot change them. A second Airlock on the same
staging root still stops at startup (U4-NFR-09). *Trace*: NFR-03; U3 handover; U4 infrastructure design.

**DR-04 — Syft's one version.** v1.55.0 appears in four places: the two install scripts, the `Dockerfile` and the
README. A repository test fails when they differ. The scripts check Syft's download against the SHA-256 they carry for
each archive, so a replaced release asset stops them, and against its release's checksums file (U3; NFR Design). The
image takes Syft from Anchore's image, pinned by digest (DR-27). Syft's integration tests run against the installed
version and expect its output, so they move with it. Changing the version is one pull request that touches all of
these, the hashes included. *Trace*: E14; NFR-06; U3 IR-25.

**DR-05 — Third-party notices.** The published image redistributes software that is not Airlock's own:

- Syft, under Apache-2.0, whose terms require a copy of the licence;
- the NuGet packages in Airlock.Web's publish output, as restored today: MudBlazor, Scalar.AspNetCore,
  Microsoft.AspNetCore.OpenApi, Microsoft.OpenApi and Microsoft.AspNetCore.App.Internal.Assets under MIT, Polly.Core
  and Polly.RateLimiting under BSD-3-Clause, and QuestPDF under its own licence;
- the fonts, under the SIL OFL 1.1.

`THIRD-PARTY-NOTICES.md` lists each of them with its version, licence and copyright line. The image carries that file,
Airlock's `LICENSE` and Syft's licence text. Code Generation added, in `licenses/`, QuestPDF's licence with the texts
for the projects its native libraries are built from, and ASP.NET Core's notices, which two of the packages ship. The fonts' `OFL.txt` files are already among the static files. The .NET
runtime's own notices come with the base image. A repository test fails when a package that a `src/` project uses is
missing from the notices. Code Generation fills the list from the restored packages, transitive ones included.
Polly's BSD-3-Clause also corrects NFR-10, which says every dependency but QuestPDF is MIT or Apache-2.0. *Trace*:
NFR-10; Q-U6-2.

**DR-06 — What stays out.** `.dockerignore` keeps these out of the build context, so they cannot reach any layer:
`.git`, every `.env` file, the developer's `bin/` folder (which holds a Windows `syft.exe`), build output, test results,
the IDE folders and `.claude/`. The runtime stage holds no SDK, source code, tests or documents beyond the notices.
*Trace*: NFR-03; E13 (the key never leaves the operator's machine).

## Compose

**DR-07 — One service.** `docker-compose.yml` defines `airlock` with the settings in `domain-entities.md`:

- it names the published image and also builds from the `Dockerfile`, so `docker compose up` runs the release in
  `AIRLOCK_IMAGE_TAG` (default `latest`) and `docker compose up --build` builds this copy;
- it publishes `127.0.0.1:${AIRLOCK_PORT:-8080}:8080`;
- it passes `NVD_API_KEY` and `LOG_LEVEL` from `.env`, and sets `Airlock__AllowedScanRoots__0=/uploads`;
- it mounts `SCAN_UPLOAD_PATH` read-only at `/uploads`, and a named volume on the staging root;
- it sets the host name `airlock` and `restart: unless-stopped`;
- it runs the container hardened (DR-29).

There is no second web service, because the UI runs in the same host (ADR-0001). The original had two, on 8000 and
3000. *Trace*: E14 "Run with Docker Compose"; NFR-11; ADR-0001; U4 and U5 handovers; Q-U6-2.

**DR-08 — The scan folder is required.** When `SCAN_UPLOAD_PATH` is unset or empty, compose stops before starting
anything, with a message naming the variable. The original's file failed with Docker's own message about an empty
volume path. Airlock never writes to the folder: it is mounted read-only. *Trace*: E14; ADR-0002 (a clear reason
instead of a puzzling failure).

**DR-09 — Settings files.** Compose reads `.env` next to the compose file. Git and Docker both ignore it. Every
variable the compose file reads is in `.env.example`, with a comment and no secret value; a repository test checks
that. Settings beyond those five go in `docker-compose.override.yml`, which Compose merges on its own and Git ignores.
*Trace*: E13; NFR-03.

## Health

**DR-10 — The image reports its own health.** The image declares a health check that asks
`GET http://localhost:8080/api/v2/system/health`. A 200 answer is healthy, whether the body says ONLINE or DEGRADED:
a missing key is a setting to fix, not a broken container (U4). Any other answer, or none within the timeout, is a
failure, and several failures in a row make the container unhealthy. A start period covers startup. The runtime
image has no shell (Q-U6-N1), so the check runs a small probe that ships with Airlock, as an executable with
arguments: `/usr/bin/dotnet /app/Airlock.Web.dll --health-probe`, every 30 seconds, and every 2 seconds during a
30-second start period (Infrastructure Design). Because the check lives in the image,
`docker run` gets it as well as compose. *Trace*: NFR-07; U4 infrastructure design; Q-U6-N1; U6-NFR-11.

## Continuous integration

**DR-11 — When CI runs.** `ci.yml` runs on pushes to `main`, on pull requests to `main`, by hand, and when
`release.yml` calls it. It has `contents: read` and nothing more. Its jobs are lint (DR-28), test (DR-12) and image
(DR-13). When `release.yml` calls it, the image job is skipped: the release's publishing job builds and tests the one
image it publishes (DR-22; NFR Design). A newer run for the same branch or pull request cancels the older one. It reads
no secret, so pull requests from forks get the same checks. *Trace*: E15; NFR-11; Q-U6-1.

**DR-12 — The test job**, on `ubuntu-24.04` and on `windows-2025` (U6-NFR-03):

1. installs the SDK that `global.json` names, then restores and builds; warnings are errors (`Directory.Build.props`);
2. installs Syft with that platform's install script and sets `SYFT_BINARY_PATH`, so the Syft integration tests run
   instead of skipping;
3. runs every test project with coverage, and fails when a project falls below its targets on either platform
   (`domain-entities.md`);
4. keeps the test results and coverage reports as the run's artifacts.

The live NVD test stays skipped, because `AIRLOCK_LIVE_NVD` is never set, and no NVD key exists in CI (Q-U6-1 = A).
*Trace*: E15 "Pull requests are verified"; NFR-06, NFR-09; U1-NFR-06, U2-NFR-13, U3-NFR-13, U4-NFR-14, U5-NFR-14
("enforced in CI from U6").

**DR-13 — The image job**, on `ubuntu-24.04` (amended by U6 NFR Requirements). It builds the image from the commit
as the release does, with the same script (Infrastructure Design). It tests it under the compose file's settings, with
`SCAN_UPLOAD_PATH` set to `tests/smoke/uploads`, which holds a small `requirements.txt`. The tests run in two phases.

1. **Without a key.** Once the health check reports healthy:
   - health answers 200 with `DEGRADED`; the `Syft` check passes with `1.55.0`, so Syft is in the image and runs; the
     `NVD API key` check fails;
   - the published port answers: Control Center's page returns 200, and so do the stylesheets it names, which proves a
     published build serves its static files (U5);
   - a scan of `/uploads` is refused with 503 and the missing-key problem, so the API answers in Production;
   - the container's process runs as `app`, UID 1654, not as root.
2. **With NVD unreachable.** The container restarts with a fixed stand-in key, which never leaves the runner, and with
   `Nvd:BaseUrl` at a closed port on 127.0.0.1. A scan of `/uploads` must end Incomplete, with every package Syft found
   Unverified — ADR-0002's fail-closed path, in the shipped image — and its PDF must download. This proves that Syft,
   the staging volume, the hardening (DR-29) and QuestPDF's native code work in the image, with no NVD and no secret
   (Q-U6-1). The container's log must not contain the stand-in key.

Syft then makes the image's SBOM, and Grype scans it (DR-30). Last, the job stops the container and removes its
volume. On a failure it prints the container's log first. *Trace*: E15; NFR-11; ADR-0002; U4 handover ("the container
answers on the published port, which catches a listening address left on the container's loopback"); U6-NFR-12.

**DR-14 — A failure fails the run.** No step continues after an error, and every job must pass on every platform.
The pull request shows the result. *Trace*: E15 "reports failure if any step fails".

## Code scanning and updates

**DR-15 — CodeQL.** `codeql.yml` analyses C#, as E15 asks, and JavaScript, which covers `airlock.js` at little cost;
the original scanned both of its languages. It runs on pull requests to `main`, on pushes to `main` (which gives pull
requests a base to compare with) and once a week. Its permissions are `contents: read`, `security-events: write` and
`actions: read`. Findings appear as the repository's code scanning alerts. *Trace*: E15 "Code scanning"; NFR-11.

**DR-16 — Dependabot.** Once a week, Dependabot proposes updates for NuGet (`Directory.Packages.props`), Docker (the
`Dockerfile`'s base images), GitHub Actions and the .NET SDK (`global.json`), keeping the pins and their version
comments (DR-27). Minor and patch updates are grouped into one pull request per ecosystem, and each major update
comes on its own. Major updates of the SDK, Microsoft's .NET images and the `Microsoft.Extensions.*` and
`Microsoft.AspNetCore.*` packages are not proposed, so Airlock stays on .NET 10 LTS; moving to the next LTS is a
deliberate change (NFR Design). A version update waits 7 days after its release; security updates, Microsoft's .NET
images and the SDK do not wait (U6-NFR-02). Every proposal goes through CI like any pull request. A proposal that
moves Syft's image alone fails DR-04's test until the scripts, the README and the integration tests follow in the
same pull request.
That makes it a weekly signal that a newer Syft exists. *Trace*: E15; NFR-11; Q-U6-N4.

## The README

**DR-17 — The README describes what Airlock actually does.** Every claim in it is backed by code, a test or a Build
and Test check. It has the sections in `domain-entities.md`. Its configuration table gives each setting's key,
environment variable, default and range, taken from the options' defaults (U4: "one source of truth"). Links point at
`github.com/JJWren/Airlock-DotNet`, as the problem types already do. *Trace*: E15; defect 18 (README drift); U3 to U5
handovers.

**DR-18 — Licensing and credits.** The README states:

- Airlock is MIT-licensed;
- QuestPDF is used under its Community licence, which is free below USD 1M in annual revenue;
- MudBlazor is MIT;
- Inter and JetBrains Mono are under the SIL OFL 1.1;
- Syft is Apache-2.0;
- everything else is in `THIRD-PARTY-NOTICES.md`.

It also carries the notice NVD's terms of use ask for: "This product uses the NVD API but is not endorsed or certified
by the NVD". It credits the original Airlock. A repository test checks the MIT statement, QuestPDF's licence and
threshold, and the NVD notice. *Trace*: E15 "Licensing is documented"; NFR-10.

## Releases (Q-U6-2 = B)

**DR-19 — When a release runs.** `release.yml` runs when a tag `v` plus a semantic version is pushed: `vX.Y.Z`, or
`vX.Y.Z-suffix` for a pre-release. It runs in `JJWren/Airlock-DotNet` only, so a fork's tag publishes nothing. It cannot
be started by hand, so only a pushed tag can publish. *Trace*: Q-U6-2; DR-26.

**DR-20 — The version must agree.** Before anything is built, the tag without its `v` must equal `Version` in
`Directory.Build.props`. Otherwise the run fails, naming both values. This keeps the version that health and the UI
show the same as the image's tag. *Trace*: Q-U6-2; U4 WR-17 (health's `version`).

**DR-21 — A version is published once.** When `X.Y.Z` already exists in the registry, the run fails before building.
A published version never changes; a fix gets a new version. *Trace*: Q-U6-2; NFR-03.

**DR-22 — Publish only what was tested.** The release first runs CI's lint and test jobs on the tagged commit (DR-12,
DR-28). When they pass, the publishing job builds the release's one image from that commit, smoke-tests it as DR-13
does and scans it (DR-30). It then pushes that same image without a tag, stores its attestations (DR-31) and verifies
them with `gh attestation verify`, as a user would (NFR Design). Only after that does it tag the image's digest
`ghcr.io/jjwren/airlock-dotnet:X.Y.Z`, adding `:latest` when the version has no pre-release suffix, and it checks that
the tags name that digest. So no tag ever names an image that was not tested, scanned and attested, with attestations
that verify. How the image is pushed without a tag, and tagged without a new manifest, is Infrastructure Design's. The
image is for linux/amd64 only, the platform the smoke test runs on. *Trace*: Q-U6-2,
Q-U6-N2, Q-U6-N3; E15; U6-NFR-07.

**DR-23 — Least privilege.** Only the publishing job has more than `contents: read`: `packages: write` to push, and
`id-token: write` and `attestations: write` to sign and store the attestations (Q-U6-N3). It signs in to `ghcr.io` with
the run's own `GITHUB_TOKEN`, so no personal access token or other secret is stored for it. *Trace*: Q-U6-2, Q-U6-N3;
NFR-03.

**DR-24 — Labels.** The image carries the OCI labels `org.opencontainers.image.source` (the repository's URL, which
links the package to the repository), `version`, `revision` (the commit), `licenses` (`MIT`), `title`, `description`
and `created`. *Trace*: Q-U6-2.

**DR-25 — The owner's steps.** The README's Releasing section gives the steps:

1. bump `Version` in `Directory.Build.props` on a branch, and merge it to `main`;
2. tag the merged commit `vX.Y.Z`;
3. push the tag.

GitHub creates the package on the first publish. Whether others may pull it without signing in is the package's
visibility, which only the owner can set in GitHub. *Trace*: Q-U6-2; DR-26.

## Nothing pushed

**DR-26 — Nothing reaches GitHub without the owner.** The AIDLC work commits locally and pushes nothing. The owner
creates `JJWren/Airlock-DotNet` and pushes its branches and every tag. No workflow writes to the repository: each has
`contents: read`. The workflows first run when the owner pushes. Until then, Code Generation checks them locally.
*Trace*: E15; NFR-11; Q18.

## Added by NFR Requirements (Q-U6-N1 to Q-U6-N4 = A)

**DR-27 — Everything CI runs is pinned.** Every action is named by its full commit SHA, with its version as a comment;
every `FROM` names its image by tag and digest; every tool CI fetches is pinned to a version and checked against a
checksum or digest. CI rejects an unpinned reference (DR-28, and a repository test). Airlock's own image in the compose
file is the exception, since users choose its tag (DR-07). *Trace*: Q-U6-N4; U6-NFR-01.

**DR-28 — The workflows are checked.** A lint job in `ci.yml` runs actionlint and zizmor on every workflow. zizmor
requires a commit SHA for every action, GitHub's own included. Any finding fails the run unless `.github/zizmor.yml`
accepts it with a reason. The workflows use GitHub's own actions only; Docker, Syft, Grype and the linters run as
command-line tools. *Trace*: U6-NFR-03, U6-NFR-04.

**DR-29 — The container runs hardened.** Compose gives the container a read-only root filesystem, with writable mounts
only for the volume, which holds the staging root and the data-protection keys, and for `/tmp` (in memory). It drops
every Linux capability, sets `no-new-privileges`, and runs an init process that forwards signals and reaps child
processes. It mounts no Docker socket, and asks for no privileged mode or host network. *Trace*: NFR-03; U6-NFR-09.

**DR-30 — The image is scanned.** Grype scans the SBOM Syft makes of the image, in CI's image job and in the release
before anything is pushed. A High or Critical vulnerability that has a fixed version fails the run. Every finding,
whatever its severity, is kept in the run's report. A finding is accepted only in `.grype.yaml`, which names the
vulnerability and the package and gives a reason and a review date. A repository test fails once a review date has
passed. *Trace*: Q-U6-N2; U6-NFR-06.

**DR-31 — Published images carry proof of their origin.** For every published version, the publishing job stores two
signed GitHub artifact attestations, bound to the image's digest and pushed to GHCR beside it. One is build
provenance, naming the workflow and the commit that built the image; the other is the SBOM Syft made of it. The README
shows how to check both with `gh attestation verify`. Attestations need a public repository, or GitHub Enterprise Cloud
for a private one. *Trace*: Q-U6-N3; E15; U6-NFR-07.
