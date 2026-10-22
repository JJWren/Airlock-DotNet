# U1 Domain — Domain Entities

**Stage**: CONSTRUCTION — U1 Domain — Functional Design (Standard depth)

## Conventions

- Every type is immutable. Collections are exposed as read-only lists and copied on construction.
- Constructors and factories validate their input and throw `ArgumentException` (or a subclass) on invalid data —
  these are programming errors, not user refusals.
- A `Scan` transition that the state machine does not allow throws `InvalidOperationException`.
- No I/O, no clock and no dependencies beyond the base class library: times are passed in.
- Names follow GLOSSARY.md. Rule IDs (BR-nn) refer to `business-rules.md`.

## Identity and lifecycle

### `ScanId` — value (`readonly record struct`)
| Member | Type | Rules |
|---|---|---|
| `Value` | `Guid` | Never `Guid.Empty`. |
| `New()` | factory | A fresh random identifier. |
| `ToString()` | | The GUID in its standard "D" format, as used in URLs. |

### `ScanStatus` — enum
`Running`, `Completed`, `Incomplete`, `Failed`, `Cancelled`. Every value except `Running` is final.

### `ScanPhase` — enum
`SbomGeneration` → `Correlation` → `Summary`, in that order.
*Refinement:* the earlier `Staging` phase is dropped. Uploads are fully staged before the scan exists (the reservation
step in U2), and "receiving upload" is reported through the active-scan state instead.

### `Scan` — aggregate (immutable record)
| Member | Type | Rules |
|---|---|---|
| `Id` | `ScanId` | Set by `Start`. |
| `Target` | `ScanTarget` | Set by `Start`. |
| `Status` | `ScanStatus` | `Running` until a final transition (BR-14). |
| `Phase` | `ScanPhase` | Only moves forward (BR-14). |
| `Progress` | `ScanProgress?` | Present once correlation reports progress. |
| `StartedAt` | `DateTimeOffset` | UTC; set by `Start`. |
| `FinishedAt` | `DateTimeOffset?` | UTC; set by a final transition; never earlier than `StartedAt`. |
| `Summary` | `ScanSummary?` | Set only by `Finish`. |
| `VulnerablePackages` | list of `VulnerablePackage` | Empty until `Finish`; ordered by highest severity, then package name. |
| `UnverifiedPackages` | list of `UnverifiedPackage` | Empty until `Finish`; ordered by package name. |
| `Warnings` | list of `ScanWarning` | Appended while running; kept afterwards. |
| `Failure` | `ScanFailure?` | Present only when `Failed`. |
| `CancellationReason` | `string?` | Present only when `Cancelled`. |
| `IsFinished` | `bool` | `Status != Running`. |
| `Duration` | `TimeSpan?` | `FinishedAt − StartedAt` when finished (BR-15). |

Transitions: `Start`, `EnterPhase`, `RecordProgress`, `AddWarning`, `Finish`, `Fail`, `Cancel` (BR-14).

### `ScanProgress` — value
`Correlated` and `Total`, with 0 ≤ `Correlated` ≤ `Total`.

### `ScanWarning` — value
| Member | Type | Rules |
|---|---|---|
| `Code` | `ScanWarningCode` | `NoPackagesIdentified`, `DistributionBackportsNotConsidered`, `CvesWithoutSeverity`; `NotProductMatched`, added by U2 question Q-U2-3 and implemented in U2 Code Generation. |
| `Message` | `string` | Non-empty; the text shown to people. |
| `Input` | `string?` | The artifact or package the warning is about, when there is one. |

### `ScanFailure` — value
| Member | Type | Rules |
|---|---|---|
| `Code` | `ScanFailureCode` | `SyftFailed`, `SyftTimedOut`, `SyftOutputUnreadable`, `ScanTimedOut`, `Unexpected`. |
| `Reason` | `string` | Non-empty; safe to show (no stack traces). |
| `Input` | `string?` | The directory or artifact involved, when known. |

## What gets scanned

### `ScanTarget` — value (closed hierarchy)
- `DirectoryTarget(Path)` — a non-empty, fully qualified path (validated against the host in U2).
- `ArtifactsTarget(Artifacts)` — a non-empty list of `Artifact`.

### `Artifact` — value
| Member | Type | Rules |
|---|---|---|
| `FileName` | `string` | Non-empty, already sanitized: no directory separators, no `..`. |
| `SizeBytes` | `long` | ≥ 0. |
| `Kind` | `ArtifactKind` | `Manifest` or `Other`, decided by `ManifestCatalog` (BR-12). |

