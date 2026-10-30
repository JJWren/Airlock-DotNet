# U6 Delivery — NFR Design Patterns

**Stage**: CONSTRUCTION — U6 Delivery — NFR Design (Minimal depth)

How U6-NFR-01 to U6-NFR-18 (`../nfr-requirements/nfr-requirements.md`) are met. Paths, digests, sizes and timings are
Infrastructure Design's. The facts this relies on were checked on 2026-10-29
(`../../plans/u6-delivery-nfr-design-plan.md`). The pieces it adds, and the checks, are in `logical-components.md`.

| NFR | Pattern | How it is applied |
|---|---|---|
| U6-NFR-01 Pinning | Immutable references, with the expected value kept in this repository | Actions are written `owner/repo@<40-character SHA> # vX.Y.Z`. zizmor 1.31.1 already requires a SHA for every action, and `.github/zizmor.yml` says so explicitly. Every `FROM` is written `name:tag@sha256:…`. Each tool CI downloads is compared with a SHA-256 or digest committed here, never only with a checksums file fetched beside it, so a replaced release asset stops the run. The release's own checksums file stays a second check. U3's install scripts gain the SHA-256 of each Syft archive they can fetch. |
| U6-NFR-02 Updates | One updater, a cooldown with named exceptions, and the LTS line held | Dependabot alone moves the pins, weekly and grouped. Version updates wait 7 days, except Microsoft's .NET images, which the cooldown excludes by name, and the .NET SDK, whose entry has no cooldown. Major updates of the .NET SDK, Microsoft's .NET images and the `Microsoft.Extensions.*` and `Microsoft.AspNetCore.*` packages are ignored. So .NET 11, an STS release due in November 2026, is never proposed, and moving to the next LTS is a deliberate change. Every other major update still arrives on its own. |
| U6-NFR-03 What CI runs | An allow-list, enforced by the linter | zizmor's `forbidden-uses` audit, given an allow-list of GitHub's own actions, rejects any other `uses:`. Docker, Syft, Grype, actionlint, zizmor, and GitHub's own `gh` that the runner carries, run as commands. A repository test keeps `runs-on` to `ubuntu-24.04` and `windows-2025`. |
| U6-NFR-04 Workflow security | Deny by default and grant per job; linters for what they know, tests for the rest | Each workflow sets `permissions: contents: read` at the top; only the jobs that need more are granted it. zizmor's audits cover permissions (`excessive-permissions`), checkout credentials (`artipacked`), `${{ }}` inside scripts (`template-injection`), triggers (`dangerous-triggers`) and caches in a release (`cache-poisoning`). It runs online with the job's read-only token, so `impostor-commit` and `known-vulnerable-actions` run too. Repository tests cover the rest: a time limit on every job; the release's repository and tag guards and its step order; and the concurrency groups. The groups have fixed names, `ci-` or `release-` plus the ref, so the CI jobs a release calls never share a group with the release, and only CI cancels an older run. Accepted findings live only in `.github/zizmor.yml`, each with a comment giving its reason, and a repository test rejects inline ignore comments. |
| U6-NFR-05 Secrets | A canary key | The stand-in key is a fixed value, written in the smoke test, that cannot be a real key and is easy to search for. It reaches only the second phase's container, through `NVD_API_KEY`, and is never a build argument. Finding it in the container's log, or in the image's configuration or history, fails the test. |
| U6-NFR-06 Vulnerability management | One SBOM, the policy in one file, and acceptances that expire | Syft makes one SBOM of the built image. Grype reads it, the run keeps it, and the release attests it. `.grype.yaml` holds the whole policy — `only-fixed: true`, `fail-on-severity: high` and the accepted findings — so CI, the release and a developer running `grype` in the repository all apply the same one. `only-fixed` works by adding ignore rules, so Grype's JSON report still lists every unfixed match under `ignoredMatches`: one run gives both the gate and the full report. Each acceptance names the vulnerability and the package. Grype's rules have no date field, so its `reason` begins `review-by YYYY-MM-DD:`, and the repository test fails once that date has passed (UTC). Grype refuses a database built more than five days ago, so a stalled feed fails the job instead of passing an image unscanned. |
| U6-NFR-07 Release integrity | Build once, promote by digest, verify, tag last | See "The release" below. |
| U6-NFR-08 Runtime image | Nothing needs a shell | The runtime stage has no `RUN`, and its `ENTRYPOINT`, its `HEALTHCHECK` and every Syft run are exec-form. Airlock's and Syft's files are copied owned by root. The folders `app` writes are made in the build stage and copied owned by UID 1654. A repository test reads the runtime stage. |
| U6-NFR-09 Hardening | A read-only root with declared writable places | See "Writable places" below. Docker's own `init` is the init process. `cap_drop: [ALL]` and `no-new-privileges` hold because Airlock listens above port 1024 and runs Syft as its own user. |
| U6-NFR-10 Disk | Explicit paths, no fallback | The image sets the staging root and the keys' folder under the volume's mount, so nothing falls back to `/tmp`, which is in memory. Syft's temporary files already follow the scan's folder (U3 IR-01). |
| U6-NFR-11 Health | A quiet probe inside the image | The exec-form health check runs a probe that builds no host — no configuration, logging or services — so it cannot write to Airlock's logs or the staging root. It sends one `GET /api/v2/system/health` to `localhost:8080`, a host name Airlock allows (ADR-0007). It exits 0 on a 200, and 1 on any other answer or at its timeout. Health requests log nothing at Information: `Microsoft.AspNetCore` is at Warning, and Code Generation checks Airlock's own categories. A short interval during the start period lets "healthy within 30 seconds" be observed. |
| U6-NFR-12 Smoke test | A black box run through the compose file that users run | The smoke test starts the built image with `docker-compose.yml` plus a smoke override. The override changes only the image and the folder behind `/uploads`, and for the second phase the canary key and `Nvd:BaseUrl`. So the hardening under test is the file's own, not a copy of its flags, and a repository test keeps the override to those keys. The fail-closed path is the case that must pass: the second phase passes only when the scan ends Incomplete with every package Unverified. One script holds both phases. CI's image job and the release's publishing job both call it, and a developer can run it against their own build. |
| U6-NFR-13 Budgets | Measure, report, warn | The image job measures the image's compressed layers, as the registry will serve them. It reports them, with each job's duration, in the run summary. Past 150 MiB it adds a warning annotation but does not fail, since the target is reported. Job time limits start at about twice the estimated length, and are tightened after the first runs. |
| U6-NFR-14 Portability | A matrix, not copies | One test job with a two-runner matrix. The image is built for `linux/amd64` only. |
| U6-NFR-15 Maintainability | One definition, several callers | Each tool's version and checksum live in one place, or a repository test checks that its places agree. `release.yml` calls `ci.yml`. The steps a workflow and a developer share are scripts under `scripts/`, not inline YAML. One-line comments name the DR and U6-NFR rules each part serves. |
| U6-NFR-16 Observability | Results first, the verdict last | Every check writes a machine-readable result: test results, Cobertura reports, Grype's JSON, the SBOM and the image's metadata, all kept for 14 days. One step renders the run summary from them. A failing check writes its results, and the container's log, before it fails the job. The warnings a clean start may log form one list. The smoke test fails on any other warning, and a repository test checks that the README explains each one on the list. |
| U6-NFR-17 Licensing | Notices travel with the binaries | The runtime stage copies `LICENSE`, `THIRD-PARTY-NOTICES.md` and Syft's licence. Nothing removes the base image's own files, since the stage has no `RUN`. The attested SBOM lists the rest. |
| U6-NFR-18 Documentation | A tested README | Repository tests check its facts (DR-17, DR-18) and the expected warnings. Build and Test follows the quick start from a fresh clone. |

