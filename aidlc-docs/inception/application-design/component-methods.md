# Component Methods

**Stage**: INCEPTION — Application Design (Standard depth)

Signatures are the intended public surface, written as C# 14 for .NET 10. Bodies, private members and exact
validation rules belong to each unit's Functional Design; names may be refined there but the shapes should hold.

## Airlock.Domain

```csharp
public readonly record struct ScanId(Guid Value)
{
    public static ScanId New();
}

public enum ScanStatus { Running, Completed, Incomplete, Failed, Cancelled }
public enum ScanPhase { SbomGeneration, Correlation, Summary }   // uploads are staged before the scan exists

public abstract record ScanTarget;
public sealed record DirectoryTarget(string Path) : ScanTarget;
public sealed record ArtifactsTarget(IReadOnlyList<Artifact> Artifacts) : ScanTarget;

public enum ArtifactKind { Manifest, Other }
public sealed record Artifact(string FileName, long SizeBytes, ArtifactKind Kind);
public static class ManifestCatalog
{
    public static bool IsManifest(string fileName);
}

public sealed record Package(string Type, string Name, string Version);   // identity: type exact, name ignoring case, version exact
public static class PackageVersion
{
    public static bool IsKnown(Package package);          // false for empty, whitespace or "UNKNOWN"
    public static string ForNvd(Package package);         // Go modules drop a leading "v"
}

public enum CpeSource { Dictionary, Generated }
public sealed record CandidateCpe(string Value, CpeSource Source)
{
    public string Vendor { get; }
    public string Product { get; }
    // Dictionary first; generated candidates whose vendor and product equal the package name next; at most `max`.
    public static IReadOnlyList<CandidateCpe> Prioritize(Package package, IEnumerable<CandidateCpe> candidates,
                                                         int max = 3);
}

public enum Severity { None, Low, Medium, High, Critical }
public static class SeverityScale
{
    public static Severity FromScore(CvssVersion version, decimal baseScore);   // used when NVD gives no label
}
public static class SeverityThreshold
{
    public static Severity Minimum { get; }               // High
    public static bool Admits(Severity severity);
}

public enum CvssVersion { V20, V30, V31, V40 }
public enum CvssSource { Primary, Secondary }              // NVD's own score, or the reporting CNA's
public sealed record CvssScore(decimal BaseScore, Severity Severity, CvssVersion Version, CvssSource Source)
{
    // v3.1 → v3.0 → v4.0 → v2.0; within a version, Primary before Secondary (Q-S0-1)
    public static CvssScore? SelectPreferred(IEnumerable<CvssScore> scores);
}

public sealed record Cve(string Id, string Description, Uri NvdUrl,
                         IReadOnlyList<CvssScore> Scores, IReadOnlyList<string> AffectedProducts)
{
    public CvssScore? PreferredScore { get; }
}

public static class ProductName
{
    public static string Normalize(string name);
    // The CPE product to ask NVD about, or null when the name is too generic to identify a product (ADR-0006).
    public static string? ForProductMatch(Package package);
}

public abstract record MatchMethod;
public sealed record CpeMatch(CandidateCpe Cpe) : MatchMethod;
public sealed record ProductMatch(string Product) : MatchMethod;   // name and version matched against any vendor's product

public sealed record Finding(Cve Cve, CvssScore Score)
{
    public static FindingSelection Select(IEnumerable<Cve> matched);   // threshold, unscored, distinct, ordered
}
public sealed record FindingSelection(IReadOnlyList<Finding> Findings, IReadOnlyList<string> UnscoredCveIds);
public sealed record VulnerablePackage(Package Package, MatchMethod Method, IReadOnlyList<Finding> Findings)
{
    public Severity HighestSeverity { get; }
}
public enum UnverifiedReason { LookupFailed, VersionUnknown }
public sealed record UnverifiedPackage(Package Package, UnverifiedReason Reason, string Detail);

public sealed record ScanSummary(int TotalPackages, int VulnerablePackages, int CleanPackages,
                                 int UnverifiedPackages, int CriticalFindings, int HighFindings)
{
    public static ScanSummary From(int totalPackages,
                                   IReadOnlyList<VulnerablePackage> vulnerable,
                                   IReadOnlyList<UnverifiedPackage> unverified);
}

public sealed record ScanProgress(int Correlated, int Total);
public enum ScanWarningCode
{
    NoPackagesIdentified, DistributionBackportsNotConsidered, CvesWithoutSeverity,
    NotProductMatched   // added by U2 Q-U2-3: no CPE records and a name too generic to product-match
}
public sealed record ScanWarning(ScanWarningCode Code, string Message, string? Input = null);
public enum ScanFailureCode { SyftFailed, SyftTimedOut, SyftOutputUnreadable, ScanTimedOut, Unexpected }
public sealed record ScanFailure(ScanFailureCode Code, string Reason, string? Input = null);

public sealed record Scan   // immutable; every transition returns a new instance or throws on an illegal move
{
    public ScanId Id { get; }
    public ScanTarget Target { get; }
    public ScanStatus Status { get; }
    public ScanPhase Phase { get; }
    public ScanProgress? Progress { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? FinishedAt { get; }
    public ScanSummary? Summary { get; }
    public IReadOnlyList<VulnerablePackage> VulnerablePackages { get; }
    public IReadOnlyList<UnverifiedPackage> UnverifiedPackages { get; }
    public IReadOnlyList<ScanWarning> Warnings { get; }
    public ScanFailure? Failure { get; }
    public string? CancellationReason { get; }
    public bool IsFinished { get; }
    public TimeSpan? Duration { get; }

    public static Scan Start(ScanId id, ScanTarget target, DateTimeOffset now);
    public Scan EnterPhase(ScanPhase phase);
    public Scan RecordProgress(int correlated, int total);
    public Scan AddWarning(ScanWarning warning);
    public Scan Finish(int totalPackages, IReadOnlyList<VulnerablePackage> vulnerable,
                       IReadOnlyList<UnverifiedPackage> unverified, DateTimeOffset now);   // Completed or Incomplete
    public Scan Fail(ScanFailure failure, DateTimeOffset now);
    public Scan Cancel(string reason, DateTimeOffset now);
}
```

