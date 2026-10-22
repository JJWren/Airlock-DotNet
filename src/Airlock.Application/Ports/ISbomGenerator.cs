using Airlock.Domain.Intelligence;
using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;

namespace Airlock.Application.Ports;

/// <summary>Builds SBOMs; implemented by U3's Syft runner. One call is one Syft run.</summary>
public interface ISbomGenerator
{
    /// <summary>Runs Syft on the source. Cancelling kills the run and throws <see cref="OperationCanceledException"/>.</summary>
    ValueTask<SbomOutcome> GenerateAsync(SbomSource source, CancellationToken cancellationToken);

    ValueTask<SbomToolStatus> GetStatusAsync(CancellationToken cancellationToken);
}

/// <summary>What one Syft run scans.</summary>
public abstract record SbomSource
{
    private protected SbomSource()
    {
    }
}

/// <summary>A <c>dir:</c> run over a full directory path: a directory target, or a staging area's manifests.</summary>
public sealed record DirectorySource(string Path) : SbomSource;

/// <summary>A run over one staged artifact file.</summary>
public sealed record FileSource(string Path, Artifact Artifact) : SbomSource;

/// <summary>A package as Syft reported it, with its candidate CPEs in Syft's order.</summary>
public sealed record SbomPackage(Package Package, IReadOnlyList<CandidateCpe> CandidateCpes);

/// <summary>The result of one Syft run.</summary>
public abstract record SbomOutcome
{
    private protected SbomOutcome()
    {
    }
}

/// <summary>A successful run; <paramref name="IsContainerImage"/> is set when Syft identified a container image.</summary>
public sealed record SbomProduced(IReadOnlyList<SbomPackage> Packages, bool IsContainerImage) : SbomOutcome;

/// <summary>A failed run: a non-zero exit, unreadable output or the run's time limit, naming its input.</summary>
public sealed record SbomFailed(ScanFailure Failure) : SbomOutcome;

/// <summary>Whether Syft can run, its version, and the reason when it cannot.</summary>
public sealed record SbomToolStatus(bool Available, string? Version, string Detail);
