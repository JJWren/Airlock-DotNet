using Airlock.Application;
using Airlock.Application.Options;
using Airlock.Application.Ports;
using Airlock.Application.UseCases;
using Airlock.Domain.Scanning;
using Airlock.Infrastructure;
using Airlock.Web.Interop;
using Airlock.Web.State;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using MudBlazor;
using MudBlazor.Services;

namespace Airlock.Web.Tests.Support;

/// <summary>
/// U5-NFR-13: a component-test context composed like the host — MudBlazor's services, the Application, and the
/// Infrastructure with U4's fakes for Syft and NVD — with a staging root of its own, a fake clock, a Live Log the test
/// writes, and bUnit's JavaScript stand-in: loose for MudBlazor's calls, strict for airlock.js, so every call Airlock
/// makes into the page must be planned.
/// </summary>
internal sealed class UiTestContext : BunitContext
{
    /// <summary>The module's address in a test, where <c>Assets</c> knows no fingerprints.</summary>
    public const string ModulePath = "./" + AirlockJs.ModulePath;

    // Disposed with the context's services.
    private readonly TemporaryFolder staging = new();

    public UiTestContext(Action<Dictionary<string, string?>>? configure = null, DateTimeOffset? now = null)
    {
        Clock = new FakeTimeProvider(now ?? Test.StartTime);
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Airlock:Staging:Root"] = staging.Combine("staging"),
            ["Airlock:Syft:BinaryPath"] = staging.Combine("no-syft-here", "syft"),
        };
        configure?.Invoke(settings);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        Services.AddSingleton(staging);
        Services.AddSingleton<IConfiguration>(configuration);
        Services.AddSingleton<TimeProvider>(Clock);
        Services.RemoveAll<ILoggerFactory>();
        Services.RemoveAll(typeof(ILogger<>));
        Services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddFakeLogging());
        Services.AddOptions<AirlockOptions>().Bind(configuration.GetSection(AirlockOptions.SectionName));
        Services.AddAirlockApplication();
        Services.AddAirlockInfrastructure(configuration);
        Services.RemoveAll<ISbomGenerator>();
        Services.AddSingleton<ISbomGenerator>(Sbom);
        Services.RemoveAll<IVulnerabilityCatalog>();
        Services.AddSingleton<IVulnerabilityCatalog>(Catalog);
        Services.RemoveAll<ILiveLog>();
        Services.AddSingleton<ILiveLog>(LiveLog);
        Services.AddMudServices();
        Services.AddScoped<AirlockJs>();
        Services.AddScoped<ScanSession>();

        JSInterop.Mode = JSRuntimeMode.Loose;
        Module = JSInterop.SetupModule(ModulePath);
        Module.Mode = JSRuntimeMode.Strict;
    }

    public FakeTimeProvider Clock { get; }

    public FakeSbomGenerator Sbom { get; } = new();

    public FakeVulnerabilityCatalog Catalog { get; } = new();

    public FakeLiveLog LiveLog { get; } = new();

    /// <summary>airlock.js, in strict mode.</summary>
    public BunitJSModuleInterop Module { get; }

    public ScanSession Session => Services.GetRequiredService<ScanSession>();

    public FakeLogCollector Logs => Services.GetFakeLogCollector();

    public string StagingRoot => staging.Path;

    /// <summary>A directory under the test's own folder, for directory scans.</summary>
    public string Directory(string name)
    {
        var path = staging.Combine(name);
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>The calls a session makes into the page when it starts (UR-34, UR-05).</summary>
    public void PlanStart(string? zone = "Europe/Berlin")
    {
        Module.Setup<string?>("timeZone").SetResult(zone);
        Module.SetupVoid("watchConnection").SetVoidResult();
    }

    /// <summary>The calls Control Center's components make when they are first drawn.</summary>
    public void PlanPage()
    {
        Module.SetupVoid("bindArtifactPicker", _ => true).SetVoidResult();
        Module.SetupVoid("followTerminal", _ => true).SetVoidResult();
    }

    public void PlanSubmissions() => Module.SetupVoid("submitScan", _ => true).SetVoidResult();

    /// <summary>Starts the tab's session and waits for its first health answer and its Live Log subscription.</summary>
    public async Task StartSessionAsync(string? zone = "Europe/Berlin")
    {
        PlanStart(zone);
        await Session.StartAsync();
        await Test.EventuallyAsync(() => Session.State.Health is not null, "the session's first health answer");
        await LiveLog.UntilSubscribedAsync();
    }

    /// <summary>MudBlazor's providers, each drawn as a root of its own, as a layout would hold them.</summary>
    public (IRenderedComponent<MudPopoverProvider> Popovers, IRenderedComponent<MudDialogProvider> Dialogs, IRenderedComponent<MudSnackbarProvider> Snackbars) RenderProviders() =>
        (Render<MudPopoverProvider>(), Render<MudDialogProvider>(), Render<MudSnackbarProvider>());

    /// <summary>What the API does with a submission: reserves the slot, stages the files and starts the scan.</summary>
    public async Task<Scan> StartScanAsync(string? directory, params (string Name, string Content)[] artifacts)
    {
        var reserved = await Services.GetRequiredService<StartScan>().ReserveAsync(Test.Token);
        await using var reservation = reserved.Value ?? throw new InvalidOperationException(reserved.Refusal!.Message);
        foreach (var (name, content) in artifacts)
        {
            using var body = new MemoryStream(A.Bytes(content));
            var added = await reservation.AddArtifactAsync(name, body, Test.Token);
            Assert.True(added.IsSuccess, added.Refusal?.Message);
        }

        var started = await reservation.StartAsync(directory, Test.Token);
        return started.Value ?? throw new InvalidOperationException(started.Refusal!.Message);
    }

    /// <summary>The page script's report of the server's answer, as the circuit delivers it.</summary>
    public Task AnswerAsync(int status, string? scanId = null, string? detail = null) =>
        Renderer.Dispatcher.InvokeAsync(() => Session.Sink.Value.Completed(new SubmissionAnswer(status, scanId, null, detail)));

    public Task AnswerAsync(Scan accepted) => AnswerAsync(202, accepted.Id.ToString());

    /// <summary>Files the page staged, as the script reports them.</summary>
    public Task StageAsync(params (string Id, string Name, long Size)[] files) =>
        Renderer.Dispatcher.InvokeAsync(() => Session.Sink.Value.Staged([.. files.Select(file => new StagedFile(file.Id, file.Name, file.Size))]));

    public Scan Registered(Scan scan)
    {
        Services.GetRequiredService<IScanRegistry>().Add(scan);
        return scan;
    }

    /// <summary>
    /// Asserts until it holds, moving the fake clock on by the redraw interval between tries, so redraws the coalescer
    /// holds back are drawn; for state the session's feeds change on other threads.
    /// </summary>
    public async Task UntilAsync(Action assertion, int seconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (true)
        {
            try
            {
                await Renderer.Dispatcher.InvokeAsync(assertion);
                return;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                Clock.Advance(RenderCoalescer.Interval);
                await Task.Delay(10, Test.Token);
            }
        }
    }
}