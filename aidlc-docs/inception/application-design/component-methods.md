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
    // The candidate with its version part sent as PackageVersion.ForNvd(package) — a Go module's v dropped (U3 decision 1).
    public CandidateCpe ForNvd(Package package);
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
public static class Refusals                              // added in U4: the one refusal the Web also answers itself
{
    public static Refusal ScanNotFound { get; }            // "Scan not found or no longer retained", for an unknown id too
}

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
    // Added in U4 (its refinement 1): refuse at once, before anything more is staged — a path together with artifacts,
    // or a path the host could not read as text — recorded, logged and repeated like every other refusal (AR-03).
    public Refusal RefuseBothTargets();
    public Refusal RefuseUnreadableTargetPath();
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
    // Snapshot first; the subscription is removed however the subscriber leaves, right after its snapshot included (U4).
    public IAsyncEnumerable<ScanEvent> SubscribeAsync(CancellationToken cancellationToken);
    internal int SubscriberCount { get; }                  // U4-NFR-09's check that nobody who has gone is written to
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
}   // also registers ScanLifetimeService (IHostedLifecycleService since U4): warns about missing scan roots, and cancels
    // the active scan as soon as the host begins to stop (StoppingAsync), before the web server stops (AR-30)
```

## Airlock.Infrastructure

```csharp
public sealed class SyftSbomGenerator : ISbomGenerator;
internal sealed class SyftProcessRunner
{
    // No shell; standard output streamed to the reader while the last 4 KB of standard error are kept; the process
    // tree killed on cancellation or at the run's limit; the run's own temporary folder; no NVD key in the environment.
    public Task<SyftRunResult<T>> RunAsync<T>(SyftRunRequest request, Func<Stream, CancellationToken, Task<T>> readOutput,
                                             CancellationToken cancellationToken);
}
internal sealed class SyftEnvironment                      // the update check off, the run's own TMPDIR, no NVD key
{
    public SyftRunRequest Request(string fileName, IReadOnlyList<string> arguments, TimeSpan timeLimit,
                                  string? temporaryFolder = null);
}
internal static class SyftJsonParser
{
    public static Task<SyftParse> ParseAsync(Stream syftJson, CancellationToken cancellationToken);   // a document, or why not
    public static SbomProduced Map(SyftDocument document);  // throws ArgumentException for a package the Domain rejects
}
internal static class NuspecReader                         // Q-S0-3: the NuGet package a .nupkg names
{
    public static NuspecRead Read(Stream nupkg);           // type "nuget", id, version — or the reason, past the caps too
}

public sealed class NvdVulnerabilityCatalog : IVulnerabilityCatalog, IDisposable;   // owns the one pacer; 2-minute lookups
internal sealed class NvdApiClient                         // the named HttpClient "Airlock.Nvd" from the factory, per request
{
    // One page through the pipeline (retry, NvdRequestPacer, 30-second attempt timeout); the body is read inside the
    // attempt. An NvdQuery is a cpeName or a virtualMatchString. Answers NvdPage, NvdNotFound or NvdFailed.
    public Task<NvdAttempt> GetPageAsync(NvdQuery query, int startIndex, CancellationToken cancellationToken);
}
internal sealed class NvdRequestPacer : RateLimiter;       // one request start every Window / RequestsPerWindow, app-wide
internal static class NvdResponseMapper
{
    // CVSS metrics with their Primary / Secondary source, affected products from configurations; unknown metric
    // types (e.g. ssvcV203) are ignored.
    public static IReadOnlyList<Cve> Map(NvdCveResponse response);
}

public sealed class DiskArtifactStaging : IArtifactStaging;   // names arrive sanitized by the Application's ArtifactName
internal sealed class StagingSweepService : IHostedService, IDisposable;   // at startup, locks the root (.airlock-lock, U4)
                                                                         // and deletes the marked folders an earlier run left

public sealed class QuestPdfScanReportRenderer : IScanReportRenderer;
public sealed class InMemoryScanRegistry : IScanRegistry;   // a plain store; retention is the Application's RetentionPolicy
public sealed class LiveLogBroadcaster : ILiveLog              // replays the last 200 entries, then 1,000 per subscriber
{
    public void Publish(LiveLogEntry entry);
}
public sealed class LiveLogLoggerProvider : ILoggerProvider;   // Airlock.* categories; scan-scoped entries to ScanLogRouter
public sealed class HostFileSystem : IHostFileSystem;

