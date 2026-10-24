using Airlock.Application.Ports;
using Airlock.Domain.Scanning;
using Airlock.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Airlock.Infrastructure.Staging;

/// <summary>Creates each scan's staging area, <c>&lt;Staging:Root&gt;/&lt;scan id&gt;/</c> (IR-11).</summary>
public sealed class DiskArtifactStaging(
    IOptions<StagingOptions> options, TimeProvider timeProvider, ILogger<DiskArtifactStaging> logger) : IArtifactStaging
{
    public ValueTask<IStagingArea> CreateAsync(ScanId id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(options.Value.Root);
        StagingFolders.CreateDirectory(root);

        // IR-11: one marked folder per scan, with manifests/ and artifacts/ inside.
        var folder = Path.Combine(root, id.ToString());
        StagingFolders.CreateMarked(folder);
        var manifests = Path.Combine(folder, "manifests");
        var artifacts = Path.Combine(folder, "artifacts");
        StagingFolders.CreateDirectory(manifests);
        StagingFolders.CreateDirectory(artifacts);
        logger.AreaCreated(id);
        return ValueTask.FromResult<IStagingArea>(new DiskStagingArea(folder, manifests, artifacts, timeProvider, logger));
    }
}
