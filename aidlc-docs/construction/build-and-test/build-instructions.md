# Build Instructions

**Stage**: CONSTRUCTION — Build and Test (Standard depth)
**Plan**: `construction/plans/build-and-test-plan.md` (approved 2026-10-30T16:42:36Z)
**Covers**: the solution (U1 to U5), Syft, running from source, the image with Compose and as CI builds it (U6), on
Windows and Linux

## Prerequisites

| Tool | Version | Pinned in | Needed for |
|---|---|---|---|
| .NET SDK | 10.0.403, rolling forward to the latest patch | `global.json` | Building, the tests, the C# scripts in `scripts/ci/` |
| Syft | v1.55.0 | The install scripts' SHA-256 values, the `Dockerfile`, the README | Scans, and Syft's integration tests |
| Docker with Compose | Build and Test used Docker Engine 29 with Compose v5 | — | The image and `docker-compose.yml` |
| bash and jq | Any recent | — | The image scripts, as CI's Linux runner runs them; on Windows, in WSL |

## 1. The solution

```bash
dotnet build
```

```bash
dotnet build --configuration Release
```

Warnings are errors (`Directory.Build.props`), so a build that warns fails. Central Package Management keeps every
package version in `Directory.Packages.props`.

## 2. Syft

Each script downloads Syft v1.55.0's release archive for this system from github.com/anchore/syft, checks it against
the SHA-256 it carries, and puts the binary in the repository's `bin/` folder. It does nothing when `bin/` already holds
v1.55.0, and installs nothing when the checksum differs.

- Windows, in PowerShell:

  ```powershell
  ./scripts/install-tools.ps1
  ```

- Linux and macOS:

  ```bash
  sh scripts/install-tools.sh
  ```

A corrupted archive stops the script with `install-tools: the checksum of syft_1.55.0_<os>_<arch>.<ext> does not match
the one this script carries` and exit code 1.

## 3. Running from source

As the README's "Running from source" says:

1. Install Syft (section 2), then point `SYFT_BINARY_PATH` at it with a full path, since a relative one starts at the
   application's folder:
   - PowerShell: `$env:SYFT_BINARY_PATH = "$PWD\bin\syft.exe"`
   - bash: `export SYFT_BINARY_PATH="$PWD/bin/syft"`
2. Store the NVD key in user-secrets:

   ```bash
   dotnet user-secrets set Airlock:Nvd:ApiKey <your-key> --project src/Airlock.Web
   ```

3. Run it, then open `http://localhost:8080`:

   ```bash
   dotnet run --project src/Airlock.Web
   ```

Without the key or without Syft, Airlock starts DEGRADED and says why. Two Airlocks cannot share a staging root: the
second stops at start with "Another Airlock is using the staging root". To run a second one, give it its own root and
port after `--`, for example `-- --urls=http://localhost:8082 --Airlock:Staging:Root=<a folder>`.

## 4. The image with Compose

1. Copy `.env.example` to `.env`, then set `SCAN_UPLOAD_PATH` to the host folder Airlock may scan, which it sees as
   `/uploads`, and `NVD_API_KEY` to your key.
2. Build this copy and start it:

   ```bash
   docker compose up -d --build
   ```

   `docker compose up` alone runs the release named by `AIRLOCK_IMAGE_TAG` (`latest` by default), and builds this copy
   instead when that image cannot be pulled, as before the first release.
3. Open `http://localhost:8080`, or the port in `AIRLOCK_PORT`.

A variable given in the shell takes precedence over the same one in `.env`, which is how to change `AIRLOCK_PORT`,
`SCAN_UPLOAD_PATH` or `LOG_LEVEL` for one run without editing the file.

## 5. The image as CI builds it

CI's image job, with Docker, bash and jq (on Windows, in WSL):

```bash
bash scripts/ci/build-image.sh load
```

```bash
dotnet run scripts/ci/smoke-test.cs -- --image airlock-dotnet:smoke
```

```bash
bash scripts/ci/scan-image.sh
```

`build-image.sh load` builds with a BuildKit builder of its own, loads the image as `airlock-dotnet:smoke`, writes an OCI
archive, and prints the configuration digest. `scan-image.sh` builds the image's SBOM with Syft and fails on a fixable
High or Critical vulnerability that `.grype.yaml` does not accept. `dotnet run scripts/ci/summary.cs -- --image <work
folder>` writes the job summary.

## 6. The C# scripts

`scripts/ci/smoke-test.cs`, `coverage.cs` and `summary.cs` are .NET 10 file-based programs, run with `dotnet run
<file> [-- <arguments>]` from the repository's root. They build like the solution, warnings as errors.

## Notes for Windows

- Git Bash rewrites arguments that look like POSIX paths, so `curl -F "targetPath=/uploads" …` sends a Windows path.
  Put `MSYS_NO_PATHCONV=1` before such a command.
- A port Airlock needs may be taken: in Build and Test, `127.0.0.1:8081` belonged to WSL's relay for a service inside
  WSL. Choose another with `AIRLOCK_PORT`.

## Found while building in Build and Test

- **`blazor.web.js` answered 404 in the image** (defect 1 of the summary). The Dockerfile restores before it copies
  `src/`, and the .NET SDK adds the package that carries the framework's scripts only once it sees a `.razor` file, so
  the image's page never became interactive. `Airlock.Web.csproj` now sets `RequiresAspNetWebAssets` to `true`; a
  Dockerfile test keeps the property, and the smoke test checks that every script on the page answers.