public sealed class SyftOptions                                                             // SYFT_BINARY_PATH
{
    public string? BinaryPath { get; set; }                                                 // none: the IR-06 search
    public TimeSpan StatusCacheDuration { get; set; }                                       // 30 s
}
public sealed class NvdOptions                                                              // NVD_API_KEY, base URL, rate
{
    public string? ApiKey { get; set; }
    public Uri BaseUrl { get; set; }
    public int RequestsPerWindow { get; set; }                                              // 50
    public TimeSpan Window { get; set; }                                                    // 30 s
}
public sealed class StagingOptions { public string Root { get; set; } }                     // per-scan folders live here; a full path

public static class AirlockConfigurationBuilderExtensions
{
    // NVD_API_KEY, SYFT_BINARY_PATH and LOG_LEVEL (the original's level names or .NET's) onto the options; U4 adds it
    public static IConfigurationBuilder AddAirlockEnvironmentVariables(this IConfigurationBuilder builder);
}

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
| `POST /scans` | `ScanEndpoints.CreateScanAsync` | `StartScan` via `MultipartScanReader` | 202 + `Location` + `ScanAcceptedDto`; problems 400 / 403 / 409 / 413 / 415 / 503 |
| `GET /scans/{id}` | `ScanEndpoints.GetScan` | `GetScan` | 200 `ScanDto`; 404 |
| `GET /scans/{id}/events` | `ScanEndpoints.WatchScan` | `GetScan`, then `WatchScan` | SSE: `snapshot`, `phase`, `progress`, `log`, `finished`; 404 |
| `GET /scans/{id}/report.pdf` | `ScanEndpoints.GetReportAsync` | `RenderScanReport` | 200 `application/pdf`; 404; 409 |
| `POST /scans/{id}/cancel` | `ScanEndpoints.CancelAsync` | `CancelScan` | 200 or 202 + `ScanDto`; 403; 404; 409 |
| `POST /system/stop` | `SystemEndpoints.StopAsync` | `EmergencyStop` | 200 `EmergencyStopDto` (`TERMINATED` / `IDLE`, `scanId`); 403 |
| `GET /system/health` | `SystemEndpoints.GetHealthAsync` | `GetSystemHealth` | 200 `SystemHealthDto` (`status`, `version`, `checks[]`) |
| `GET /logs/stream` | `LogEndpoints.Stream` | `WatchLiveLog` | SSE: `log` |

An `{id}` is parsed with `ScanId.TryParse`, so anything but a scan id answers 404 `scan-not-found` (U4 WR-01). The DTOs,
event payloads and problem catalogue are in `construction/u4-web-api/functional-design/domain-entities.md`.

