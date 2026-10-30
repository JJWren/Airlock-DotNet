# U6 Delivery — Code Generation Summary

**Stage**: CONSTRUCTION — U6 Delivery — Code Generation, Part 2 (Generation)
**Plan**: `construction/plans/u6-delivery-code-generation-plan.md` (approved 2026-10-30T00:43:49Z; Q-CG6-2 answered
2026-10-30T15:31:22Z)
**Completed**: 2026-10-30T15:38:24Z · **Branch**: `feature/u6-delivery`, from `main` fast-forwarded to the U5 commit

## Result

| Check | Target | Result |
|---|---|---|
| SDK (A1, A4) | 10.0.403 | Installed; `global.json` names 10.0.403 with `latestPatch`. U1 to U5 built on it with no warning, and 1,128 of 1,131 tests passed, as before |
| Pins (A3) | as designed | Every image digest, action SHA and Syft hash read again on 2026-10-30, unchanged; no newer release under the 7-day rule |
| Build, warnings as errors (I1) | 0 warnings | 0 warnings, 0 errors, the three C# scripts included |
| All tests, with Syft v1.55.0 through `SYFT_BINARY_PATH` (I1) | all pass | 1,236 of 1,239; the 3 skipped are U3's, as before |
| Delivery tests (G) | all pass | 85 of 85, new |
| Web tests | all pass | 395 of 395: U5's 373 and 22 new, for the probe, the keys' folder and quiet health requests |
| Infrastructure tests | all pass | 271 of 274, 3 skipped: U5's and 1 new, for Syft's status logging |
| Domain, Application and Architecture tests | all pass | 211, 206 and 68, unchanged |
| Coverage, each project from its own tests (I2) | 95/90, 90/85, 85/80, 90/85 | Domain 100 % lines (588 of 588) and 99.3 % branches (282 of 284); Application 97.1 % and 94.7 %; Infrastructure 95.6 % and 85.5 %; Web 96.3 % (2,187 of 2,272) and 86.6 % (843 of 973) |
| Each repository test class fails once (I3) | 12 of 12 | 12 of 12, each naming the rule a temporary edit broke; every file then restored byte for byte |
| actionlint, shellcheck, zizmor offline (I4) | no unaccepted finding | None; zizmor's one finding is accepted in `.github/zizmor.yml` (deviation 6) |
| Image download size (I5, U6-NFR-13) | at most 150 MiB | 112.7 MiB, in 9 layers |
| Smoke test (I6) | both phases | Passed in 48 s: healthy after 3.3 s and 4.5 s; the scan Incomplete after 36 s, both packages Unverified; a 34,992-byte PDF |
| Image scan (I7) | no fixable High or Critical | Passes, with 12 findings in Syft's binary accepted to 2026-11-30 (Q-CG6-2 = A) |
| Release rehearsal (I8) | as designed | The pushed configuration digest is the tested one; both tags name the pushed digest; a moved tag and a republished version are refused |
| Compose's fallback (I9) | builds when the pull fails | Builds an unpublished tag from this copy; pulls a published image |

Run the checks with:

```bash
dotnet build
dotnet test --coverage --coverage-output-format cobertura --report-xunit-trx --report-xunit-trx-filename results.trx
dotnet run scripts/ci/coverage.cs
bash scripts/ci/run-tool.sh actionlint
bash scripts/ci/run-tool.sh shellcheck scripts/install-tools.sh scripts/ci/*.sh
bash scripts/ci/run-tool.sh zizmor --offline --config .github/zizmor.yml .
bash scripts/ci/build-image.sh load
dotnet run scripts/ci/smoke-test.cs -- --image airlock-dotnet:smoke
bash scripts/ci/scan-image.sh
```

The bash scripts ran in WSL's Ubuntu 24.04, the runner's release, with jq 1.7.1; the rest ran on Windows.

## Files

### At the repository's root

| File | What it is |
|---|---|
| `Dockerfile` | The `syft`, `build` and `runtime` stages, each pinned by tag and digest |
| `.dockerignore` | The allowlist, with `.editorconfig`, then build output and any `.env` excluded |
| `docker-compose.yml` | The `airlock` service, hardened, on `127.0.0.1` |
| `.env.example` | The five variables, each with a comment |
| `.grype.yaml` | The gate, and the 12 acceptances of Q-CG6-2 |
| `THIRD-PARTY-NOTICES.md` | The 8 packages Airlock.Web restores, QuestPDF's native libraries, Syft, the fonts, and the base image's notices |
| `licenses/` | `syft/LICENSE`; `questpdf/` (its licence and 14 texts from its package); `microsoft-aspnetcore/THIRD-PARTY-NOTICES.TXT` |
| `global.json` | SDK 10.0.403, `latestPatch` |
| `README.md` | The complete README, replacing U1's placeholder |
| `.gitignore` | Gains `docker-compose.override.yml` |
| `Directory.Packages.props`, `Airlock.slnx` | YamlDotNet 18.1.0 under Tests; the Delivery tests' project |

