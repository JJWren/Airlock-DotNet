using System.Globalization;
using Airlock.Application.Logging;
using Airlock.Application.Options;
using Airlock.Application.Outcomes;
using Airlock.Application.Ports;
using Airlock.Application.Scanning;
using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;
using Microsoft.Extensions.Logging;

namespace Airlock.Application.UseCases;

/// <summary>
/// Holds the active slot between the reservation and the start of the scan: stages the uploads within the limits,
/// validates the target and launches the scan (AR-03 to AR-07). Disposing it before the start frees everything.
/// </summary>
public sealed class ScanReservation : IAsyncDisposable
{
    internal const string NothingToScanMessage = "Choose a directory or upload at least one artifact";
    internal const string NotBothMessage = "Choose a directory or upload artifacts, not both";
    internal const string StoppedMessage = "Emergency Stop ended the upload before the scan started";

    private const long Mebibyte = 1024 * 1024;
    private const long Gibibyte = 1024 * Mebibyte;

    private readonly ReservationTicket ticket;
    private readonly ScanCoordinator coordinator;
    private readonly IArtifactStaging staging;
    private readonly IHostFileSystem fileSystem;
    private readonly AirlockOptions settings;
    private readonly ILogger logger;
    private IStagingArea? stagingArea;
    private Refusal? refusal;
    private bool started;
    private bool stagingTaken;
    private bool ended;

    internal ScanReservation(
        ReservationTicket ticket,
        ScanCoordinator coordinator,
        IArtifactStaging staging,
        IHostFileSystem fileSystem,
        AirlockOptions settings,
        ILogger logger)
    {
        this.ticket = ticket;
        this.coordinator = coordinator;
        this.staging = staging;
        this.fileSystem = fileSystem;
        this.settings = settings;
        this.logger = logger;
    }

    public ScanId Id => ticket.Id;

    /// <summary>Streams one uploaded file into the scan's staging area under a sanitized name (AR-06, AR-07).</summary>
    public async ValueTask<Outcome<Artifact>> AddArtifactAsync(string fileName, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (Blocked() is { } blocked)
        {
            return Outcome.Refused<Artifact>(blocked);
        }

        if ((stagingArea?.Artifacts.Count ?? 0) >= settings.Limits.MaxArtifactsPerScan)
        {
            return Refuse<Artifact>(
                RefusalCode.TooManyArtifacts,
                string.Create(CultureInfo.InvariantCulture, $"At most {settings.Limits.MaxArtifactsPerScan} files per scan"));
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ticket.StopToken);
        try
        {
            stagingArea ??= await staging.CreateAsync(Id, linked.Token);
            coordinator.MarkReceivingUpload(ticket);
            var remaining = settings.Limits.MaxBytesPerScan - stagingArea.TotalBytes;
            var outcome = await stagingArea.AddAsync(ArtifactName.Sanitize(fileName), content, remaining, linked.Token);
            return outcome is ArtifactStaged staged
                ? Outcome.Success(staged.Artifact.Artifact)
                : Refuse<Artifact>(RefusalCode.TooLarge, $"Uploads are limited to {Size(settings.Limits.MaxBytesPerScan)} per scan");
        }
        catch (OperationCanceledException) when (ticket.IsStopped)
        {
            // AR-28
            return Refuse<Artifact>(RefusalCode.ReservationStopped, StoppedMessage);
        }
    }

