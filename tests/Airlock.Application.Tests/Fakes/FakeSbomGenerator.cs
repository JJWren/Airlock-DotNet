using System.Diagnostics;
using Airlock.Application.Ports;
using Airlock.Domain.Scanning;

namespace Airlock.Application.Tests.Fakes;

/// <summary>
/// Syft, from a table keyed by input: a directory path, an artifact's file name, or "manifests" for the manifests run.
/// Unknown inputs produce no packages.
/// </summary>
internal sealed class FakeSbomGenerator : ISbomGenerator
{
    public const string Manifests = "manifests";

    private readonly Lock gate = new();
    private readonly Dictionary<string, Func<CancellationToken, Task<SbomOutcome>>> answers = new(StringComparer.Ordinal);
    private readonly List<SbomSource> calls = [];

    public SbomToolStatus Status { get; set; } = new(true, "1.55.0", "Syft 1.55.0");

    /// <summary>Completes when a blocking run has begun.</summary>
    public TaskCompletionSource RunStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<SbomSource> Calls
    {
        get
        {
            lock (gate)
            {
                return [.. calls];
            }
        }
    }

    public FakeSbomGenerator Produces(string input, params SbomPackage[] packages) =>
        Answer(input, _ => Task.FromResult<SbomOutcome>(new SbomProduced(packages, false)));

    public FakeSbomGenerator ProducesImage(string input, params SbomPackage[] packages) =>
        Answer(input, _ => Task.FromResult<SbomOutcome>(new SbomProduced(packages, true)));

    public FakeSbomGenerator Fails(string input, ScanFailure failure) =>
        Answer(input, _ => Task.FromResult<SbomOutcome>(new SbomFailed(failure)));

    public FakeSbomGenerator Throws(string input, Exception exception) =>
        Answer(input, _ => Task.FromException<SbomOutcome>(exception));

    /// <summary>A run that lasts until it is cancelled, as a Syft process killed by cancellation would.</summary>
    public FakeSbomGenerator BlocksUntilCancelled(string input) =>
        Answer(input, async token =>
        {
            RunStarted.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new UnreachableException();
        });

    /// <summary>A run that produces its packages once <paramref name="release"/> completes.</summary>
    public FakeSbomGenerator ProducesAfter(string input, Task release, params SbomPackage[] packages) =>
        Answer(input, async token =>
        {
            RunStarted.TrySetResult();
            await release.WaitAsync(token);
            return new SbomProduced(packages, false);
        });

    /// <summary>A run that ignores cancellation until <paramref name="release"/> completes.</summary>
    public FakeSbomGenerator IgnoresCancellation(string input, Task release) =>
        Answer(input, async _ =>
        {
            RunStarted.TrySetResult();
            await release;
            return new SbomProduced([], false);
        });

    public async ValueTask<SbomOutcome> GenerateAsync(SbomSource source, CancellationToken cancellationToken)
    {
        var key = source switch
        {
            FileSource file => file.Artifact.FileName,
            DirectorySource directory when directory.Path.EndsWith("/" + Manifests, StringComparison.Ordinal) => Manifests,
            DirectorySource directory => directory.Path,
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };

        Func<CancellationToken, Task<SbomOutcome>>? answer;
        lock (gate)
        {
            calls.Add(source);
            answers.TryGetValue(key, out answer);
        }

        return answer is null ? new SbomProduced([], false) : await answer(cancellationToken);
    }

    public ValueTask<SbomToolStatus> GetStatusAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Status);

    private FakeSbomGenerator Answer(string input, Func<CancellationToken, Task<SbomOutcome>> answer)
    {
        lock (gate)
        {
            answers[input] = answer;
        }

        return this;
    }
}
