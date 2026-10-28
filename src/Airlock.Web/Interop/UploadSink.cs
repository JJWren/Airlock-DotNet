using Microsoft.JSInterop;

namespace Airlock.Web.Interop;

/// <summary>
/// U5-NFR-05: where the page's script reports the files picked, an upload's progress and the server's answer. The tab's
/// session owns it, so an upload carries on whichever page is open, and no call from the server waits for one.
/// </summary>
internal sealed class UploadSink(
    Action<IReadOnlyList<StagedFile>> onStaged, Action<long, long> onProgress, Action<SubmissionAnswer> onCompleted)
{
    [JSInvokable]
    public void Staged(StagedFile[]? files) => onStaged(files ?? []);

    [JSInvokable]
    public void Progress(long sentBytes, long totalBytes) => onProgress(Math.Max(sentBytes, 0), Math.Max(totalBytes, 0));

    [JSInvokable]
    public void Completed(SubmissionAnswer? answer) => onCompleted(answer ?? SubmissionAnswer.NoAnswer);
}

/// <summary>A file the page staged: its handle in the page, its name as the browser gives it, and its size.</summary>
internal sealed record StagedFile(string Id, string Name, long Size);

/// <summary>
/// The server's answer to a submission: 202 with the scan's id, a problem's status, title and detail, or status 0 when
/// no answer came (UR-10).
/// </summary>
internal sealed record SubmissionAnswer(int Status, string? ScanId, string? Title, string? Detail)
{
    public static SubmissionAnswer NoAnswer { get; } = new(0, null, null, null);
}

/// <summary>What the page's script submits: a directory path, or the staged files by their handles.</summary>
internal sealed record ScanSubmissionTarget(string? TargetPath, IReadOnlyList<string>? StagedIds)
{
    public static ScanSubmissionTarget ForPath(string path) => new(path, null);

    public static ScanSubmissionTarget ForArtifacts(IReadOnlyList<string> stagedIds) => new(null, stagedIds);
}

/// <summary>Whether the page started an upload.</summary>
internal enum UploadStart
{
    Started,

    /// <summary>Rescan: the page no longer holds the files that scan uploaded (UR-33).</summary>
    NoFiles,

    /// <summary>The call into the browser failed.</summary>
    Failed,
}