```csharp
internal static class AirlockHost                          // the composition root, the only Web code naming Infrastructure
{
    public static void ConfigureServices(WebApplicationBuilder builder);   // configuration sources, Kestrel, options, layers, API
    public static void ConfigurePipeline(WebApplication app);              // the /api branch, the endpoints, the docs
}   // Program's top-level statements only create the builder, call these two and run

internal sealed class MultipartScanReader
{
    // 415 for another media type, 400 for a missing or over-long boundary, before anything is reserved (U4 WR-06).
    public ProblemHttpResult? CheckContentType(HttpRequest request, out string boundary);
    // Reads the parts in order without buffering whole files (U4 WR-07): "targetPath" (0..1) and "artifacts" or
    // "artifacts[]" (0..n), names compared ignoring case; any other part is refused. Both kinds of target, or an
    // unreadable path, are refused through the reservation at once.
    public Task<SubmissionResult> ReadAndStartAsync(HttpContext context, string boundary, ScanReservation reservation);
}
// SubmissionStarted(Scan), SubmissionRefused(Refusal), SubmissionInvalid(Detail), SubmissionTooLarge,
// SubmissionAbandoned(Stalled): what ScanEndpoints answers — 202, the refusal's problem, 400, 413, or nothing.
internal abstract record SubmissionResult;

internal static class ScanDtoMapper
{
    public static ScanDto ToDto(Scan scan);                // camelCase, glossary names, values in capitals (U4 WR-02)
    public static ScanStatusValue Status(ScanStatus status);
    public static ScanPhaseValue Phase(ScanPhase phase);
}
internal static class EventMapper                          // one event: name and one line of compact JSON each (U4 WR-13)
{
    public static EventStreamItem ForScan(ScanEvent scanEvent);       // snapshot, phase, progress, log, finished
    public static EventStreamItem ForLiveLog(LiveLogEntry entry);     // log, with its scanId
}
internal static class ApiProblems
{
    public static ProblemHttpResult ForRefusal(Refusal refusal);     // the problem kind of each RefusalCode (U4 WR-03)
    public static ProblemHttpResult ForInvalidRequest(string detail); // and the Web's own problems (U4 WR-04):
    public static ProblemHttpResult ForUnsupportedMediaType();         // 415,
    public static ProblemHttpResult ForCrossSiteRequest();             // 403,
    public static ProblemHttpResult ForBodyTooLarge();                 // 413
    // For the problems ASP.NET Core writes itself (404, 405, 400 and 500 among them): the kind from the status, the
    // five members only, and the traceId (U4 WR-05).
    public static void Customize(ProblemDetailsContext context);
}
internal sealed class ApiExceptionHandler : IExceptionHandler;   // 500 without the message or stack; a bad request keeps its status
internal sealed class CrossSiteRequestFilter : IEndpointFilter
{
    // 403 for a POST a browser marks as cross-site: Sec-Fetch-Site, else Origin against Host (U4 WR-19).
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next);
}
internal sealed class EventStreamResult : IResult          // constructed with the source of its EventStreamItems
{
    // event: and data: lines, a flush per event, ": keep-alive" after 15 seconds of silence; it ends when the client
    // goes, a second after shutdown begins, or with the source (U4 WR-13, U4-NFR-05).
    public Task ExecuteAsync(HttpContext httpContext);
}
internal sealed class IdleTimeoutStream : Stream
{
    // The request body, read with a 60-second idle timer on the TimeProvider (U4-NFR-04).
}
internal sealed class RequestBodyStream : Stream;          // an artifact's part; remembers whether reading the request failed
```

### UI

The rules (UR), the copy and the types are in `construction/u5-web-ui/functional-design/`. Razor makes every component
a public class, so the components, and `SessionComponent`, their base, are public; their parameters are of public types,
and what they share of the tab's session is `private protected` (U5 refinement 1). Every other UI type is internal.

