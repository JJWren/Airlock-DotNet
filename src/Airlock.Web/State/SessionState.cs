using System.Collections.Immutable;
using Airlock.Application.Events;
using Airlock.Application.UseCases;
using Airlock.Domain.Scanning;

namespace Airlock.Web.State;

/// <summary>What changed in a tab's session; each component redraws for its own kinds only (U5-NFR-02).</summary>
[Flags]
internal enum SessionChange
{
    None = 0,
    Lines = 1 << 0,
    Scan = 1 << 1,
    Slot = 1 << 2,
    Health = 1 << 3,
    Notice = 1 << 4,
    Staging = 1 << 5,
    Upload = 1 << 6,
    TimeZone = 1 << 7,
}

/// <summary>A tab's session at one moment, replaced whole and never changed in place (U5 domain entities, NFR Design).</summary>
internal sealed record SessionState(
    string DirectoryPath,
    ImmutableList<StagedArtifact> Staged,
    UploadProgress? Upload,
    bool Submitting,
    ActiveScanState Active,
    Scan? Current,
    ImmutableHashSet<ScanId> Submitted,
    ImmutableList<TerminalLine> Lines,
    SystemHealth? Health,
    TimeZoneInfo? TimeZone,
    InlineNotice? Notice)
{
    /// <summary>UR-06: the Local Directory Path's first value.</summary>
    public const string DefaultDirectoryPath = "/uploads";

    public static SessionState Initial { get; } =
        new(DefaultDirectoryPath, [], null, false, ActiveScanState.Idle, null, [], [], null, null, null);

    /// <summary>UR-13: a reservation holds the slot, and no scan of it is followed yet.</summary>
    public bool IsPreparing => Active.ScanId is { } id && Current?.Id != id;

    /// <summary>UR-09: anything holds the server's slot, or this tab is submitting.</summary>
    public bool SlotHeld => Active.IsActive || Submitting;

    /// <summary>UR-09: ONLINE, the slot free and this tab not uploading.</summary>
    public bool CanExecute => Health?.Status == HealthStatus.Online && !SlotHeld;

    public long StagedBytes => Staged.Sum(artifact => artifact.SizeBytes);
}

/// <summary>A scan the tab followed has finished; <see cref="SubmittedHere"/> when this tab submitted it (UR-14).</summary>
internal sealed record ScanEnding(Scan Scan, bool SubmittedHere);

/// <summary>A file the page holds for the next submission; the same file when name and size are equal (UR-07).</summary>
internal sealed record StagedArtifact(string Id, string FileName, long SizeBytes);

/// <summary>This tab's upload: the request body's bytes sent so far and in all (UR-11).</summary>
internal sealed record UploadProgress(long SentBytes, long TotalBytes)
{
    /// <summary>Rounded down, from 0 to 100.</summary>
    public int Percent => TotalBytes <= 0 ? 0 : (int)Math.Clamp(SentBytes * 100 / TotalBytes, 0, 100);
}

/// <summary>One line of the terminal, made from a Live Log entry; its sequence number is its key (UR-17, U5-NFR-03).</summary>
internal sealed record TerminalLine(long Sequence, DateTimeOffset Timestamp, Microsoft.Extensions.Logging.LogLevel Level, string Message, ScanId? ScanId);

/// <summary>The kinds of inline notice (UR-12).</summary>
internal enum NoticeKind
{
    Refusal,
    Interrupted,
    Failed,
    Cancelled,
    Finished,
    RescanHint,
}

/// <summary>A tab's one inline notice; a finished scan's names the scan to view (UR-12).</summary>
internal sealed record InlineNotice(NoticeKind Kind, string Text, ScanId? ScanId)
{
    /// <summary>What a screen reader hears, the link of a finished scan included.</summary>
    public string Announcement => Kind == NoticeKind.Finished ? Text + " — View report" : Text;
}

/// <summary>How a submission ended for the tab that made it.</summary>
internal enum SubmissionKind
{
    Accepted,
    Refused,
    Interrupted,
    NoFiles,
    NotSubmitted,
}

/// <summary>A submission's end, with the accepted scan's id.</summary>
internal readonly record struct SubmissionOutcome(SubmissionKind Kind, ScanId? ScanId = null);