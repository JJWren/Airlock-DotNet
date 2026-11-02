# Unit Test Instructions

**Stage**: CONSTRUCTION — Build and Test (Standard depth)
**Plan**: `construction/plans/build-and-test-plan.md` (approved 2026-10-30T16:42:36Z)
**Covers**: every test project, filters, coverage and its checker, the measurements the performance tests write, and the
repository tests

## The test projects

All use xunit.v3 on Microsoft.Testing.Platform, the runner `global.json` names. Each project writes its results to
`TestResults/<project>/` (`tests/Directory.Build.props`).

| Project | What it holds |
|---|---|
| `Airlock.Domain.Tests` | The domain model (U1), and U1-NFR-05's performance smoke test |
| `Airlock.Application.Tests` | The use cases, the scan pipeline, correlation, the outage breaker, options (U2), and U2-NFR-10's tests |
| `Airlock.Infrastructure.Tests` | Syft's runner and parser, the NVD client and its pacer, staging, the PDF renderer, the registry and the Live Log (U3); Syft's integration tests; the live NVD test; U3-NFR-05 and U3-NFR-10's tests |
| `Airlock.Web.Tests` | The `/api/v2` endpoints through `WebApplicationFactory` (U4), the pages and parts with bUnit (U5), the stylesheet checks, and U4-NFR-08 and U5-NFR-04's tests |
| `Airlock.Architecture.Tests` | The layers' rules, with ArchUnitNET |
| `Airlock.Delivery.Tests` | The delivery files (U6): pins, workflows, Compose, the Dockerfile, the notices and the README |
| `Airlock.Infrastructure.StandIn` | Not a test project: a program that plays Syft for the Infrastructure's process tests |

## Running them

Every test:

```bash
dotnet test
```

One project, one class, or the tests whose names match:

```bash
dotnet test --project tests/Airlock.Web.Tests
```

```bash
dotnet test --project tests/Airlock.Web.Tests -- --filter-class "Airlock.Web.Tests.Ui.TerminalTests"
```

```bash
dotnet test --project tests/Airlock.Infrastructure.Tests -- --filter-method "*A_CVE_id_is_drawn_whole*"
```

`--filter-trait "Category=Integration"` and `--filter-not-trait` select by trait. `--report-xunit-trx` adds a TRX file,
which keeps each test's output.

## Tests that need something

| Tests | Run when | Otherwise |
|---|---|---|
| Syft's integration tests (`Category=Integration`) | `SYFT_BINARY_PATH` names Syft v1.55.0, with a full path | Skipped, saying how to install it |
| The live NVD test (`Category=Network`) | `AIRLOCK_LIVE_NVD=1`: one request to the real NVD, with no key, for pyyaml 5.3's two CVEs | Skipped |
| Windows' file rules (drive letters, files held open) | On Windows | Skipped on Linux |
| The temporary folder's access rules | On Linux | Skipped on Windows |
| Symbolic links | The account may create them (on Windows, Developer Mode), or junctions | Skipped |

So a full run on Windows with `SYFT_BINARY_PATH` set skips three tests, and the same on Linux skips the Windows-only
ones instead.

## Coverage

```bash
dotnet test --coverage --coverage-output-format cobertura
```

```bash
dotnet run scripts/ci/coverage.cs
```

The checker reads each project's coverage from its own test project's Cobertura report, and fails below its target:

| Project | Lines | Branches |
|---|---|---|
| `Airlock.Domain` | 95 % | 90 % |
| `Airlock.Application` | 90 % | 85 % |
| `Airlock.Infrastructure` | 85 % | 80 % |
| `Airlock.Web` | 90 % | 85 % |

It writes `TestResults/coverage.json`, which `scripts/ci/summary.cs` reads for CI's job summary.

## The measurements

The performance smoke tests assert bounds ten times wider than their targets, and write what they measured, one line
each starting `measured <requirement>:`, to the test output. With `--report-xunit-trx` the TRX keeps those lines:

```bash
dotnet test --report-xunit-trx -- --filter-class "*PerformanceSmokeTests" --filter-class "*LargeResultsSmokeTests"
```

`performance-test-instructions.md` lists every target, and how the ones that need a browser or a real NVD are measured.

## The repository tests

`Airlock.Delivery.Tests` read the delivery files as text: the image digests, action SHAs and Syft's checksums agree
everywhere they appear; the workflows keep their least privileges; Compose keeps its hardening; the Dockerfile keeps
its stages, user and health check, and asks for the framework's scripts; every package and image has its line in
`THIRD-PARTY-NOTICES.md`; the README names what the code does. A test that fails names the rule and the file.

## In CI

CI's test job runs on Ubuntu and on Windows: it installs Syft with the install script, builds, runs every test with
coverage and a TRX, checks coverage against the targets, and writes the job summary.