### `.github/`

`workflows/ci.yml`, `workflows/codeql.yml`, `workflows/release.yml`, `dependabot.yml`, `zizmor.yml`, and
`ci-tools/Dockerfile`, which pins Grype, actionlint, zizmor and BuildKit and is never built.

### `scripts/`

| File | What it does |
|---|---|
| `ci/run-tool.sh` | Runs a pinned tool image, or prints a stage's image with `--ref` |
| `ci/build-image.sh` | `load`: the build into Docker and as an OCI archive; `push`: the same build by digest, with the configuration checks |
| `ci/smoke-test.cs` | Both phases of the smoke test, through compose |
| `ci/scan-image.sh` | Both SBOMs from one Syft run, then Grype |
| `ci/promote.sh` | `verify`, with `gh attestation verify`; `tag`, refusing to move a version tag |
| `ci/check-release.sh` | The tag against `Version`, and the version against the registry |
| `ci/coverage.cs` | The coverage checker; writes `TestResults/coverage.json` |
| `ci/summary.cs` | The run summary |
| `install-tools.sh`, `install-tools.ps1` | Each archive's SHA-256, checked as well as the release's checksums file |

### `src/`

| File | Change |
|---|---|
| `Airlock.Web/HealthProbe.cs` | New: the `--health-probe` mode |
| `Airlock.Web/KeyStorage.cs` | New: `KeyStorageOptions`, its validator, `KeyStorageSetup` and `AddAirlockKeyStorage` |
| `Airlock.Web/Program.cs` | Hands `--health-probe` to the probe before building a host |
| `Airlock.Web/AirlockHost.cs` | Adds the keys' storage |
| `Airlock.Infrastructure/Syft/SyftStatusProbe.cs` | Logs only a change of Syft's status (deviation 2) |

### `tests/`

| File | Covers |
|---|---|
| `Airlock.Delivery.Tests/` | The 12 classes of the design, with `Support/` (the repository, YAML, Dockerfiles) |
| `Directory.Build.props` | Each test project's results in `TestResults/<project>/` (deviation 8) |
| `smoke/compose.smoke.yml`, `smoke/uploads/requirements.txt`, `smoke/expected-warnings.txt` | The smoke test's override, fixture and expected warnings |
| `Airlock.Web.Tests/Host/HealthProbeTests.cs` | The probe against a stub server; Airlock.Web run with the argument prints one line and builds no host |
| `Airlock.Web.Tests/Host/KeyStorageTests.cs` | Keys in the folder, under Airlock's name, surviving a restart; unset, U4's behaviour; the path refused by name |
| `Airlock.Web.Tests/Host/HostCompositionTests.cs` | A relative keys' folder stops startup, naming the setting |
| `Airlock.Web.Tests/Endpoints/HealthEndpointTests.cs` | Health requests log nothing once Syft's status is known |
| `Airlock.Infrastructure.Tests/Syft/SyftToolStatusTests.cs` | A re-check logs only a change of status |

## The image

- **Download size**: 112.7 MiB in 9 layers; the runtime base is about 69 MiB, Syft 30 MiB, Airlock the rest.
- **Owners and modes**: `/app` and `/app/bin/syft` belong to root, and nothing there is writable by group or others.
  `/var/lib/airlock`, `staging/` and `keys/` belong to UID 1654, the last two mode 0700.
- **Licence files**: those of the infrastructure design's table, as corrected: Ubuntu's `gcc-14-base` copyright
  covers `libgcc-s1` and `libstdc++6`.
- **Start**: healthy about 3 seconds after starting; each health check takes about 0.4 s; the only warning is
  `XmlKeyManager[35]`, at a volume's first start. Without a volume, as with a plain `docker run`, ASP.NET Core also
  warns `FileSystemXmlRepository[60]`; the README says to mount one.

## The image scan and Q-CG6-2

The first scan failed on 12 vulnerabilities with a fix, all in `/app/bin/syft`, Syft v1.55.0's binary: 11 in Go
1.26.8's standard library (GO-2026-6612 and GO-2026-6604 Critical; GO-2026-6603, -6605, -6607, -6608, -6609, -6610,
-6611, -6613 and -6617 High), fixed in Go 1.26.9, and GHSA-mgcc-cww5-fp2q (High) in go-jose 4.1.5, fixed in 4.1.6.
Nothing in .NET, Airlock's packages or the Ubuntu base failed the gate. Syft v1.55.0 was still Syft's newest release.
You chose A: the 12 are accepted in `.grype.yaml`, each limited to `/app/bin/syft`, to be reviewed by 2026-11-30. The
scan then passed. Under the gate remain 2 Medium and 3 Unknown findings with a fix, and 9 without one.