| Component | Parameters / state | Behaviour |
|---|---|---|
| `MainLayout`, `NavMenu`, `ConnectionBar`, `Announcer` | `ScanSession` | Top bar with the brand and inert placeholders (UR-02); drawer with the items and Execute Audit (UR-01); the connection bar (UR-05); the screen readers' announcer. `MainLayout` hands `airlock.js`'s fingerprinted address to `AirlockJs` and starts the session (U5 refinement 5). |
| `ControlCenter` (`/`) | `ScanSession` | Status pill; the path, dimmed while artifacts are staged (Q-U5-1); artifact picker; the checks before uploading; Execute with its reasons; transfer progress; timer; progress; notice; docked Terminal (UR-04 to UR-16). |
| `ActiveLogs` (`/logs`) | `ScanSession` | Heading, the node line with the machine's name, timer, full Terminal (UR-21). |
| `Reports` (`/reports/{Id:guid?}`) | `Guid? Id`, `ScanSession` | The views of UR-22; header, exports, Rescan, cards, banner, warnings, grid, detail pane (UR-22 to UR-33). The parameter is `Id`, since a property named `ScanId` would hide the type. |
| `NotFound`, `AirlockErrorBoundary` | — | "Page not found"; "Something went wrong" with Reload (UR-39). |
| `Error` (`/error`), `PlainLayout` | the request's `HttpContext` | Excluded from interactive routing and drawn by the server alone, without MudBlazor or the session (U5 refinement 2). |
| `StatusPill` | `ScanSession` | LOADING, ONLINE, DEGRADED with the failing checks' details in a tooltip that keyboard focus opens; OFFLINE set by `airlock.js` (UR-04, UR-05). |
| `ArtifactPicker` | `ScanSession` | The Artifact Upload panel over a hidden `<input type="file" multiple>`; the staged list with sizes and remove (UR-07). |
| `ScanProgress` | `ScanSession` | The Preparing line, the transfer line, the phase and "Correlating n of total" (UR-11, UR-16). |
| `ElapsedTimer` | `Scan? Scan` | `mm:ss`, `h:mm:ss` from an hour; stopped at the finish; `00:00` without a scan (UR-15). |
| `ScanNotice` | `ScanSession` | The tab's inline notice, with ✕ (UR-12). |
| `Terminal` | `bool Compact`, `ScanSession` | Lines with level names and colours; following the newest line unless scrolled up; minimize; empty state; Export Logs; Emergency Stop (UR-17 to UR-20). |
| `StopScanDialog` | — | "CRITICAL: Terminate the active scan?" with Cancel, focused first, and Terminate (UR-20). |
| `SummaryCards` | `ScanSummary Summary` | Total Packages, Critical Findings (pulsing above zero), High Findings, Clean Packages (UR-24). |
| `UnverifiedBanner` | `IReadOnlyList<UnverifiedPackage> Packages` | The count sentence, five rows, Show all (UR-25). |
| `WarningsPanel` | `IReadOnlyList<ScanWarning> Warnings` | "Warnings (n)", collapsed (UR-26). |
| `InventoryGrid` | `Scan Scan`, `Package? Selected`, `EventCallback<Package?> SelectedChanged` | Rows of vulnerable, then unverified packages; the filter by package name or CVE id; selection by package identity (UR-27 to UR-29). |
| `VulnerabilityDetail` | `Scan Scan`, `Package Package`, `EventCallback Closed` | The match; each finding's link, COPY, CVSS version and source, score bar and description; an unverified package's reason (UR-30, UR-31). |

