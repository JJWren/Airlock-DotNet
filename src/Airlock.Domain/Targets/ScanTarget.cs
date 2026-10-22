using System.Collections.Immutable;
using Airlock.Domain.Text;

namespace Airlock.Domain.Targets;

/// <summary>What a scan inspects: a directory path, or a set of uploaded artifacts.</summary>
public abstract record ScanTarget
{
    private protected ScanTarget()
    {
    }
}

/// <summary>A directory on the Airlock host; U2 checks that it exists and is fully qualified.</summary>
public sealed record DirectoryTarget : ScanTarget
{
    public DirectoryTarget(string path) => Path = DomainText.Require(path, DomainLimits.Path, nameof(path));

    public string Path { get; }
}

/// <summary>One or more uploaded artifacts.</summary>
public sealed record ArtifactsTarget : ScanTarget
{
    public ArtifactsTarget(IEnumerable<Artifact> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        var items = artifacts
            .Select(artifact => artifact ?? throw new ArgumentException("Must not contain null.", nameof(artifacts)))
            .ToImmutableArray();
        if (items.IsEmpty)
        {
            throw new ArgumentException("At least one artifact is required.", nameof(artifacts));
        }

        Artifacts = items;
    }

    public IReadOnlyList<Artifact> Artifacts { get; }
}
