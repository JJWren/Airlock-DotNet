# U3 Infrastructure — Domain Entities (Infrastructure types)

**Stage**: CONSTRUCTION — U3 Infrastructure — Functional Design (Comprehensive depth)

The Infrastructure adds no business entities; it turns the outside world into the Domain's values behind U2's ports.
This document defines what it reads from Syft and NVD, how it stores uploads, its options and its Live Log buffers.
Rules are IR-nn in `business-rules.md`.

## Syft JSON, as read (Syft v1.55.0, schema 16.1.11)

| Field | Use |
|---|---|
| `source.type` | `image` sets `SbomProduced.IsContainerImage` (IR-03); `directory` and `file` do not |
| `artifacts[].type` | `Package.Type` (`python`, `npm`, `go-module`, `java-archive`, `apk`, `binary`, `dotnet`, …) |
| `artifacts[].name`, `artifacts[].version` | `Package.Name`, `Package.Version` as Syft reports them; an empty version stays empty |
| `artifacts[].cpes[]` | Candidate CPEs in Syft's order: `cpe` is the value; `source` `nvd-cpe-dictionary` is `Dictionary`, anything else `Generated` |
| everything else | Ignored (`purl`, `locations`, `licenses`, `metadata`, `distro`, `descriptor`) |

## NVD CVE API 2.0, as read

| Field | Use |
|---|---|
| `totalResults`, `resultsPerPage`, `startIndex` | Paging (IR-07) |
| `vulnerabilities[].cve.id` | `Cve.Id` |
| `vulnerabilities[].cve.vulnStatus` | `Rejected` records are dropped (decision 2) |
| `descriptions[]` | The first one whose `lang` is `en` becomes `Cve.Description` |
| `metrics.cvssMetricV31[]`, `cvssMetricV30[]`, `cvssMetricV40[]`, `cvssMetricV2[]` | One `CvssScore` per entry: `cvssData.baseScore`; severity from `cvssData.baseSeverity` (v3.x, v4.0) or the entry's `baseSeverity` (v2), else derived from the score (BR-03); `type` `Primary` is NVD's own score, anything else `Secondary` |
| other metric keys (`ssvcV203`, …) | Ignored (S0 N7) |
| `configurations[].nodes[].cpeMatch[]` | Each `criteria` with `vulnerable: true` adds its "vendor:product" to `Cve.AffectedProducts`, once |

## Staging layout (decision 6)

```text
<StagingOptions.Root>/            default: <system temp>/airlock-staging
  <scan id>/                       one scan's IStagingArea; deleted on dispose
    .airlock                       marker file: a folder Airlock made (IR-11, IR-13)
    manifests/                     IStagingArea.ManifestsDirectory, scanned in one dir: run
      0/requirements.txt           each manifest in its own numbered folder, under its own name
      3/package-lock.json
    artifacts/
      1/app-image.tar              every other artifact in its own numbered folder
      2/PyYAML-5.3-cp38-cp38-win_amd64.whl.zip   wheels and NuGet packages get a .zip suffix on disk (FR-05)
  <run id>/                        one Syft run's temporary folder, also marked; deleted after the run (IR-01)
```

The number is the file's position in the upload. The `Artifact` keeps the uploaded (sanitized) name; only the file on
disk carries the `.zip` suffix.

## Options

| Option | Default | Original variable | Rule |
|---|---|---|---|
| `Airlock:Nvd:ApiKey` | none — Airlock starts DEGRADED | `NVD_API_KEY` | IR-22, IR-23 |
| `Airlock:Nvd:BaseUrl` | `https://services.nvd.nist.gov/rest/json/cves/2.0` | — | IR-07 |
| `Airlock:Nvd:RequestsPerWindow`, `Airlock:Nvd:Window` | 50 per 30 seconds | — | IR-07 |
| `Airlock:Syft:BinaryPath` | `syft` on the path, then `./bin` | `SYFT_BINARY_PATH` | IR-06 |
| `Airlock:Syft:StatusCacheDuration` | 30 seconds (U3 NFR Design) | — | IR-05 |
| `Airlock:Staging:Root` | `<system temp>/airlock-staging` | — | IR-11 |
| `Logging:LogLevel:Default` | `Information` | `LOG_LEVEL` | IR-18 (console only) |

## Live Log buffers (Q-U3-1)

| Buffer | Size | Behaviour |
|---|---|---|
| Replay ring | the last 200 entries | Every new subscriber receives them first, oldest first |
| Each subscriber | 1,000 entries | When full, the oldest entry is dropped; publishing never waits |

## PDF Scan Report (FR-21, Q-U3-2)

Sections, in order: title and scan details; summary; findings; Unverified packages; warnings; a footer on every page.
The layout is in `business-logic-model.md`.