## Airlock.Application

### Outcomes

```csharp
public enum RefusalCode
{
    ScanInProgress,      // 409
    NotReady,            // 503 — NVD key missing or Syft unavailable
    InvalidTarget,       // 400 — nothing to scan, a path and artifacts together, path not absolute, missing,
                         //       a file, or outside AllowedScanRoots
    TooManyArtifacts,    // 400
    TooLarge,            // 413
    ReservationStopped,  // 409 — Emergency Stop ended an upload before the scan started
    ScanNotFound,        // 404
    ScanNotFinished,     // 409 — report requested while running
    ScanAlreadyFinished, // 409 — cancel requested after the end
    ScanHasNoResults     // 409 — report requested for a Failed or Cancelled scan
}
public sealed record Refusal(RefusalCode Code, string Message);

public readonly record struct Outcome<T>
{
    public T? Value { get; }
    public Refusal? Refusal { get; }
    public bool IsSuccess { get; }
}
public static class Outcome                               // factories here: CA1000 bars statics on generic types
{
    public static Outcome<T> Success<T>(T value);
    public static Outcome<T> Refused<T>(Refusal refusal);
    public static Outcome<T> Refused<T>(RefusalCode code, string message);
}
```

### Use cases

```csharp
public sealed class StartScan
{
    // Readiness and single-active-slot checks happen here, before the request body is read.
    public ValueTask<Outcome<ScanReservation>> ReserveAsync(CancellationToken cancellationToken);
}

public sealed class ScanReservation : IAsyncDisposable
{
    public ScanId Id { get; }
    // Streams one file into the scan's staging area; enforces count and byte limits; sanitizes the name.
    public ValueTask<Outcome<Artifact>> AddArtifactAsync(string fileName, Stream content,
                                                         CancellationToken cancellationToken);
    // Validates the target (directory path, or at least one artifact), registers the Running scan, starts the pipeline.
    public ValueTask<Outcome<Scan>> StartAsync(string? directoryPath, CancellationToken cancellationToken);
    // If the scan never started: releases the slot and deletes the staging area.
    public ValueTask DisposeAsync();
}

public sealed class CancelScan
{
    public ValueTask<Outcome<Scan>> ExecuteAsync(ScanId id, CancellationToken cancellationToken);
}

public sealed record EmergencyStopResult(bool Stopped, ScanId? ScanId);   // TERMINATED or IDLE
public sealed class EmergencyStop
{
    public ValueTask<EmergencyStopResult> ExecuteAsync(CancellationToken cancellationToken);
}

public sealed class GetScan
{
    public Scan? Execute(ScanId id);
}

public sealed class WatchScan
{
    // First item is ScanEvent.Snapshot; the stream ends after ScanEvent.Finished.
    public IAsyncEnumerable<ScanEvent> SubscribeAsync(ScanId id, CancellationToken cancellationToken);
}

public sealed record ActiveScanState(ScanId? ScanId, bool ReceivingUpload);
public sealed class WatchActiveScan
{
    public ActiveScanState Current { get; }
    public IAsyncEnumerable<ActiveScanState> SubscribeAsync(CancellationToken cancellationToken);
}

public sealed record ScanReportDocument(string FileName, string ContentType, ReadOnlyMemory<byte> Content);
public sealed class RenderScanReport
{
    public ValueTask<Outcome<ScanReportDocument>> ExecuteAsync(ScanId id, CancellationToken cancellationToken);
}

public enum HealthStatus { Online, Degraded }
public sealed record HealthCheck(string Name, bool Passing, string Detail);
public sealed record SystemHealth(HealthStatus Status, IReadOnlyList<HealthCheck> Checks);
public sealed class GetSystemHealth
{
    public ValueTask<SystemHealth> ExecuteAsync(CancellationToken cancellationToken);
}

public sealed class WatchLiveLog
{
    public IAsyncEnumerable<LiveLogEntry> SubscribeAsync(CancellationToken cancellationToken);
}
```

