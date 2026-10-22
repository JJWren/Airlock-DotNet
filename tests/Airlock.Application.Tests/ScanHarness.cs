using Airlock.Application.Correlation;
using Airlock.Application.Events;
using Airlock.Application.Logging;
using Airlock.Application.Options;
using Airlock.Application.Scanning;
using Airlock.Application.Tests.Fakes;
using Airlock.Application.UseCases;
using Airlock.Domain.Scanning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Airlock.Application.Tests;

/// <summary>The Application wired to a fake of every port, a fake clock and a fake logger.</summary>
internal sealed class ScanHarness : IDisposable
{
    public const string Directory = "/uploads/acme-app";

    public static readonly DateTimeOffset StartTime = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly CancellationTokenSource timeout;

    public ScanHarness(HostMode mode = HostMode.Linux, Action<AirlockOptions>? configure = null)
    {
        configure?.Invoke(Options);
        FileSystem = new FakeHostFileSystem(mode).WithDirectory(Directory);
        var options = new OptionsWrapper<AirlockOptions>(Options);
        LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(new FakeLoggerProvider(Logs))
            .AddProvider(new RoutingLoggerProvider(Router, Clock)));

        Correlator = new PackageCorrelator(Catalog, LoggerFactory.CreateLogger<PackageCorrelator>());
        Pipeline = new ScanPipeline(Sbom, Correlator, Registry, options, Clock, LoggerFactory.CreateLogger<ScanPipeline>());
        Coordinator = new ScanCoordinator(Pipeline, Registry, Router, options, Clock, LoggerFactory.CreateLogger<ScanCoordinator>());
        StartScan = new StartScan(Coordinator, Sbom, Catalog, Staging, FileSystem, options, LoggerFactory.CreateLogger<StartScan>());
        CancelScan = new CancelScan(Coordinator, Registry, options, Clock);
        EmergencyStop = new EmergencyStop(Coordinator);
        GetScan = new GetScan(Coordinator, Registry);
        WatchScan = new WatchScan(Coordinator, Registry);
        WatchActiveScan = new WatchActiveScan(Coordinator);
        RenderScanReport = new RenderScanReport(Coordinator, Registry, Renderer);
        GetSystemHealth = new GetSystemHealth(Sbom, Catalog);

        timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
    }

    public AirlockOptions Options { get; } = new();

    public FakeTimeProvider Clock { get; } = new(StartTime);

    public FakeSbomGenerator Sbom { get; } = new();

    public FakeVulnerabilityCatalog Catalog { get; } = new();

    public FakeArtifactStaging Staging { get; } = new();

    public FakeScanRegistry Registry { get; } = new();

    public FakeScanReportRenderer Renderer { get; } = new();

    public FakeHostFileSystem FileSystem { get; }

    public ScanLogRouter Router { get; } = new();

    public FakeLogCollector Logs { get; } = new();

    public ILoggerFactory LoggerFactory { get; }

    public PackageCorrelator Correlator { get; }

    public ScanPipeline Pipeline { get; }

    public ScanCoordinator Coordinator { get; }

    public StartScan StartScan { get; }

    public CancelScan CancelScan { get; }

    public EmergencyStop EmergencyStop { get; }

    public GetScan GetScan { get; }

    public WatchScan WatchScan { get; }

    public WatchActiveScan WatchActiveScan { get; }

    public RenderScanReport RenderScanReport { get; }

    public GetSystemHealth GetSystemHealth { get; }

    /// <summary>The test's token, cut off after 30 seconds so a broken test fails instead of hanging.</summary>
    public CancellationToken Token => timeout.Token;

    public async Task<ScanReservation> ReserveAsync()
    {
        var reserved = await StartScan.ReserveAsync(Token);
        Assert.True(reserved.IsSuccess, reserved.Refusal?.Message);
        return reserved.Value!;
    }

    public async Task<Scan> StartDirectoryScanAsync(string path = Directory)
    {
        await using var reservation = await ReserveAsync();
        var started = await reservation.StartAsync(path, Token);
        Assert.True(started.IsSuccess, started.Refusal?.Message);
        return started.Value!;
    }

    public async Task<Scan> StartUploadScanAsync(params string[] fileNames)
    {
        await using var reservation = await ReserveAsync();
        foreach (var fileName in fileNames)
        {
            using var content = new MemoryStream([1, 2, 3]);
            var added = await reservation.AddArtifactAsync(fileName, content, Token);
            Assert.True(added.IsSuccess, added.Refusal?.Message);
        }

        var started = await reservation.StartAsync(null, Token);
        Assert.True(started.IsSuccess, started.Refusal?.Message);
        return started.Value!;
    }

    public async Task<Scan> RunDirectoryScanAsync(string path = Directory) => await WaitForEndAsync((await StartDirectoryScanAsync(path)).Id);

    public async Task<Scan> RunUploadScanAsync(params string[] fileNames) =>
        await WaitForEndAsync((await StartUploadScanAsync(fileNames)).Id);

    /// <summary>The scan's events up to its finished event; returns once its slot is free again.</summary>
    public async Task<IReadOnlyList<ScanEvent>> CollectEventsAsync(ScanId id)
    {
        List<ScanEvent> events = [];
        await foreach (var scanEvent in WatchScan.SubscribeAsync(id, Token))
        {
            events.Add(scanEvent);
        }

        await WaitForIdleAsync();
        return events;
    }

    public async Task<Scan> WaitForEndAsync(ScanId id)
    {
        await CollectEventsAsync(id);
        return Registry.Find(id) ?? throw new InvalidOperationException("The scan is no longer in the registry.");
    }

    public async Task WaitForIdleAsync()
    {
        await foreach (var state in WatchActiveScan.SubscribeAsync(Token))
        {
            if (!state.IsActive)
            {
                return;
            }
        }
    }

    public IReadOnlyList<FakeLogRecord> LogRecords => Logs.GetSnapshot();

    public void Dispose()
    {
        timeout.Dispose();
        LoggerFactory.Dispose();
    }
}
