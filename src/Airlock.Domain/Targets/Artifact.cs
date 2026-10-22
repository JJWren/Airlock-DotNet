using Airlock.Domain.Text;

namespace Airlock.Domain.Targets;

public enum ArtifactKind
{
    Manifest,
    Other,
}

/// <summary>A file submitted for a scan; a Manifest when its name is a known dependency declaration (BR-12).</summary>
public sealed record Artifact
{
    public Artifact(string fileName, long sizeBytes)
    {
        FileName = DomainText.Require(fileName, DomainLimits.FileName, nameof(fileName));
        if (FileName.AsSpan().IndexOfAny('/', '\\', ':') >= 0 || FileName is "." or "..")
        {
            throw new ArgumentException("Must be a plain file name without directory parts.", nameof(fileName));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);
        SizeBytes = sizeBytes;
        Kind = ManifestCatalog.IsManifest(FileName) ? ArtifactKind.Manifest : ArtifactKind.Other;
    }

    public string FileName { get; }

    public long SizeBytes { get; }

    public ArtifactKind Kind { get; }
}