### Services

```csharp
public sealed class ScanCoordinator                       // singleton; the slot behind one small lock
{
    public ActiveScanState Active { get; }
    public IAsyncEnumerable<ActiveScanState> WatchActiveAsync(CancellationToken cancellationToken);
    // Used by the use cases only:
    internal bool TryReserve(ReservationTicket ticket);    // claims the single active slot; the reservation owns the ticket
    internal void MarkReceivingUpload(ReservationTicket ticket);
    internal void Release(ReservationTicket ticket);       // reservation ended before the start
    internal Scan? Launch(ReservationTicket ticket, ScanTarget target, IStagingArea? staging);   // null if stopped meanwhile
    internal Task? TryStop(ScanId id, StopCause cause);    // completes when the run has ended
    internal ScanId? StopActive(StopCause cause);          // Emergency Stop: the running scan or the reservation
    internal Task StopAllAsync();                          // server shutdown
    internal ScanEventHub? HubFor(ScanId id);
    internal void Prune();                                 // RetentionPolicy, then IScanRegistry.Remove
}

public sealed class ScanPipeline
{
    internal Task RunAsync(ScanRun run);                   // ScanRun: the scan's latest version, staging, hub,
                                                           // breaker and one cancellation source with a stop cause
}

public static class ArtifactName                          // AR-07: a safe plain file name on Windows and Linux
{
    public static string Sanitize(string? fileName);
}

public abstract record PackageCorrelation(Package Package);
// Method null: no CPE records and a name too generic to product-match (U2 AR-15).
public sealed record CleanCorrelation(Package Package, MatchMethod? Method, IReadOnlyList<string> UnscoredCveIds)
    : PackageCorrelation(Package);
public sealed record VulnerableCorrelation(VulnerablePackage Result, IReadOnlyList<string> UnscoredCveIds)
    : PackageCorrelation(Result.Package);
public sealed record UnverifiedCorrelation(UnverifiedPackage Result) : PackageCorrelation(Result.Package);

public sealed class PackageCorrelator
{
    public ValueTask<PackageCorrelation> CorrelateAsync(SbomPackage package, NvdOutageBreaker breaker,
                                                        CancellationToken cancellationToken);
}

public sealed class NvdOutageBreaker(int threshold)      // one per scan (AR-18)
{
    public bool IsOpen { get; }
    public void RecordAnswer();
    public bool RecordFailure();                           // true for the failure that opened it
}

public sealed class ScanEventHub                          // one per running scan; owns its latest version
{
    public Scan Latest { get; }
    public IAsyncEnumerable<ScanEvent> SubscribeAsync(CancellationToken cancellationToken);   // snapshot first
    internal void Publish(Scan updated, ScanEvent? scanEvent);
    internal void PublishLog(LiveLogEntry entry);
    internal void Complete(Scan final);                    // publishes ScanFinished, ends every subscription
}

public sealed class ScanLogRouter                         // U3's Live Log provider hands it scan-scoped entries
{
    public void Route(LiveLogEntry entry);
}

public static class ScanLogScope                          // the logging scope with the "ScanId" entry
{
    public static IReadOnlyList<KeyValuePair<string, object?>> For(ScanId scanId);
}

public static class ExternalText                          // AR-25
{
    public static string Clean(string? text, int maxLength = 512);
}
```