```csharp
public abstract class SessionComponent : ComponentBase, IDisposable   // Airlock.Web.Components.Parts
{
    private protected ScanSession Session { get; }         // injected
    private protected SessionState State { get; }          // Session.State
    private protected abstract SessionChange RedrawsFor { get; }   // redraws, coalesced, for these kinds only
}

internal sealed class ScanSession : IAsyncDisposable       // scoped: one per circuit (browser tab)
{
    // One immutable snapshot, replaced under a lock (U5 NFR Design): the directory path, the staged artifacts, the
    // upload, whether the tab is submitting, the active slot, the scan followed (Q-U5-2), the ids submitted here, the
    // last 1,000 terminal lines, the health read every 10 seconds, the time zone and the notice.
    public SessionState State { get; }
    public LimitsOptions Limits { get; }                   // the server's limits, for the checks before uploading (UR-08)
    public DotNetObjectReference<UploadSink> Sink { get; }
    public event Action<SessionChange>? Changed;           // flags: Lines, Scan, Slot, Health, Notice, Staging, Upload, TimeZone
    public event Action<ScanEnding>? ScanEnded;            // MainLayout navigates or shows the snackbar (UR-14)
    public event Action<string>? Announced;                // every notice, and the snackbars the pages show (U5-NFR-07)
    public Task StartAsync();                              // the four loops and the time zone, once per circuit
    public void SetDirectoryPath(string? path);
    public Task UnstageAsync(string stagedId);
    public Task<SubmissionOutcome> ExecuteAsync();         // UR-08 to UR-11; completes with the server's answer
    public Task<SubmissionOutcome> RescanAsync(Scan scan); // UR-33
    public Task<EmergencyStopResult> StopAsync(CancellationToken cancellationToken);   // UR-20
    public void DismissNotice();
    public void Announce(string text);
}

internal sealed record SessionState(string DirectoryPath, ImmutableList<StagedArtifact> Staged, UploadProgress? Upload,
    bool Submitting, ActiveScanState Active, Scan? Current, ImmutableHashSet<ScanId> Submitted,
    ImmutableList<TerminalLine> Lines, SystemHealth? Health, TimeZoneInfo? TimeZone, InlineNotice? Notice);
    // and IsPreparing, SlotHeld, CanExecute, StagedBytes
internal sealed record ScanEnding(Scan Scan, bool SubmittedHere);
internal readonly record struct SubmissionOutcome(SubmissionKind Kind, ScanId? ScanId = null);
    // SubmissionKind: Accepted, Refused, Interrupted, NoFiles, NotSubmitted

internal sealed record StagedArtifact(string Id, string FileName, long SizeBytes);
internal sealed record UploadProgress(long SentBytes, long TotalBytes);            // and its Percent
internal sealed record TerminalLine(long Sequence, DateTimeOffset Timestamp, LogLevel Level, string Message, ScanId? ScanId);
internal enum NoticeKind { Refusal, Interrupted, Failed, Cancelled, Finished, RescanHint }
internal sealed record InlineNotice(NoticeKind Kind, string Text, ScanId? ScanId); // and its Announcement
internal abstract record InventoryRow(Package Package);  // VulnerableRow(VulnerablePackage) or UnverifiedRow(UnverifiedPackage)

internal sealed class AirlockJs : IAsyncDisposable        // wraps wwwroot/js/airlock.js; a failed call is logged at Error
{
    public void UseModuleAddress(string fingerprinted);    // from MainLayout, which can read Assets (U5 refinement 5)
    // The panel opens the file dialog in the browser; picked files are reported to the sink (Staged).
    public ValueTask<bool> BindArtifactPickerAsync(ElementReference panel, ElementReference fileInput,
                                                   DotNetObjectReference<UploadSink> sink);
    public ValueTask<bool> UnstageAsync(string stagedId);
    // Both return once the upload has started; progress and the answer arrive through the sink (U5-NFR-05).
    public ValueTask<UploadStart> SubmitScanAsync(ScanSubmissionTarget target, DotNetObjectReference<UploadSink> sink);
    public ValueTask<UploadStart> ResubmitScanAsync(ScanId id, DotNetObjectReference<UploadSink> sink);   // NoFiles: gone
    public ValueTask<bool> FollowTerminalAsync(ElementReference lines);   // newest line unless scrolled up (UR-17)
    public ValueTask<bool> CopyTextAsync(string text);
    public ValueTask<bool> DownloadTextAsync(string fileName, string text);
    public ValueTask<string?> TimeZoneAsync();             // null when the call failed; the session then uses UTC
    public ValueTask<bool> WatchConnectionAsync();
}
internal enum UploadStart { Started, NoFiles, Failed }
// UploadSink, owned by the session: [JSInvokable] Staged(StagedFile[]), Progress(sent, total), Completed(SubmissionAnswer).
// SubmissionAnswer: the status, and the scan id (202) or the problem's title and detail; status 0 for no answer.
// ScanSubmissionTarget: ForPath(path) or ForArtifacts(stagedIds).

internal static class AirlockTheme
{
    public static MudTheme Create();                       // the tokens of the U5 domain entities
}
```

Added to the Application by U5 (UR-08), in `Airlock.Application.UseCases`, so that the page and the reservation word
the limits alike:

```csharp
public static class UploadLimits
{
    public static Refusal? Check(int fileCount, long totalBytes, LimitsOptions limits);   // the reservation's refusals
    public static string Size(long bytes);                 // "2 GB", "1.5 MB": the size wording of the refusals
    // Internal: TooManyArtifacts(limits) and TooLarge(limits), which ScanReservation's checks before each file and
    // while each one streams use.
}
```

`airlock.js` (ES module, one per page, kept across Blazor's navigation): `bindArtifactPicker(panel, input, sink)`,
`unstage(id)`, `submitScan(target, sink)` and `resubmitScan(scanId, sink)` — `multipart/form-data` to `/api/v2/scans`
through XMLHttpRequest, returning once started, reporting progress at most four times a second and the answer through
the sink, and keeping each accepted upload's files for Rescan — `followTerminal(element)`, `copyText(text)`, with a
fallback outside secure contexts, `downloadText(name, text)`, `timeZone()` and `watchConnection()`. The contract is in
the U5 business logic model.

**Exports**: Export PDF and Export JSON are plain download links to `GET /api/v2/scans/{id}/report.pdf` and
`GET /api/v2/scans/{id}`, so the UI and the API return byte-for-byte the same files (E11); the JSON is saved as
`airlock_audit_<finish time>.json`, the time written as in the PDF's name. Export Logs is generated from the tab's
terminal lines and saved through `DownloadTextAsync`.
