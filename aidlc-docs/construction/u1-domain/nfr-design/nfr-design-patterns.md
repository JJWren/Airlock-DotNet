# U1 Domain — NFR Design Patterns

**Stage**: CONSTRUCTION — U1 Domain — NFR Design (Minimal depth)

| NFR | Pattern | How it is applied |
|---|---|---|
| U1-NFR-01 Dependencies | Package-free project, guarded by tests | `Airlock.Domain.csproj` has no `PackageReference` or `ProjectReference`. Architecture rule 1 fails the build's tests if any Domain type depends on another Airlock assembly or on `Microsoft.*`. |
| U1-NFR-02 Immutability | Records without public setters; factories and transitions | Every Domain property is `{ get; }` or `{ get; private init; }` — **no public `init`**, because a public `init` setter lets any caller write `scan with { Status = ... }` and skip validation. Validated types have private constructors and are created through static factories (`Scan.Start`, `ScanSummary.From`, `CandidateCpe.Prioritize`, `Finding.Select`); `Scan` transitions use `this with { ... }` internally after checking BR-14. List members are `ImmutableArray<T>` (shipped with .NET, no package) exposed as `IReadOnlyList<T>`, so callers cannot cast back to a mutable array. |
| U1-NFR-03 Determinism | Pure functions; banned calls; invariant strings | Times are parameters (`now`). Architecture tests forbid Domain calls to `DateTime.Now`, `DateTime.UtcNow`, `DateTime.Today`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow` and `Environment`, and any dependency on `System.IO`, `System.Net`, `System.Diagnostics.Process` or `System.Console`. Identity uses `StringComparer.OrdinalIgnoreCase`; normalization uses `ToLowerInvariant`; every `string.Equals`, `StartsWith` and `IndexOf` passes a `StringComparison`. |
| U1-NFR-04 Untrusted text | One guard, shared limits | An internal `DomainText.Require(value, maxLength, paramName, allowLineBreaks)` is the only validation path for text: it rejects null or empty values where a value is required, values over the limit, C0 and C1 control characters, and the bidirectional formatting characters (U+200E, U+200F, U+202A–U+202E, U+2066–U+2069) that can make text display differently from what it contains. `allowLineBreaks` lets CVE descriptions keep line feeds and tabs. A public `DomainLimits` class holds the limits, so U3 cleans Syft and NVD data to the same numbers before constructing. |
| U1-NFR-05 Performance | Single-pass algorithms | `CandidateCpe.Prioritize` is a stable sort by tier, then de-duplication and a cap of three. `Finding.Select` groups CVEs by id in one dictionary pass. `ScanSummary.From` makes one pass over the findings. Transitions copy one record and share unchanged lists. |
| U1-NFR-06 Coverage | Table-driven tests mirroring the design | One test class per rule (the rule-to-test map); `TheoryData` rows copy the worked-example tables of the business logic model, so the design and the tests stay in step. Coverage is collected with `dotnet test --coverage --coverage-output-format cobertura`. |
| U1-NFR-07 Maintainability | Glossary namespaces; rule ids in comments | Namespaces follow the glossary's sections (see `logical-components.md`); each rule's method carries a one-line `// BR-nn` comment. |
| U1-NFR-08 Errors | Standard exceptions through throw helpers | `ArgumentNullException.ThrowIfNull`, `ArgumentException.ThrowIfNullOrWhiteSpace`, `ArgumentOutOfRangeException.ThrowIfNegative` and friends name the parameter; illegal transitions throw `InvalidOperationException("Cannot <transition> a scan that is <Status> in phase <Phase>.")`. No custom exception types. |

## Equality

Records get value equality for their fields, but a list field compares by reference. Value equality is relied on only
where the design needs it — `ScanId`, `Package` (overridden for BR-11), `CandidateCpe`, `CvssScore`, `ScanProgress`,
`ScanSummary` — and BR-07's "distinct by CVE id" uses the id, not record equality. Tests compare list contents
explicitly.