### Events and log entries

```csharp
public abstract record ScanEvent(ScanId ScanId);
public sealed record ScanSnapshot(ScanId ScanId, Scan Scan) : ScanEvent(ScanId);
public sealed record PhaseChanged(ScanId ScanId, ScanPhase Phase) : ScanEvent(ScanId);
public sealed record PackagesCorrelated(ScanId ScanId, ScanProgress Progress) : ScanEvent(ScanId);
public sealed record ScanLogged(ScanId ScanId, LiveLogEntry Entry) : ScanEvent(ScanId);
public sealed record ScanFinished(ScanId ScanId, ScanStatus Status) : ScanEvent(ScanId);

public sealed record LiveLogEntry(DateTimeOffset Timestamp, LogLevel Level, string Category,
                                  string Message, ScanId? ScanId);
```

### Ports

```csharp
public abstract record SbomSource;
public sealed record DirectorySource(string Path) : SbomSource;
public sealed record FileSource(string Path, Artifact Artifact) : SbomSource;

public sealed record SbomPackage(Package Package, IReadOnlyList<CandidateCpe> CandidateCpes);
public abstract record SbomOutcome;
public sealed record SbomProduced(IReadOnlyList<SbomPackage> Packages, bool IsContainerImage) : SbomOutcome;
public sealed record SbomFailed(ScanFailure Failure) : SbomOutcome;
public sealed record SbomToolStatus(bool Available, string? Version, string Detail);

public interface ISbomGenerator
{
    ValueTask<SbomOutcome> GenerateAsync(SbomSource source, CancellationToken cancellationToken);   // one Syft run
    ValueTask<SbomToolStatus> GetStatusAsync(CancellationToken cancellationToken);
}

public abstract record CatalogLookup;
public sealed record CatalogFound(IReadOnlyList<Cve> Records) : CatalogLookup;
public sealed record CatalogNoRecords : CatalogLookup;
public sealed record CatalogFailed(string Reason) : CatalogLookup;

public interface IVulnerabilityCatalog
{
    bool IsConfigured { get; }
    // Unknown and malformed CPEs both answer CatalogNoRecords (S0 N1, N2).
    ValueTask<CatalogLookup> FindByCpeAsync(CandidateCpe cpe, CancellationToken cancellationToken);
    // Product match: any vendor's product named `product` at the package's version (virtualMatchString).
    ValueTask<CatalogLookup> FindByProductAsync(Package package, string product, CancellationToken cancellationToken);
}

public sealed record StagedArtifact(Artifact Artifact, string Path);
public interface IArtifactStaging
{
    ValueTask<IStagingArea> CreateAsync(ScanId id, CancellationToken cancellationToken);
}
public interface IStagingArea : IAsyncDisposable
{
    string ManifestsDirectory { get; }
    IReadOnlyList<StagedArtifact> Artifacts { get; }
    long TotalBytes { get; }
    // The name is already sanitized (ArtifactName); the byte limit is answered as a value.
    ValueTask<StagingOutcome> AddAsync(string fileName, Stream content, long maxBytes,
                                       CancellationToken cancellationToken);
}
public abstract record StagingOutcome;
public sealed record ArtifactStaged(StagedArtifact Artifact) : StagingOutcome;
public sealed record ArtifactTooLarge : StagingOutcome;

public interface IScanReportRenderer
{
    ValueTask<byte[]> RenderPdfAsync(Scan scan, CancellationToken cancellationToken);
}

public interface IScanRegistry
{
    void Add(Scan scan);
    Scan? Find(ScanId id);
    Scan Update(ScanId id, Func<Scan, Scan> transition);   // atomic replace of the latest version
    IReadOnlyList<Scan> List();                             // the Application's RetentionPolicy decides what to remove
    void Remove(ScanId id);
}

public interface ILiveLog
{
    IAsyncEnumerable<LiveLogEntry> SubscribeAsync(CancellationToken cancellationToken);   // scan-scoped lines: ScanLogRouter
}

public interface IHostFileSystem
{
    bool IsAbsolute(string path);              // by the host's rules
    bool DirectoryExists(string path);
    bool FileExists(string path);
    string GetFullPath(string path);
    string ResolveLinks(string path);          // every symbolic link on the way followed
    bool IsWithin(string path, string root);   // whole path segments, with the host's case rules
}
```