## Deviations from the design and the plan

1. **The image job's condition.** The design's `inputs.image != false` would skip the job on every push and pull
   request: GitHub compares loosely and turns both null and false into 0. `ci.yml` uses
   `toJSON(inputs.image) != 'false'`, and a repository test holds it.
2. **Syft's status logging (U3 code).** Checking U6-NFR-11 found that every health request after Syft's 30-second
   status cache logged Syft's status again, at Debug, which the Live Log keeps; the image's health check would have added
   a Live Log line every half minute. `SyftStatusProbe` now logs only a change of status, with a test.
3. **The keys' setup reads its setting unvalidated.** Antiforgery reads data protection's options while the pipeline is
   built, before startup validation. Reading the validated options there made a bad path fail before the validation
   that names it. The setup ignores a path that is not a full path, which startup then refuses by name, as designed.
4. **The configuration digest comes from the archive.** With two outputs BuildKit's metadata names the manifest but no
   configuration digest, so `build-image.sh load` reads it from that manifest in the OCI archive. `push` also checks it
   as read back from the registry.
5. **`/app`'s modes are set in the build stage.** Files copied from a build context keep its modes; from WSL's view of a
   Windows drive every file was 0777, so `app` could have changed `appsettings.json` and the static files. The build
   stage now copies the notices into `/app` and sets its modes, so the runtime stage has three copies and no `RUN`.
6. **zizmor's acceptances.** zizmor 1.31 asks `release.yml` to call `ci.yml` with GitHub's newer `$/` form, which the
   pinned actionlint 1.7.12 rejects; `self-repository` is accepted for that line. zizmor 1.31.1 does not report the SDK
   entry's missing cooldown while other entries have one; its acceptance stays, at the entry's line, for a zizmor that
   does. A test checks that each acceptance names its line.
7. **The release's version check.** GHCR gives no token for a repository without a package, so `check-release.sh` takes
   that as no release yet. `promote.sh tag` refuses to move a version tag that names another digest, which keeps DR-21
   whatever the version job could read.
8. **Each test project's results in a folder of its own.** By default every project's coverage report lands in one
   `TestResults/` folder under a random name. `tests/Directory.Build.props` gives each `TestResults/<project>/`, and
   the coverage checker reads each project's report from there.
9. **Branch coverage counts.** Some branch lines carry fractional percentages, such as `66.67% (4/6)`. The script U3 and
   U5 used for their figures read only whole ones and skipped those lines' branches, so their branch figures were a
   little high (U5's Web 88.1 %, U3's Infrastructure 87.2 %). The checker reads the counts: 86.6 % and 85.5 %, still
   above the targets.
10. **More notices.** QuestPDF's native libraries embed Skia, HarfBuzz, FreeType and others, and two ASP.NET Core
    packages ship third-party notices. Their texts are in `licenses/`, and the image carries them.
11. **Six hashes in `install-tools.ps1`.** It runs on PowerShell 7 everywhere and already supported Windows on arm64,
    so it carries that archive's hash too; `install-tools.sh` carries four.
12. **The notices test checks versions.** A package update, and an SDK update, which moves
    `Microsoft.AspNetCore.App.Internal.Assets`, fail it until `THIRD-PARTY-NOTICES.md` is updated: a review point for
    licence changes. The README says so.
13. **The smoke test's start.** Its warning check reads the log up to "Application started", where a warning not on the
    list, or any error, fails. After it, the second phase's scan logs its fail-closed warnings and errors. Compose v5's
    `top` puts its own columns before `UID`, so the test finds the column by its header.

The plan's three refinements held: `--env-file` replaces `.env`, which Compose otherwise reads; without
`PublishAot=false` the scripts' build fails on IL2026 and IL3050; and restoring the Delivery tests writes the assets file
`NoticesTests` reads.

## On this machine afterwards

- The builder `airlock-dotnet` is stopped and kept, with its cache, for Build and Test.
- The image `airlock-dotnet:smoke` and the pulled tool images stay; no container, volume or network of this work is
  left. Grype's database was deleted.

## Left for Build and Test, and for the first push

- **What needs GitHub**: zizmor's online audits; CodeQL; the Windows runner; the push to GHCR; the attestations and
  their verification; the tags on the real registry; `check-release.sh` against GHCR, first release included.
- **E14 with a real NVD key**, and the quick start from a fresh clone.
- **Stopping the container during a scan**, and time zones in the container.
- **U5's remaining checks**.
- **The review of `.grype.yaml`'s acceptances by 2026-11-30**, or sooner, when a Syft release built with Go 1.26.9 or
  later passes the 7-day cooldown.
- **At the commit**, the bash scripts are added executable: `git add --chmod=+x scripts/ci/*.sh scripts/install-tools.sh`.
