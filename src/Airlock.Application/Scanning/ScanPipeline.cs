using System.Globalization;
using Airlock.Application.Correlation;
using Airlock.Application.Events;
using Airlock.Application.Logging;
using Airlock.Application.Options;
using Airlock.Application.Ports;
using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;
using Airlock.Domain.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Airlock.Application.Scanning;

/// <summary>
/// Runs one scan from SBOM generation to its final status. It is the only writer of that scan: every transition goes
/// to the registry first and then to the scan's hub (AR-21).
/// </summary>
public sealed class ScanPipeline(
    ISbomGenerator sbomGenerator,
    PackageCorrelator correlator,
    IScanRegistry registry,
    IOptions<AirlockOptions> options,
    TimeProvider timeProvider,
    ILogger<ScanPipeline> logger)
{
    internal const string UnexpectedReason = "Unexpected error — see the server log";

    private const int MaxListedPackages = 20;
    private const int MaxShownInput = 1_024;

    private readonly AirlockOptions settings = options.Value;

    internal async Task RunAsync(ScanRun run)
    {
        using var scope = logger.BeginScope(ScanLogScope.For(run.Id));
        try
        {
            LogStart(run.Current.Target);
            var packages = await GenerateSbomAsync(run);
            if (packages is not null)
            {
                var correlations = await CorrelateAsync(run, packages);
                Finish(run, packages.Count, correlations);
            }
        }
        catch (OperationCanceledException) when (run.Token.IsCancellationRequested)
        {
            End(run, Stopped(run.Cause));
        }
        catch (Exception exception)
        {
            // AR-31: whatever goes wrong, the scan fails; it never ends clean.
            logger.ScanCrashed(exception);
            End(run, scan => scan.Fail(new ScanFailure(ScanFailureCode.Unexpected, UnexpectedReason), timeProvider.GetUtcNow()));
        }
        finally
        {
            await CleanUpAsync(run);
        }
    }

    private void LogStart(ScanTarget target)
    {
        if (target is ArtifactsTarget artifacts)
        {
            logger.ScanStarting("uploaded artifacts");
            logger.ProcessingArtifacts(artifacts.Artifacts.Count);
        }
        else if (target is DirectoryTarget directory)
        {
            logger.ScanStarting(directory.Path);
        }
    }

    // AR-08 to AR-12
    private async Task<IReadOnlyList<SbomPackage>?> GenerateSbomAsync(ScanRun run)
    {
        List<SbomPackage> found = [];
        foreach (var (source, input) in Runs(run))
        {
            run.Token.ThrowIfCancellationRequested();
            logger.GeneratingSbom(input);
            var outcome = await sbomGenerator.GenerateAsync(source, run.Token);
            if (outcome is SbomFailed failed)
            {
                // AR-09: the scan fails at once, naming its input; the remaining runs are not started.
                var failure = new ScanFailure(failed.Failure.Code, failed.Failure.Reason, failed.Failure.Input ?? Shown(input));
                logger.SyftFailed(failure.Reason);
                End(run, scan => scan.Fail(failure, timeProvider.GetUtcNow()));
                logger.ScanFailed(failure.Reason);
                return null;
            }

            var produced = (SbomProduced)outcome;
            if (produced.Packages.Count == 0)
            {
                // AR-10
                logger.NoPackagesIdentified(input);
                Warn(run, ScanWarningCode.NoPackagesIdentified, $"No packages identified in {Shown(input)}", input);
            }
            else
            {
                logger.PackagesIdentified(produced.Packages.Count, input);
            }

            if (produced.IsContainerImage)
            {
                // AR-11
                logger.BackportsNotConsidered(input);
                Warn(
                    run,
                    ScanWarningCode.DistributionBackportsNotConsidered,
                    $"Distribution backports are not considered for {Shown(input)}: versions are checked as the image reports them",
                    input);
            }

            found.AddRange(produced.Packages);
        }

        var merged = PackageMerge.Merge(found);
        logger.PackagesToVet(merged.Count);
        return merged;
    }

    // AR-08: the directory in one run; the manifests together, then every other artifact on its own, in upload order.
    private static IEnumerable<(SbomSource Source, string Input)> Runs(ScanRun run)
    {
        if (run.Current.Target is DirectoryTarget directory)
        {
            yield return (new DirectorySource(directory.Path), directory.Path);
            yield break;
        }

        var staging = run.Staging ?? throw new InvalidOperationException("An artifacts target needs its staging area.");
        var manifests = staging.Artifacts.Where(staged => staged.Artifact.Kind == ArtifactKind.Manifest).ToList();
        if (manifests.Count > 0)
        {
            yield return (new DirectorySource(staging.ManifestsDirectory), string.Join(", ", manifests.Select(m => m.Artifact.FileName)));
        }

        foreach (var other in staging.Artifacts.Where(staged => staged.Artifact.Kind != ArtifactKind.Manifest))
        {
            yield return (new FileSource(other.Path, other.Artifact), other.Artifact.FileName);
        }
    }

    // AR-13 to AR-18, AR-22
    private async Task<IReadOnlyList<PackageCorrelation>> CorrelateAsync(ScanRun run, IReadOnlyList<SbomPackage> packages)
    {
        Transition(run, scan => scan.EnterPhase(ScanPhase.Correlation), scan => new PhaseChanged(scan.Id, scan.Phase));
        Transition(run, scan => scan.RecordProgress(0, packages.Count), Progress);

        var results = new PackageCorrelation?[packages.Count];
        var decided = 0;
        var progressGate = new Lock();
        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = settings.Correlation.MaxParallelism,
            CancellationToken = run.Token,
        };
        await Parallel.ForEachAsync(Enumerable.Range(0, packages.Count), parallel, async (index, token) =>
        {
            results[index] = await correlator.CorrelateAsync(packages[index], run.Breaker, token);
            lock (progressGate)
            {
                var count = ++decided;
                Transition(run, scan => scan.RecordProgress(count, packages.Count), Progress);
            }
        });

        // U2-NFR-06: the scan finishes only when every package has its verdict.
        return [.. results.Select(result => result ?? throw new InvalidOperationException("A package was left without a verdict."))];
    }

    // AR-19, AR-20
    private void Finish(ScanRun run, int totalPackages, IReadOnlyList<PackageCorrelation> correlations)
    {
        Transition(run, scan => scan.EnterPhase(ScanPhase.Summary), scan => new PhaseChanged(scan.Id, scan.Phase));
        AddResultWarnings(run, correlations);

        var vulnerable = correlations.OfType<VulnerableCorrelation>().Select(correlation => correlation.Result).ToList();
        var unverified = correlations.OfType<UnverifiedCorrelation>().Select(correlation => correlation.Result).ToList();
        End(run, scan => scan.Finish(totalPackages, vulnerable, unverified, timeProvider.GetUtcNow()));

        if (run.Current.Summary is { } summary)
        {
            logger.ScanCompleted(summary.VulnerablePackages, summary.UnverifiedPackages, summary.CleanPackages, summary.TotalPackages);
        }
    }

    // AR-19: the packages not product-matched in one warning, then the unscored CVEs package by package.
    private void AddResultWarnings(ScanRun run, IReadOnlyList<PackageCorrelation> correlations)
    {
        var ordered = correlations
            .OrderBy(correlation => correlation.Package.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(correlation => correlation.Package.Version, StringComparer.OrdinalIgnoreCase)
            .ThenBy(correlation => correlation.Package.Type, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var notMatched = ordered
            .OfType<CleanCorrelation>()
            .Where(correlation => correlation.Method is null)
            .Select(correlation => correlation.Package.ToString())
            .ToList();
        if (notMatched.Count > 0)
        {
            var listed = string.Join(", ", notMatched.Take(MaxListedPackages));
            var more = notMatched.Count > MaxListedPackages
                ? string.Create(CultureInfo.InvariantCulture, $" and {notMatched.Count - MaxListedPackages} more")
                : string.Empty;
            Warn(run, ScanWarningCode.NotProductMatched, $"Not product-matched, name too generic: {listed}{more}", null);
        }

        foreach (var correlation in ordered)
        {
            IReadOnlyList<string> unscored = correlation switch
            {
                CleanCorrelation clean => clean.UnscoredCveIds,
                VulnerableCorrelation vulnerable => vulnerable.UnscoredCveIds,
                _ => [],
            };
            if (unscored.Count > 0)
            {
                var package = correlation.Package.ToString();
                Warn(run, ScanWarningCode.CvesWithoutSeverity, $"{package}: {string.Join(", ", unscored)} not yet scored by NVD", package);
            }
        }
    }

    private void Warn(ScanRun run, ScanWarningCode code, string message, string? input)
    {
        var warning = new ScanWarning(
            code,
            ExternalText.Clean(message, DomainLimits.Message),
            input is null ? null : ExternalText.Clean(input, DomainLimits.Path));
        Transition(run, scan => scan.AddWarning(warning), _ => null);
    }

    private Func<Scan, Scan> Stopped(StopCause cause)
    {
        if (cause == StopCause.TimeLimit)
        {
            // AR-29
            var reason = string.Create(
                CultureInfo.InvariantCulture, $"Scan exceeded the {settings.Timeouts.Scan.TotalMinutes:0.##}-minute limit");
            logger.ScanFailed(reason);
            return scan => scan.Fail(new ScanFailure(ScanFailureCode.ScanTimedOut, reason), timeProvider.GetUtcNow());
        }

        // AR-27, AR-28, AR-30
        var cancellation = cause switch
        {
            StopCause.EmergencyStop => "Emergency Stop",
            StopCause.ServerShutdown => "Server shutting down",
            _ => "Cancelled by request",
        };
        return scan => scan.Cancel(cancellation, timeProvider.GetUtcNow());
    }

    /// <summary>
    /// Records a final version. When that fails, the scan ends Failed instead: a status that could not be recorded is
    /// never reported as a success (U2-NFR-06, U2-NFR-11).
    /// </summary>
    private void End(ScanRun run, Func<Scan, Scan> change)
    {
        if (run.Current.IsFinished)
        {
            return;
        }

        try
        {
            Transition(run, change, _ => null);
            return;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.CleanupFailed("record the final status", ExternalText.Clean(exception.Message));
        }

        var failed = run.Current.Fail(new ScanFailure(ScanFailureCode.Unexpected, UnexpectedReason), timeProvider.GetUtcNow());
        try
        {
            registry.Update(run.Id, _ => failed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.CleanupFailed("record the failure", ExternalText.Clean(exception.Message));
        }

        run.Current = failed;
    }

    // AR-21
    private void Transition(ScanRun run, Func<Scan, Scan> change, Func<Scan, ScanEvent?> eventFor)
    {
        var next = change(run.Current);
        registry.Update(run.Id, _ => next);
        run.Current = next;
        run.Hub.Publish(next, eventFor(next));
    }

    // AR-32: the finished event and the end of every subscription, then the staging area; each step on its own.
    private async Task CleanUpAsync(ScanRun run)
    {
        if (!run.Current.IsFinished)
        {
            run.Current = run.Current.Fail(new ScanFailure(ScanFailureCode.Unexpected, UnexpectedReason), timeProvider.GetUtcNow());
        }

        try
        {
            run.Hub.Complete(run.Current);
        }
        catch (Exception exception)
        {
            logger.CleanupFailed("complete the event stream", ExternalText.Clean(exception.Message));
        }

        if (run.Staging is { } staging)
        {
            try
            {
                await staging.DisposeAsync();
            }
            catch (Exception exception)
            {
                logger.CleanupFailed("delete the staging area", ExternalText.Clean(exception.Message));
            }
        }

        if (run.Current.Status == ScanStatus.Cancelled)
        {
            logger.ScanAborted();
        }
    }

    private static string Shown(string input) => ExternalText.Clean(input, MaxShownInput);

    private static PackagesCorrelated Progress(Scan scan) => new(scan.Id, scan.Progress!);
}