### Options

```csharp
public sealed class AirlockOptions                        // section "Airlock"; settable for configuration binding
{
    public LimitsOptions Limits { get; set; }              // MaxBytesPerScan = 2 GiB, MaxArtifactsPerScan = 25
    public TimeoutOptions Timeouts { get; set; }           // SyftRun = 15 min, Scan = 60 min, CancelGracePeriod = 5 s
    public RetentionOptions Retention { get; set; }        // Duration = 24 h, MaxScans = 20
    public IReadOnlyList<string> AllowedScanRoots { get; set; }    // empty = unrestricted
    public CorrelationOptions Correlation { get; set; }    // MaxParallelism = 4, OutageThreshold = 5
}
public sealed class AirlockOptionsValidator : IValidateOptions<AirlockOptions>;   // the ranges of the U2 NFR Design

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddAirlockApplication(this IServiceCollection services);
}   // also registers ScanLifetimeService (IHostedService): warns about missing scan roots, stops scans at shutdown
```

## Airlock.Infrastructure

```csharp
public sealed class SyftSbomGenerator : ISbomGenerator;
internal sealed class SyftProcessRunner
{
    public Task<SyftRunResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout,
                                        CancellationToken cancellationToken);   // kills the process tree on cancel or timeout
}
internal sealed class SyftJsonParser
{
    public IReadOnlyList<SbomPackage> Parse(Stream syftJson);
}
internal static class NuspecReader                         // Q-S0-3: the NuGet package a .nupkg names
{
    public static Package? Read(Stream nupkg);             // type "nuget", id, version
}

public sealed class NvdVulnerabilityCatalog : IVulnerabilityCatalog;
public sealed class NvdApiClient                           // typed HttpClient; resilience and limiter in its handler pipeline
{
    public Task<NvdCveResponse?> GetByCpeNameAsync(string cpeName, CancellationToken cancellationToken);
    public Task<NvdCveResponse?> GetByVirtualMatchAsync(string matchString, CancellationToken cancellationToken);
}
internal sealed class NvdResponseMapper
{
    // CVSS metrics with their Primary / Secondary source, affected products from configurations; unknown metric
    // types (e.g. ssvcV203) are ignored.
    public IReadOnlyList<Cve> Map(NvdCveResponse response);
}

public sealed class DiskArtifactStaging : IArtifactStaging;
internal static class FileNameSanitizer
{
    public static string Sanitize(string fileName);
}

public sealed class QuestPdfScanReportRenderer : IScanReportRenderer;
public sealed class InMemoryScanRegistry : IScanRegistry;
public sealed class LiveLogBroadcaster : ILiveLog
{
    public void Publish(LiveLogEntry entry);
}
public sealed class LiveLogLoggerProvider : ILoggerProvider;   // Airlock.* categories; scan id from the logging scope
public sealed class HostFileSystem : IHostFileSystem;

public sealed class SyftOptions { public string BinaryPath { get; init; } }                 // SYFT_BINARY_PATH, default "syft"
public sealed class NvdOptions                                                              // NVD_API_KEY, base URL, rate
{
    public string? ApiKey { get; init; }
    public Uri BaseUrl { get; init; }
    public int RequestsPerWindow { get; init; }                                             // 50
    public TimeSpan Window { get; init; }                                                   // 30 s
}
public sealed class StagingOptions { public string Root { get; init; } }                    // per-scan folders live here

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddAirlockInfrastructure(this IServiceCollection services,
                                                              IConfiguration configuration);
}
```

