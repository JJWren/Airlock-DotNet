using Airlock.Application.Ports;
using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;

namespace Airlock.Application.Tests.Fakes;

/// <summary>Staging in memory: each scan's area records its files, its byte count and whether it was deleted.</summary>
internal sealed class FakeArtifactStaging : IArtifactStaging
{
    private readonly List<FakeStagingArea> created = [];

    public IReadOnlyList<FakeStagingArea> Created => created;

    /// <summary>When set, deleting a staging area throws it (fault injection).</summary>
    public Exception? DisposeFailure { get; set; }

    public ValueTask<IStagingArea> CreateAsync(ScanId id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var area = new FakeStagingArea(id, DisposeFailure);
        created.Add(area);
        return ValueTask.FromResult<IStagingArea>(area);
    }
}

internal sealed class FakeStagingArea(ScanId id, Exception? disposeFailure) : IStagingArea
{
    private readonly List<StagedArtifact> artifacts = [];

    public string ManifestsDirectory => $"/staging/{id}/{FakeSbomGenerator.Manifests}";

    public IReadOnlyList<StagedArtifact> Artifacts => artifacts;

    public long TotalBytes { get; private set; }

    public bool Disposed { get; private set; }

    public IReadOnlyList<string> ReceivedNames => [.. artifacts.Select(staged => staged.Artifact.FileName)];

    public async ValueTask<StagingOutcome> AddAsync(string fileName, Stream content, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        long total = 0;
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                return new ArtifactTooLarge();
            }
        }

        var artifact = new Artifact(fileName, total);
        var folder = artifact.Kind == ArtifactKind.Manifest ? ManifestsDirectory : $"/staging/{id}/artifacts";
        var staged = new StagedArtifact(artifact, $"{folder}/{artifacts.Count}/{fileName}");
        artifacts.Add(staged);
        TotalBytes += total;
        return new ArtifactStaged(staged);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return disposeFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(disposeFailure);
    }
}
