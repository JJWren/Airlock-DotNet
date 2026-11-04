# Airlock

Airlock checks third-party software before it is admitted into a controlled environment. You give it a folder or a
set of files; it lists the packages inside them and looks each one up in NIST's National Vulnerability Database (NVD).

It is a rebuild, on .NET 10 with a Blazor UI, of [Airlock](https://github.com/JJWren/Airlock), a Python API and React
UI first written in college.

## What Airlock is

Airlock does **software composition analysis (SCA)**:

1. [Syft](https://github.com/anchore/syft) builds the SBOM, the inventory of packages, from a directory or from uploaded
   artifacts: manifests, lock files, archives, package files, container image tarballs and binaries.
2. Each package is matched to NVD's records: first by its candidate CPEs, then by product name and version when no CPE
   has any record.
3. Every CVE rated HIGH or CRITICAL is a finding. Lower severities are not reported.

It is **not** SAST: it does not read your own source code for insecure patterns. `GLOSSARY.md` defines the terms used
here.

What changed from the original:
- **Scans fail closed** (`docs/adr/0002-scans-fail-closed.md`). A package whose lookups fail is Unverified, never Clean,
  and the scan ends Incomplete. A bad path, a Syft failure or a missing NVD key fails or refuses the scan instead of
  reporting "all clean".
- **Matches are checked**: product matching against the CVEs' own product lists replaces the original's keyword search,
  which reported unrelated CVEs (`docs/adr/0006-product-match-replaces-keyword-search.md`).
- **The API is asynchronous** (`/api/v2`): a scan is a resource you create, follow and fetch.
- **Uploads accept any artifact Syft understands**, not just text manifests.
- **One host**: the Blazor UI and the API run in one ASP.NET Core process, as one container.

## Quick start

You need Docker with Compose v2, and an NVD API key from <https://nvd.nist.gov/developers/request-an-api-key>.

```bash
cp .env.example .env
```

In `.env`, set `SCAN_UPLOAD_PATH` to the folder Airlock may scan and `NVD_API_KEY` to your key. Then:

```bash
docker compose up -d
```

Open <http://localhost:8080>. `docker compose up` runs the published release named by `AIRLOCK_IMAGE_TAG`, `latest`
by default. When that tag cannot be pulled, as before the first release, Compose builds this copy instead. To run
this copy over a release, add `--build`; `docker compose pull` returns to the release afterwards.

## Running from source

1. Install the .NET SDK that `global.json` names: 10.0.403, or a later 10.0.4xx patch. Building inside Visual Studio
   needs version 18.9 or later; the command line works either way.
2. Install Syft v1.55.0 into the repository's `bin/` folder. The script checks the download against the SHA-256 it
   carries and against the release's checksums file before it unpacks anything.
   - Windows: `./scripts/install-tools.ps1`
   - Linux and macOS: `sh scripts/install-tools.sh`
3. Point `SYFT_BINARY_PATH` at it with a full path, since a relative one starts at the application's folder. From the
   repository's root, in PowerShell `$env:SYFT_BINARY_PATH = "$PWD\bin\syft.exe"`, or in bash
   `export SYFT_BINARY_PATH="$PWD/bin/syft"`. Without it Airlock looks for `syft` on the path, then in `bin/` next to
   the application, which is the build output, not the repository.
4. Store your NVD key, which stays out of the repository:

   ```bash
   dotnet user-secrets set Airlock:Nvd:ApiKey <your-key> --project src/Airlock.Web
   ```

5. Run Airlock. It listens on <http://localhost:8080> in Development:

   ```bash
   dotnet run --project src/Airlock.Web
   ```

Without a key, or without Syft, Airlock starts DEGRADED: it runs, and says why it cannot scan.

## Configuration

Every setting has a default and is checked at startup. A value out of range stops Airlock with a message naming it.
The original's variable names come first and win over the `Airlock__*` spellings; a command-line value,
`--Airlock:Limits:MaxArtifactsPerScan=10` for example, wins over both. Environment variables use `__` where the key
has `:`. Durations are written `hh:mm:ss`, or `d.hh:mm:ss`.

| Setting | Configuration key | Environment variable | Default | Allowed |
|---|---|---|---|---|
| NVD API key | `Airlock:Nvd:ApiKey` | `NVD_API_KEY` | None: Airlock runs DEGRADED | — |
| Syft | `Airlock:Syft:BinaryPath` | `SYFT_BINARY_PATH` | `syft` on the path, then `bin/syft` next to the application | Not blank. A folder means the binary inside it; a relative path starts at the application's folder |
| Console level | `Logging:LogLevel:Default` | `LOG_LEVEL` | `Information` | `TRACE`, `DEBUG`, `INFO`, `WARNING`, `ERROR`, `CRITICAL` or `NONE`. The Live Log is not affected |
| Listening address | `Urls` | `URLS` | `http://localhost:8080`; the image sets `http://+:8080` | — |
| Host names answered | `AllowedHosts` | `AllowedHosts` | `localhost;127.0.0.1;[::1]` | Names separated by `;`. Keep `localhost`: the image's health check uses it |
| Folders a scan may target | `Airlock:AllowedScanRoots:0`, `:1`, … | `Airlock__AllowedScanRoots__0`, … | None: any folder. Compose sets `/uploads` | Full paths |
| Staging root | `Airlock:Staging:Root` | `Airlock__Staging__Root` | `airlock-staging` in the system's temporary folder; the image sets `/var/lib/airlock/staging` | A full path |
| Data-protection keys | `Airlock:DataProtection:KeysPath` | `Airlock__DataProtection__KeysPath` | Where ASP.NET Core keeps them; the image sets `/var/lib/airlock/keys` | A full path |
| Artifacts per scan | `Airlock:Limits:MaxArtifactsPerScan` | `Airlock__Limits__MaxArtifactsPerScan` | 25 | 1 to 1,000 |
| Bytes per scan | `Airlock:Limits:MaxBytesPerScan` | `Airlock__Limits__MaxBytesPerScan` | 2,147,483,648 (2 GiB) | At least 1 MiB |
| Each Syft run | `Airlock:Timeouts:SyftRun` | `Airlock__Timeouts__SyftRun` | `00:15:00` | 1 minute up to `Timeouts:Scan` |
| Each scan | `Airlock:Timeouts:Scan` | `Airlock__Timeouts__Scan` | `01:00:00` | 1 minute to 24 hours |
| Cancellation grace | `Airlock:Timeouts:CancelGracePeriod` | `Airlock__Timeouts__CancelGracePeriod` | `00:00:05` | 0 to 10 seconds |
| How long a finished scan is kept | `Airlock:Retention:Duration` | `Airlock__Retention__Duration` | `1.00:00:00` | 1 minute to 30 days |
| Finished scans kept | `Airlock:Retention:MaxScans` | `Airlock__Retention__MaxScans` | 20 | 1 to 1,000 |
| Packages looked up at once | `Airlock:Correlation:MaxParallelism` | `Airlock__Correlation__MaxParallelism` | 4 | 1 to 16 |
| Failed lookups in a row before NVD counts as down | `Airlock:Correlation:OutageThreshold` | `Airlock__Correlation__OutageThreshold` | 5 | 3 to 20 |
| NVD's address | `Airlock:Nvd:BaseUrl` | `Airlock__Nvd__BaseUrl` | `https://services.nvd.nist.gov/rest/json/cves/2.0` | An absolute `https` URL |
| NVD requests per window | `Airlock:Nvd:RequestsPerWindow` | `Airlock__Nvd__RequestsPerWindow` | 50 | 1 to 1,000 |
| NVD's window | `Airlock:Nvd:Window` | `Airlock__Nvd__Window` | `00:00:30` | 1 second to 1 hour |
| How long Syft's status is kept | `Airlock:Syft:StatusCacheDuration` | `Airlock__Syft__StatusCacheDuration` | `00:00:30` | 1 second to 1 hour |

Under Compose, the five settings in `.env.example` go in `.env`. Any other goes in a `docker-compose.override.yml`
next to `docker-compose.yml`, under `services.airlock.environment`. Compose merges that file on its own, and Git
ignores it.

## Using Airlock

- **Control Center** starts a scan of a directory, `/uploads` by default in the container, or of the artifacts you
  pick. **Execute Security Audit** is enabled when the status pill says ONLINE: DEGRADED lists what is missing, and
  OFFLINE means the page lost its connection to the server. While a scan runs you see its phase, its progress and
  the elapsed time.
- **Active Logs** shows the Live Log: Airlock's activity, for every scan, as it happens. You can export it, and
  **Emergency Stop** cancels the running scan, whoever started it.
- **Reports** shows a finished scan: total, clean, Critical and High counts, a banner when packages are Unverified,
  and the inventory, which you can filter by package or CVE. Pick a row for its findings. **Export JSON** and
  **Export PDF** download the report; **Rescan** runs the same target again. A report's address keeps working while
  the scan is retained.

Airlock runs one scan at a time. Another start is refused until it ends.

## The API

The OpenAPI document and an interactive reference are at <http://localhost:8080/docs>. A session with `curl`:

```bash
curl -F targetPath=/uploads http://localhost:8080/api/v2/scans
```

That answers 202 with the scan's id, and its address in `Location`. Upload files instead with
`-F artifacts=@requirements.txt`, once per file. Then follow the scan's events, and fetch it and its report:

```bash
curl -N http://localhost:8080/api/v2/scans/<id>/events
```

```bash
curl http://localhost:8080/api/v2/scans/<id>
```

```bash
curl -o report.pdf http://localhost:8080/api/v2/scans/<id>/report.pdf
```

Errors are RFC 9457 problems; each type is described in `docs/api/problems.md`.

## What Syft finds

From Syft v1.55.0, checked against one sample of each kind:
- Manifests and lock files, such as `requirements.txt` and `package-lock.json`, give their packages. A folder with
  several is one run.
- `.jar` files give their Maven packages. `.zip` and `.tar.gz` archives are unpacked and give what is inside.
- A `docker save` tarball is read as an image: its distribution and its OS packages. NVD may not know a distribution's
  backported fixes, so such a scan says so.
- Go binaries give their modules, and .NET assemblies their names and versions.
- `.whl` and `.nupkg` files give nothing to Syft as they are, so Airlock stages them as `.zip` files, and reads each
  `.nuspec` so that the NuGet package itself is reported.
- A package with no version cannot be looked up: it is Unverified, with that reason.

## Security

- **There is no authentication.** Airlock is a single-user tool, as the original was. That is an accepted risk: anyone
  who can reach it can start and stop scans and read the reports.
- **It answers on this machine only**: `localhost` from source, `127.0.0.1` under Compose. Opening it to a network
  means exposing an unauthenticated service, so put it behind a reverse proxy that authenticates, and add the proxy's
  host name to `AllowedHosts`.
- **The proxy must carry WebSockets**, which the UI needs, and terminate TLS. For the API's event streams it must not
  buffer: send `X-Accel-Buffering: no`, or turn buffering off.
- **Cross-site requests are refused**: a page on another site cannot start or stop a scan from your browser
  (`docs/adr/0007-cross-site-and-host-checks.md`).
- **One Airlock per staging root.** A second one on the same root stops at startup.
- **The container is hardened**: a read-only root filesystem, every Linux capability dropped, `no-new-privileges`, an
  init process, and the non-root user `app` (UID 1654). It can write only to its volume, which holds the staging root
  and the data-protection keys, and to a 64 MiB `/tmp` in memory. The folder it scans is mounted read-only. Leave about
  three times your largest upload free for the volume.

## Troubleshooting

- `docker compose logs` shows Airlock's log, one line per entry, in UTC.
- **Warnings at start.** A clean start logs only these:
  - `Microsoft.AspNetCore.DataProtection.KeyManagement.XmlKeyManager[35]`, "No XML encryptor configured": ASP.NET Core
    created a data-protection key and stored it unencrypted. It does so at the first start on a new volume, and when it
    renews the key every 90 days. The keys protect only Airlock's antiforgery tokens and the UI's connections, and their
    folder on the volume is readable by the container's user alone, so the warning needs no action.
- **Without Compose**, give the container a volume on `/var/lib/airlock`, for example
  `docker run -p 127.0.0.1:8080:8080 -v airlock-data:/var/lib/airlock ghcr.io/jjwren/airlock-dotnet`. Without one,
  ASP.NET Core warns `Microsoft.AspNetCore.DataProtection.Repositories.FileSystemXmlRepository[60]` that its keys will
  not outlive the container, and uploads fill the container's own layer. Compose's hardening is not applied either.
- `/api/v2/system/health` and <http://localhost:8080/docs> answer as long as Airlock runs. DEGRADED names what is
  missing.
- The image has no shell. Run Syft itself with `docker compose exec airlock /app/bin/syft version`, or
  `docker compose exec airlock /app/bin/syft dir:/uploads` to see what it finds.
- After `docker compose up --build`, `docker compose pull` returns to the published release.

## Releasing

The owner cuts a release:

1. On a branch, set `Version` in `Directory.Build.props` to the new version, and merge it to `main`.
2. Tag the merged commit `vX.Y.Z`, or `vX.Y.Z-suffix` for a pre-release.
3. Push the tag.

`release.yml` then checks that the tag equals `Version` and that the version is not published yet. It runs CI's lint
and test jobs, builds the image, smoke-tests and scans it, and pushes it with no tag. It stores two attestations, the
build provenance and the SBOM, and verifies both. Only then does it tag the image `X.Y.Z`, and `latest` when there is
no suffix. A published version never changes; a fix gets a new version.

GitHub creates the package at the first release. Whether anyone may pull it without signing in is the package's
visibility, which only the owner can set in GitHub. Attestations need a public repository, or GitHub Enterprise Cloud
for a private one.

Anyone can check a published image with the GitHub CLI:

```bash
gh attestation verify oci://ghcr.io/jjwren/airlock-dotnet:2.0.0 --repo JJWren/Airlock-DotNet --signer-workflow JJWren/Airlock-DotNet/.github/workflows/release.yml
```

```bash
gh attestation verify oci://ghcr.io/jjwren/airlock-dotnet:2.0.0 --repo JJWren/Airlock-DotNet --predicate-type https://spdx.dev/Document/v2.3
```

## Development

- `dotnet build`, with warnings as errors, then `dotnet test`. Set `SYFT_BINARY_PATH` so that Syft's integration tests
  run instead of skipping. Each test project writes its results to `TestResults/<project>/`.
- Coverage: run `dotnet test --coverage --coverage-output-format cobertura`, then `dotnet run scripts/ci/coverage.cs`.
  It checks each project against its target, from its own tests' report.
- `tests/Airlock.Architecture.Tests` keeps the layers apart. `tests/Airlock.Delivery.Tests` checks the delivery files:
  the pins, the workflows, Compose, the Dockerfile, the notices and this README.
- The decisions are in `docs/adr/`, the terms in `GLOSSARY.md`, and the design documents in `aidlc-docs/`.
- **Updating a package or the SDK** changes its line in `THIRD-PARTY-NOTICES.md`, after its licence is checked; the
  notices test names the line. Syft changes in one pull request: the install scripts with their checksums, the
  `Dockerfile` and this README.
- **The image locally**, with Docker, bash and jq, as CI runs it:

  ```bash
  bash scripts/ci/build-image.sh load
  ```

  ```bash
  dotnet run scripts/ci/smoke-test.cs -- --image airlock-dotnet:smoke
  ```

  ```bash
  bash scripts/ci/scan-image.sh
  ```

  The smoke test reads no `.env`, and uses port 18080 and the Compose project `airlock-smoke`.
- **The image scan** fails on a High or Critical vulnerability that has a fix. To accept one while waiting for that fix,
  add it to `.grype.yaml` with its package and a reason that starts with a review date, as the entries there do. CI
  fails again once the date has passed. The ones accepted today are in Syft v1.55.0's own binary, built with Go 1.26.8;
  they go when a Syft built with a newer Go replaces it.

## Licensing and credits

Airlock is under the MIT License (`LICENSE`).

- PDF reports are made with QuestPDF, used under the QuestPDF Community License. QuestPDF's own summary says it is
  free for individuals and businesses with annual gross revenue under USD 1,000,000, and for charitable organisations,
  academic institutions and open-source projects, but not for public-sector entities or publicly traded companies. Its
  terms, in `licenses/questpdf/LICENSE.md`, decide who needs a paid licence.
- The UI is built with MudBlazor, under the MIT License.
- The fonts Inter and JetBrains Mono are under the SIL Open Font License 1.1.
- Syft is under the Apache License 2.0.
- Everything else the image redistributes is listed, with its licence, in `THIRD-PARTY-NOTICES.md`.

This product uses the NVD API but is not endorsed or certified by the NVD.

This is a rebuild of the original [Airlock](https://github.com/JJWren/Airlock), written for the Spring 2026 Arizona
Cybersecurity Clinic. The rebuild lives at <https://github.com/JJWren/Airlock-DotNet>.