## Airlock.Web

### API (`/api/v2`)

| Endpoint | Handler | Use case | Results |
|---|---|---|---|
| `POST /scans` | `ScanEndpoints.CreateScanAsync` | `StartScan` via `MultipartScanReader` | 202 + `Location` + `{id, status}`; ProblemDetails 400 / 409 / 413 / 503 |
| `GET /scans/{id:guid}` | `ScanEndpoints.GetScan` | `GetScan` | 200 `ScanDto`; 404 |
| `GET /scans/{id:guid}/events` | `ScanEndpoints.WatchScan` | `WatchScan` | SSE: `snapshot`, `phase`, `progress`, `log`, `finished`; 404 |
| `GET /scans/{id:guid}/report.pdf` | `ScanEndpoints.GetReportAsync` | `RenderScanReport` | 200 `application/pdf`; 404; 409 |
| `POST /scans/{id:guid}/cancel` | `ScanEndpoints.CancelAsync` | `CancelScan` | 202; 404; 409 |
| `POST /system/stop` | `SystemEndpoints.StopAsync` | `EmergencyStop` | 200 `{status: TERMINATED / IDLE, scanId?}` |
| `GET /system/health` | `SystemEndpoints.GetHealthAsync` | `GetSystemHealth` | 200 `{status, checks[]}` |
| `GET /logs/stream` | `LogEndpoints.Stream` | `WatchLiveLog` | SSE: `log` |

```csharp
internal sealed class MultipartScanReader
{
    // Reads sections in order without buffering whole files: "targetPath" (optional) and "artifacts" (0..n).
    public ValueTask<Outcome<Scan>> ReadAndStartAsync(HttpRequest request, ScanReservation reservation,
                                                      CancellationToken cancellationToken);
}
internal static class ScanDtoMapper
{
    public static ScanDto ToDto(Scan scan);                // camelCase, glossary names
}
internal static class RefusalProblemDetails
{
    public static IResult ToResult(Refusal refusal);       // status-code map in RefusalCode comments
}
```

### UI

