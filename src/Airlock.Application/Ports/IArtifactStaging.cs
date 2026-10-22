using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;

namespace Airlock.Application.Ports;

/// <summary>Creates each scan's own staging folder; implemented by U3.</summary>
public interface IArtifactStaging
{
    ValueTask<IStagingArea> CreateAsync(ScanId id, CancellationToken cancellationToken);
}

/// <summary>One scan's staged uploads. Disposing deletes the folder and everything in it.</summary>
public interface IStagingArea : IAsyncDisposable
{
    /// <summary>The folder Syft scans for the manifests; each manifest sits in its own subfolder under its own name.</summary>
    string ManifestsDirectory { get; }

    IReadOnlyList<StagedArtifact> Artifacts { get; }

    long TotalBytes { get; }

    /// <summary>Streams one file in under an already sanitized name, stopping once it exceeds <paramref name="maxBytes"/>.</summary>
    ValueTask<StagingOutcome> AddAsync(string fileName, Stream content, long maxBytes, CancellationToken cancellationToken);
}

/// <summary>An uploaded file and where it is stored.</summary>
public sealed record StagedArtifact(Artifact Artifact, string Path);

/// <summary>The result of staging one file.</summary>
public abstract record StagingOutcome
{
    private protected StagingOutcome()
    {
    }
}

/// <summary>The file is stored.</summary>
public sealed record ArtifactStaged(StagedArtifact Artifact) : StagingOutcome;

/// <summary>The file was larger than the bytes left for the scan; nothing of it is kept.</summary>
public sealed record ArtifactTooLarge : StagingOutcome;