### `Package` — value
| Member | Type | Rules |
|---|---|---|
| `Type` | `string` | Non-empty; Syft's package type, e.g. `python`, `npm`, `java-archive`, `go-module`, `apk`, `dotnet`, `nuget`, `binary`. |
| `Name` | `string` | Non-empty, as Syft reports it. |
| `Version` | `string` | As Syft reports it; may be empty or `UNKNOWN` (BR-10). |

Identity (BR-11): two packages are the same when `Type` matches exactly, `Name` matches ignoring case, and `Version`
matches exactly. Equality and hashing follow that identity.

### `PackageVersion` — rule (static)
- `IsKnown(Package)` — false for an empty or whitespace version or `UNKNOWN` in any case (BR-10).
- `ForNvd(Package)` — the version to send to NVD (BR-10).

### `ManifestCatalog` — rule (static)
- `IsManifest(string fileName)` — whether the name is a known text dependency declaration (BR-12).

## Vulnerability intelligence

### `CandidateCpe` — value
| Member | Type | Rules |
|---|---|---|
| `Value` | `string` | Starts with `cpe:2.3:`; otherwise kept exactly as Syft reports it (NVD answers malformed ones with "no records", S0 N2). |
| `Source` | `CpeSource` | `Dictionary` (Syft's `nvd-cpe-dictionary`) or `Generated` (`syft-generated`). |
| `Vendor`, `Product` | `string` | The 4th and 5th components, split on unescaped colons; empty when absent. |
| `Prioritize(Package, candidates, max = 3)` | factory | Ordered, de-duplicated, capped list (BR-08). |

### `Severity` — enum
`None` < `Low` < `Medium` < `High` < `Critical`. `SeverityScale.FromScore(CvssVersion, decimal)` derives a severity
when NVD gives none (BR-03).

### `CvssScore` — value
| Member | Type | Rules |
|---|---|---|
| `BaseScore` | `decimal` | 0.0–10.0, one decimal place. |
| `Severity` | `Severity` | From NVD, or derived (BR-03). |
| `Version` | `CvssVersion` | `V20`, `V30`, `V31`, `V40` (no underscores: analyzer rule CA1707). |
| `Source` | `CvssSource` | `Primary` (NVD's own score) or `Secondary` (a CNA's or other provider's). |
| `SelectPreferred(scores)` | rule | The score Airlock uses (BR-01, BR-02); null when there is none. |

### `Cve` — value
| Member | Type | Rules |
|---|---|---|
| `Id` | `string` | Matches `CVE-YYYY-N…`. |
| `Description` | `string` | NVD's English description; "No description available." when absent. |
| `NvdUrl` | `Uri` | `https://nvd.nist.gov/vuln/detail/{Id}` (BR-17). |
| `Scores` | list of `CvssScore` | Every score NVD published; may be empty. |
| `AffectedProducts` | list of `string` | `vendor:product` pairs from the CVE's configurations. |
| `PreferredScore` | `CvssScore?` | `CvssScore.SelectPreferred(Scores)`. |

### `ProductName` — rule (static)
- `Normalize(string)` — lower case; `-`, `_` and `.` compared alike where noted (BR-08).
- `ForProductMatch(Package)` — the product to ask NVD about, or null when the name is too generic (BR-09).

## Results

### `MatchMethod` — value (closed hierarchy)
- `CpeMatch(CandidateCpe Cpe)` — linked through one of the package's candidate CPEs.
- `ProductMatch(string Product)` — linked by product name from any vendor (ADR-0006). *Refinement:* it now carries the
  product that was matched, so reports can say which name was used.

### `Finding` — value
`Cve` and `Score` (the CVE's preferred score), where `Score.Severity` is at or above the threshold (BR-04).

### `FindingSelection` — value, built by `Finding.Select(IEnumerable<Cve>)`
- `Findings` — distinct by CVE id and ordered (BR-07).
- `UnscoredCveIds` — CVEs with no score at all (BR-05).
- CVEs below the threshold are dropped.

### `VulnerablePackage` — value
`Package`, `Method` (`MatchMethod`), `Findings` (non-empty, BR-07). `HighestSeverity` is the highest finding severity
(BR-06).

### `UnverifiedPackage` — value
| Member | Type | Rules |
|---|---|---|
| `Package` | `Package` | |
| `Reason` | `UnverifiedReason` | `LookupFailed` or `VersionUnknown` (BR-18). |
| `Detail` | `string` | Non-empty; for `LookupFailed`, the last failure reason. |

### `ScanSummary` — value, built only by `ScanSummary.From(...)`
`TotalPackages`, `VulnerablePackages`, `CleanPackages`, `UnverifiedPackages`, `CriticalFindings`, `HighFindings`, all
≥ 0, with the invariants of BR-13.

### `SeverityThreshold` — rule (static)
`Minimum` is `High`; `Admits(Severity)` is true for `High` and `Critical` (BR-04).
