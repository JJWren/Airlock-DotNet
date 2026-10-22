# U1 Domain — Logical Components

**Stage**: CONSTRUCTION — U1 Domain — NFR Design (Minimal depth)

## Namespaces in `Airlock.Domain`

Folders and namespaces follow the glossary's sections.

| Namespace | Types |
|---|---|
| `Airlock.Domain.Scanning` | `Scan`, `ScanId`, `ScanStatus`, `ScanPhase`, `ScanProgress`, `ScanWarning`, `ScanWarningCode`, `ScanFailure`, `ScanFailureCode` |
| `Airlock.Domain.Targets` | `ScanTarget`, `DirectoryTarget`, `ArtifactsTarget`, `Artifact`, `ArtifactKind`, `ManifestCatalog`, `Package`, `PackageVersion` |
| `Airlock.Domain.Intelligence` | `Cve`, `CvssScore`, `CvssVersion`, `CvssSource`, `Severity`, `SeverityScale`, `SeverityThreshold`, `CandidateCpe`, `CpeSource`, `ProductName` |
| `Airlock.Domain.Results` | `MatchMethod`, `CpeMatch`, `ProductMatch`, `Finding`, `FindingSelection`, `VulnerablePackage`, `UnverifiedPackage`, `UnverifiedReason`, `ScanSummary` |
| `Airlock.Domain.Text` | `DomainLimits` (public constants), `DomainText` (internal guard) |

## Cross-cutting pieces

- **`DomainLimits`** — public constants: `PackageType` 64, `PackageName` 512, `PackageVersion` 256, `Cpe` 1,024,
  `FileName` 255, `CveDescription` 16,384. U3 imports them to clean incoming data.
- **`DomainText`** — internal; the single text guard used by every constructor (U1-NFR-04).
- **`InternalsVisibleTo("Airlock.Domain.Tests")`** — lets the tests exercise `DomainText` directly.

## Checks introduced in U1

| Check | Project | Enforces |
|---|---|---|
| Architecture rule 1 | `Airlock.Architecture.Tests` | Domain depends on no other Airlock assembly and no `Microsoft.*` namespace (U1-NFR-01) |
| Banned calls and types | `Airlock.Architecture.Tests` | No clock, environment, file, network, process or console use in Domain (U1-NFR-03), through ArchUnitNET's call and dependency conditions |
| No public setters | `Airlock.Domain.Tests` | Reflection over the exported types: no public setter, `init` included (U1-NFR-02) |
| Culture independence | `Airlock.Domain.Tests` | Name-normalization tests run under `tr-TR` as well as the invariant culture (U1-NFR-03) |
| Text guard | `Airlock.Domain.Tests` | Each limit, each control-character class and each bidirectional character is rejected (U1-NFR-04) |
| Rule tables | `Airlock.Domain.Tests` | One test class per rule (rule-to-test map) |

Architecture rules 2–6 are added by the units that create the projects they govern.