| Component | Parameters / state | Behaviour |
|---|---|---|
| `ControlCenter` (`/`) | `ScanSession` | Status pill, directory path, artifact picker, Execute, elapsed timer, progress, mini Terminal, inline refusal or failure. |
| `ActiveLogs` (`/logs`) | `ScanSession` | Header with machine name, elapsed timer, full Terminal. |
| `Reports` (`/reports/{ScanId:guid?}`) | `ScanId?`, `ScanSession` | Spinner, empty state, header, exports, Rescan, cards, Unverified banner, grid, detail pane. |
| `StatusPill` | — | ONLINE / DEGRADED (reason tooltip) / LOADING from `GetSystemHealth` + `WatchActiveScan`; OFFLINE applied client-side by the connection watcher. |
| `ArtifactPicker` | `EventCallback<IReadOnlyList<StagedFileInfo>> StagedChanged` | Plain `<input type="file" multiple>` read by `airlock.js`; staged list with remove. |
| `ScanProgress` | `Scan Scan` | Phase and "Correlating n of total". |
| `ElapsedTimer` | `DateTimeOffset? StartedAt`, `DateTimeOffset? FinishedAt` | `mm:ss`, `h:mm:ss` past an hour. |
| `Terminal` | `IReadOnlyList<LiveLogEntry> Lines`, `bool Compact`, `EventCallback EmergencyStopRequested` | Minimize, auto-scroll, empty state, Export Logs, Emergency Stop (opens `StopScanDialog`). |
| `StopScanDialog` | — | MudDialog confirmation ("CRITICAL: Terminate the active scan?"). |
| `SummaryCards` | `ScanSummary Summary` | Total, Critical findings (pulse when > 0), High findings, Clean. |
| `UnverifiedBanner` | `IReadOnlyList<UnverifiedPackage> Packages` | Shown when any package is unverified. |
| `InventoryGrid` | `Scan Scan`, `EventCallback<Package> Selected` | Risk, Package, Version, Method, Findings; filter by package name or CVE id; selection by package identity. |
| `VulnerabilityDetail` | `VulnerablePackage Package`, `TimeZoneInfo TimeZone` | CVE link, copy description, CVSS bar with version, description, package type. |

```csharp
public sealed class ScanSession                            // scoped: one per circuit (browser tab)
{
    public string DirectoryPath { get; set; }              // default "/uploads"
    public IReadOnlyList<StagedFileInfo> Staged { get; }
    public Scan? Current { get; }
    public IReadOnlyList<LiveLogEntry> TerminalLines { get; }   // bounded
    public TimeZoneInfo BrowserTimeZone { get; }
    public event Action? Changed;
    public Task<Outcome<ScanId>> SubmitAsync(ElementReference fileInput, CancellationToken cancellationToken);
    public Task FollowAsync(ScanId id, CancellationToken cancellationToken);
    public Task<EmergencyStopResult> StopAsync(CancellationToken cancellationToken);
}

public sealed class AirlockJs : IAsyncDisposable          // wraps wwwroot/js/airlock.js
{
    public ValueTask<UploadResult> SubmitScanAsync(ElementReference fileInput, string? targetPath,
                                                   DotNetObjectReference<UploadProgressSink> progress);
    public ValueTask DownloadTextAsync(string fileName, string content);
    public ValueTask CopyTextAsync(string text);
    public ValueTask<string> GetTimeZoneAsync();
    public ValueTask WatchConnectionAsync(ElementReference statusPill);
}

public static class AirlockTheme
{
    public static MudTheme Create();
}
```

`airlock.js` (ES module): `submitScan(fileInput, targetPath, progressSink)` posts `multipart/form-data` to
`/api/v2/scans` with XMLHttpRequest upload-progress events and resolves `{status, body}`; `downloadText(name, text)`;
`copyText(text)`; `getTimeZone()`; `watchConnection(element)` toggles the OFFLINE state on Blazor reconnection
events.

**Exports**: Export PDF and Export JSON are plain download links to `GET /api/v2/scans/{id}/report.pdf` and
`GET /api/v2/scans/{id}`, so the UI and the API return byte-for-byte the same files (E11). Export Logs is generated
from the tab's terminal lines and saved through `DownloadTextAsync`.