    /// <summary>Validates the target — a directory path, or the staged artifacts — and starts the scan (AR-04, AR-05).</summary>
    public ValueTask<Outcome<Scan>> StartAsync(string? directoryPath, CancellationToken cancellationToken)
    {
        if (Blocked() is { } blocked)
        {
            return ValueTask.FromResult(Outcome.Refused<Scan>(blocked));
        }

        var hasPath = !string.IsNullOrWhiteSpace(directoryPath);
        var hasArtifacts = stagingArea is { Artifacts.Count: > 0 };
        if (hasPath == hasArtifacts)
        {
            return ValueTask.FromResult(Refuse<Scan>(RefusalCode.InvalidTarget, hasPath ? NotBothMessage : NothingToScanMessage));
        }

        ScanTarget target;
        if (hasPath)
        {
            var directory = DirectoryTargetFor(directoryPath!, out var problem);
            if (directory is null)
            {
                return ValueTask.FromResult(Refuse<Scan>(RefusalCode.InvalidTarget, problem!));
            }

            target = directory;
        }
        else
        {
            target = new ArtifactsTarget(stagingArea!.Artifacts.Select(staged => staged.Artifact));
        }

        var scan = coordinator.Launch(ticket, target, hasPath ? null : stagingArea);
        if (scan is null)
        {
            return ValueTask.FromResult(Refuse<Scan>(RefusalCode.ReservationStopped, StoppedMessage));
        }

        started = true;
        stagingTaken = !hasPath;
        return ValueTask.FromResult(Outcome.Success(scan));
    }

    /// <summary>Before the start: deletes the staging area and frees the slot; no scan is recorded (AR-03).</summary>
    public async ValueTask DisposeAsync()
    {
        if (ended)
        {
            return;
        }

        ended = true;
        try
        {
            if (stagingArea is not null && !stagingTaken)
            {
                try
                {
                    await stagingArea.DisposeAsync();
                }
                catch (Exception exception)
                {
                    logger.CleanupFailed("delete the staging area", ExternalText.Clean(exception.Message));
                }
            }
        }
        finally
        {
            if (!started)
            {
                coordinator.Release(ticket);
            }

            ticket.Dispose();
        }
    }

    // AR-05
    private DirectoryTarget? DirectoryTargetFor(string path, out string? problem)
    {
        problem = null;
        try
        {
            if (TryCreate(path) is null)
            {
                problem = "Target path is not valid";
                return null;
            }

            if (!fileSystem.IsAbsolute(path))
            {
                problem = "Target path must be absolute";
                return null;
            }

            var fullPath = fileSystem.GetFullPath(path);
            if (!fileSystem.DirectoryExists(fullPath))
            {
                problem = fileSystem.FileExists(fullPath) ? "Target path is a file, not a directory" : "Target path does not exist";
                return null;
            }

            if (settings.AllowedScanRoots.Count > 0)
            {
                var resolved = fileSystem.ResolveLinks(fullPath);
                var inside = settings.AllowedScanRoots.Any(
                    root => fileSystem.IsWithin(resolved, fileSystem.ResolveLinks(fileSystem.GetFullPath(root))));
                if (!inside)
                {
                    problem = "Path is outside the allowed scan roots";
                    return null;
                }
            }

            var target = TryCreate(fullPath);
            problem = target is null ? "Target path is not valid" : null;
            return target;
        }
        catch (ArgumentException)
        {
            problem = "Target path is not valid";
            return null;
        }
    }

    private static DirectoryTarget? TryCreate(string path)
    {
        try
        {
            return new DirectoryTarget(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // AR-03: once refused, or once stopped, every later call gets the same answer.
    private Refusal? Blocked()
    {
        ObjectDisposedException.ThrowIf(ended, this);
        if (started)
        {
            throw new InvalidOperationException("The scan has already started.");
        }

        if (refusal is null && ticket.IsStopped)
        {
            refusal = new Refusal(RefusalCode.ReservationStopped, StoppedMessage);
        }

        return refusal;
    }

    private Outcome<T> Refuse<T>(RefusalCode code, string message)
    {
        refusal = new Refusal(code, message);
        logger.ScanRefused(message);
        return Outcome.Refused<T>(refusal);
    }

    private static string Size(long bytes) =>
        bytes % Gibibyte == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes / Gibibyte} GB")
            : string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)Mebibyte:0.#} MB");
}