## The release (U6-NFR-07)

A release builds one image. When `release.yml` calls `ci.yml`, CI's image job is skipped, and the publishing job runs
the same smoke test and scan on the image it builds. From then on, every step names the image by its content, never
by a tag.

| Step | Names the image by | Stops the release when |
|---|---|---|
| Build, with no build cache imported | — and records the image's ID, its configuration digest | the build fails |
| Smoke test, SBOM, Grype | that ID | any check fails |
| Push without a tag | that ID; the push gives the manifest digest D | the pushed image's configuration digest is not the tested ID |
| Attest the provenance and the SBOM | D | either is not stored |
| Verify both with `gh attestation verify`, as a user would | D | either fails to verify |
| Tag `X.Y.Z`, and `latest` when there is no pre-release suffix | D, without writing a new manifest | a tag names anything but D |

A failed step leaves D untagged in the registry, where it names no version. DR-21's check looks at tags, so it still
lets a new attempt run. The owner can delete such untagged versions in GitHub.

## Writable places (U6-NFR-09, U6-NFR-10)

| Place | Backed by | Holds | After a restart |
|---|---|---|---|
| The staging root | The named volume | Uploads, each scan's folder, Syft's temporary files, the `.airlock-lock` file | Kept; each start sweeps any scan folder a crash left |
| The data-protection keys' folder | The same volume, beside the staging root and outside the sweep | ASP.NET Core's key ring | Kept |
| `/tmp` | A small `tmpfs`, in memory | What the .NET runtime and libraries put there, such as the diagnostics socket | Empty |

The image creates both folders on the volume's mount, owned by UID 1654. A new named volume takes that owner, so `app`
can write without root. The keys persist because U4's default puts them in memory when no folder is writable. Then
every restart would log an Error for each browser's antiforgery cookie issued before it: ASP.NET Core logs a cookie it
cannot read at Error. With the keys kept, ASP.NET Core creates a key only at the first start and when it renews the key
every 90 days. Each time, it warns that the key is stored unencrypted; that warning is on the list of expected
warnings, and the README explains it.

## Found while designing

- **The listening address.**
  - `ASPNETCORE_URLS` does not override the `"Urls": "http://localhost:8080"` in `appsettings.json`.
    `ASPNETCORE_` variables are host configuration, and application configuration outranks them.
  - Checked on 2026-10-29: started from its build in Production with `ASPNETCORE_URLS` set, Airlock still listened on
    `http://localhost:8080`. With `URLS` set, it listened on the address in `URLS`.
  - So in the container, DR-02's `ASPNETCORE_URLS=http://+:8080` would have left Airlock on its own loopback,
    unreachable through the published port. The image sets `URLS=http://+:8080` instead.
  - The image also clears the base image's `ASPNETCORE_HTTP_PORTS=8080`. With both set, every start logs a warning
    that `URLS` overrides `HTTP_PORTS`.
  - Amended: DR-02, DR-13's trace, `domain-entities.md`, the rule-to-check map, and U4's infrastructure design and
    deployment architecture.
- **Syft's checksum.** U3's install scripts compare Syft's archive with the checksums file of the same release,
  downloaded at the same moment. That catches a damaged download but not a replaced one. The scripts gain the SHA-256
  of each archive (U6-NFR-01; DR-04).
