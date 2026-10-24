using System.Globalization;
using Airlock.Application.Logging;
using Airlock.Application.Options;
using Airlock.Application.Ports;
using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;
using Airlock.Infrastructure.Options;
using Airlock.Infrastructure.Staging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Airlock.Infrastructure.Syft;

/// <summary>Builds SBOMs by running Syft, one run per call (IR-01 to IR-06).</summary>
public sealed class SyftSbomGenerator : ISbomGenerator
{
    private readonly SyftProcessRunner runner;
    private readonly SyftEnvironment environment;
    private readonly SyftLocator locator;
    private readonly SyftStatusProbe probe;
    private readonly string stagingRoot;
    private readonly TimeSpan timeLimit;
    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;

    public SyftSbomGenerator(
        IOptions<SyftOptions> syft, IOptions<NvdOptions> nvd, IOptions<StagingOptions> staging, IOptions<AirlockOptions> airlock,
        TimeProvider timeProvider, ILogger<SyftSbomGenerator> logger)
        : this(
            new SyftLocator(syft.Value.BinaryPath), new SyftEnvironment(nvd.Value.ApiKey), syft.Value.StatusCacheDuration,
            staging.Value.Root, airlock.Value.Timeouts.SyftRun, timeProvider, logger)
    {
    }

    internal SyftSbomGenerator(
        SyftLocator locator, SyftEnvironment environment, TimeSpan statusCacheDuration, string stagingRoot, TimeSpan timeLimit,
        TimeProvider timeProvider, ILogger logger)
    {
        this.locator = locator;
        this.environment = environment;
        this.stagingRoot = stagingRoot;
        this.timeLimit = timeLimit;
        this.timeProvider = timeProvider;
        this.logger = logger;
        runner = new SyftProcessRunner(timeProvider);
        probe = new SyftStatusProbe(locator, runner, environment, statusCacheDuration, timeProvider, logger);
    }

    public async ValueTask<SbomOutcome> GenerateAsync(SbomSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        // IR-01: a directory is "dir:<path>"; an artifact is its path alone, so Syft detects archives and images itself.
        var (argument, input, artifact) = source switch
        {
            DirectorySource directory => ("dir:" + directory.Path, directory.Path, null),
            FileSource file => (file.Path, file.Artifact.FileName, file.Artifact),
            _ => throw new ArgumentException("Unknown kind of SBOM source.", nameof(source)),
        };
        var binary = locator.Locate().Path;
        var temporary = PrepareTemporaryFolder();
        try
        {
            var started = timeProvider.GetTimestamp();
            var run = await RunAsync(binary, argument, temporary, input, cancellationToken);
            if (run.End == SyftRunEnd.Exited && logger.IsEnabled(LogLevel.Debug))
            {
                logger.RunFinished(input, (long)timeProvider.GetElapsedTime(started).TotalMilliseconds, run.ExitCode);
            }

            return Outcome(run, source, artifact);
        }
        finally
        {
            await RemoveTemporaryFolderAsync(temporary);
        }
    }

    public ValueTask<SbomToolStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        new(probe.GetAsync(cancellationToken));

    private static string Describe(TimeSpan limit) =>
        limit.TotalMinutes >= 1 && limit.Seconds == 0 && limit.Milliseconds == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)limit.TotalMinutes} minute{(limit.TotalMinutes >= 2 ? "s" : string.Empty)}")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)limit.TotalSeconds} seconds");

    private static SbomFailed Failed(ScanFailureCode code, string reason, Artifact? artifact) =>
        new(new ScanFailure(code, reason, artifact?.FileName));

    private async Task<SyftRunResult<SyftParse>> RunAsync(
        string binary, string argument, string? temporary, string input, CancellationToken cancellationToken)
    {
        try
        {
            var request = environment.Request(binary, ["scan", argument, "-o", "json", "-q"], timeLimit, temporary);
            var run = await runner.RunAsync(request, SyftJsonParser.ParseAsync, cancellationToken);
            if (run.End == SyftRunEnd.TimedOut)
            {
                logger.RunStopped(input, "the time limit passed");
            }

            return run;
        }
        catch (OperationCanceledException)
        {
            logger.RunStopped(input, "the scan was stopped");
            throw;
        }
    }

    // IR-02: every failure names its input; a directory run's input is named by the Application (U2 AR-09).
    private SbomOutcome Outcome(SyftRunResult<SyftParse> run, SbomSource source, Artifact? artifact)
    {
        switch (run.End)
        {
            case SyftRunEnd.NotStarted:
                return Failed(ScanFailureCode.SyftFailed, $"Syft could not be started: {ExternalText.Clean(run.StartFailure)}", artifact);
            case SyftRunEnd.TimedOut:
                return Failed(ScanFailureCode.SyftTimedOut, $"Syft did not finish within {Describe(timeLimit)}", artifact);
            default:
                break;
        }

        if (run.ExitCode != 0)
        {
            return Failed(
                ScanFailureCode.SyftFailed,
                string.Create(CultureInfo.InvariantCulture, $"Syft exited with code {run.ExitCode}: {ExternalText.Clean(run.ErrorTail)}"),
                artifact);
        }

        if (run.Output?.Document is not { } document)
        {
            return Failed(ScanFailureCode.SyftOutputUnreadable, $"Syft's output could not be read: {run.Output?.Problem}", artifact);
        }

        SbomProduced produced;
        try
        {
            produced = SyftJsonParser.Map(document);
        }
        catch (ArgumentException exception)
        {
            // IR-03, Q-U3-3: a package the Domain rejects makes the whole output unreadable; nothing is dropped silently.
            return Failed(
                ScanFailureCode.SyftOutputUnreadable,
                $"Syft's output holds a package Airlock cannot represent: {ExternalText.Clean(exception.Message)}",
                artifact);
        }

        return source is FileSource file && file.Artifact.FileName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
            ? WithNuGetPackage(produced, file)
            : produced;
    }

    // IR-04: the package a NuGet package's .nuspec names, alongside the assemblies Syft found, without candidate CPEs.
    private SbomProduced WithNuGetPackage(SbomProduced produced, FileSource file)
    {
        NuspecRead read;
        try
        {
            using var nupkg = File.OpenRead(file.Path);
            read = NuspecReader.Read(nupkg);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            read = new NuspecRead(null, ExternalText.Clean(exception.Message));
        }

        if (read.Package is not { } package)
        {
            logger.NuspecUnreadable(file.Artifact.FileName, read.Problem ?? ExternalText.NoDetails);
            return produced;
        }

        logger.NuspecPackageAdded(package.Name, package.Version, file.Artifact.FileName);
        return produced with { Packages = [.. produced.Packages, new SbomPackage(package, [])] };
    }

    // IR-01: Syft's temporary files go to a marked folder of the run's own, so a killed Syft leaves nothing behind.
    private string? PrepareTemporaryFolder()
    {
        try
        {
            var root = Path.GetFullPath(stagingRoot);
            StagingFolders.CreateDirectory(root);
            var folder = Path.Combine(root, Guid.NewGuid().ToString("D"));
            StagingFolders.CreateMarked(folder);
            return folder;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Syft still runs, with the system's temporary folder.
            logger.TemporaryFolderProblem(ExternalText.Clean(exception.Message));
            return null;
        }
    }

    private async Task RemoveTemporaryFolderAsync(string? folder)
    {
        if (folder is null)
        {
            return;
        }

        try
        {
            await StagingFolders.DeleteAsync(folder, timeProvider, logger, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The startup sweep removes it later (IR-13).
            logger.TemporaryFolderProblem(ExternalText.Clean(exception.Message));
        }
    }
}
