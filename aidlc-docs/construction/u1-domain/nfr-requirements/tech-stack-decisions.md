# U1 Domain — Tech Stack Decisions

**Stage**: CONSTRUCTION — U1 Domain — NFR Requirements (Minimal depth)

U1 also creates the solution scaffold, so the solution-wide settings are decided here. Exact package versions are
resolved to the latest stable releases at Code Generation and recorded in `Directory.Packages.props`.

## Solution-wide (created in U1)

| Item | Decision |
|---|---|
| SDK | .NET SDK 10.0.303 pinned in `global.json`, `rollForward: latestFeature` |
| Language | C# 14 (the `net10.0` default) |
| Solution file | `Airlock.slnx` |
| `Directory.Build.props` | `net10.0`; `Nullable` enable; `ImplicitUsings` enable; `TreatWarningsAsErrors` true; `AnalysisLevel` latest-recommended; `EnforceCodeStyleInBuild` true; `Deterministic` true |
| Packages | Central Package Management (`Directory.Packages.props`, `ManagePackageVersionsCentrally`) |
| Style | `.editorconfig` with the standard .NET conventions (file-scoped namespaces, `var` where the type is evident, ordinal string comparison analyzers kept on) |
| Test platform | xUnit v3 on Microsoft Testing Platform, selected in `global.json` so `dotnet test` runs it natively |
| Coverage | `Microsoft.Testing.Extensions.CodeCoverage`, Cobertura output for CI |

## Airlock.Domain

| Item | Decision |
|---|---|
| References | None (U1-NFR-01) |
| Records and equality | `sealed record` types; `Package` overrides equality for its identity rule (BR-11) |
| `ScanId` | `Guid.CreateVersion7()` — time-ordered identifiers that sort by creation; they reveal the creation time to the millisecond, which the scan already shows |
| Rules | `static` classes with pure methods (`CvssScore.SelectPreferred`, `CandidateCpe.Prioritize`, `ProductName`, `PackageVersion`, `ManifestCatalog`, `SeverityScale`, `SeverityThreshold`) |
| Collections | `IReadOnlyList<T>` backed by arrays created on construction |

## Test projects created in U1

| Project | Packages |
|---|---|
| `Airlock.Domain.Tests` | `xunit.v3`, `Microsoft.Testing.Extensions.CodeCoverage` |
| `Airlock.Architecture.Tests` | `xunit.v3`, `TngTech.ArchUnitNET.xUnitV3` |

No property-based testing library: xUnit theories driven by the worked-example tables cover the rules.
