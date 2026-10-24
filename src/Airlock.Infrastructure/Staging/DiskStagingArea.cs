using System.Buffers;
using System.Globalization;
using Airlock.Application.Ports;
using Airlock.Domain.Targets;
using Microsoft.Extensions.Logging;

namespace Airlock.Infrastructure.Staging;

/// <summary>One scan's staged uploads on disk; disposing it deletes the folder and everything in it (IR-11 to IR-13).</summary>
internal sealed class DiskStagingArea(
    string folder, string manifestsDirectory, string artifactsDirectory, TimeProvider timeProvider, ILogger logger) : IStagingArea
{
    /// <summary>One pooled buffer of this size copies every upload (U3-NFR-05).</summary>
    public const int BufferSize = 80 * 1024;

    private readonly List<StagedArtifact> artifacts = [];
    private bool disposed;

    public string ManifestsDirectory => manifestsDirectory;

    public IReadOnlyList<StagedArtifact> Artifacts => [.. artifacts];

    public long TotalBytes { get; private set; }

    // IR-12
    public async ValueTask<StagingOutcome> AddAsync(
        string fileName, Stream content, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(content);
        ObjectDisposedException.ThrowIf(disposed, this);

        // IR-12: the name arrives sanitized (U2 AR-07); the area still refuses anything that would leave its folder.
        var kind = new Artifact(fileName, 0).Kind;
        var position = artifacts.Count.ToString(CultureInfo.InvariantCulture);
        var slot = Path.Combine(kind == ArtifactKind.Manifest ? manifestsDirectory : artifactsDirectory, position);
        var path = Path.GetFullPath(Path.Combine(slot, StoredName(fileName)));
        if (!path.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Must stay inside the staging area.", nameof(fileName));
        }

        StagingFolders.CreateDirectory(slot);
        long written;
        try
        {
            written = await CopyAsync(content, path, maxBytes, cancellationToken);
        }
        catch
        {
            Directory.Delete(slot, recursive: true);
            throw;
        }

        if (written > maxBytes)
        {
            // IR-12: once the file passes the bytes left for the scan, nothing of it is kept.
            Directory.Delete(slot, recursive: true);
            logger.ArtifactTooLarge(fileName);
            return new ArtifactTooLarge();
        }

        var staged = new StagedArtifact(new Artifact(fileName, written), path);
        artifacts.Add(staged);
        TotalBytes += written;
        logger.ArtifactStaged(fileName, written);
        return new ArtifactStaged(staged);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        await StagingFolders.DeleteAsync(folder, timeProvider, logger, CancellationToken.None);
    }

    // IR-12, Q-S0-3: Syft reads wheels and NuGet packages only as .zip; the Artifact keeps the uploaded name.
    private static string StoredName(string fileName) =>
        fileName.EndsWith(".whl", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
            ? fileName + ".zip"
            : fileName;

    /// <summary>Copies at most one byte past <paramref name="maxBytes"/>, so an oversized upload is never read to its end.</summary>
    private static async Task<long> CopyAsync(Stream content, string path, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            await using var file = StagingFolders.OpenNew(path);
            long written = 0;
            int read;
            while ((read = await content.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)) > 0)
            {
                written += read;
                if (written > maxBytes)
                {
                    break;
                }

                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            return written;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
