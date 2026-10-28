using System.Collections.Immutable;
using Airlock.Application.Events;
using Airlock.Application.Logging;
using Airlock.Application.Options;
using Airlock.Application.UseCases;
using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;
using Airlock.Web.Interop;
using Airlock.Web.Logging;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace Airlock.Web.State;

/// <summary>
/// A browser tab's session: one per circuit, shared by every page of the tab (UR-03). Its state is one immutable
/// snapshot, replaced under a lock. Four feeds write to it — the active slot, the followed scan, the Live Log and health
/// every 10 seconds — and so does the page's script, through the session's upload sink. Disposing it, as Blazor does
/// when the circuit ends, ends them all (U5-NFR-03).
/// </summary>
internal sealed class ScanSession : IAsyncDisposable
{
    /// <summary>The terminal's lines, as many as a Live Log subscriber's buffer (U5-NFR-03).</summary>
    public const int MaxLines = 1_000;

    public const string InterruptedMessage = "Upload interrupted — check the connection and try again";

    /// <summary>UR-04: as often as the original polled.</summary>
    public static readonly TimeSpan HealthInterval = TimeSpan.FromSeconds(10);

    private readonly WatchActiveScan watchActive;
    private readonly WatchScan watchScan;
    private readonly WatchLiveLog watchLiveLog;
    private readonly GetSystemHealth getHealth;
    private readonly GetScan getScan;
    private readonly EmergencyStop emergencyStop;
    private readonly AirlockJs js;
    private readonly TimeProvider clock;
    private readonly ILogger<ScanSession> logger;
    private readonly CancellationTokenSource stopping = new();
    private readonly Lock gate = new();
    private readonly List<Task> feeds = [];
    private SessionState state = SessionState.Initial;
    private TaskCompletionSource<SubmissionAnswer>? pendingAnswer;
    private CancellationTokenSource? followStop;
    private Task? followTask;
    private ScanId? followed;
    private long nextSequence;
    private bool started;
    private bool disposed;

    public ScanSession(
        WatchActiveScan watchActive,
        WatchScan watchScan,
        WatchLiveLog watchLiveLog,
        GetSystemHealth getHealth,
        GetScan getScan,
        EmergencyStop emergencyStop,
        IOptions<AirlockOptions> options,
        AirlockJs js,
        TimeProvider clock,
        ILogger<ScanSession> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.watchActive = watchActive;
        this.watchScan = watchScan;
        this.watchLiveLog = watchLiveLog;
        this.getHealth = getHealth;
        this.getScan = getScan;
        this.emergencyStop = emergencyStop;
        this.js = js;
        this.clock = clock;
        this.logger = logger;
        Limits = options.Value.Limits;
        Sink = DotNetObjectReference.Create(new UploadSink(OnStaged, OnProgress, OnCompleted));
    }

    /// <summary>The tab's components redraw for the kinds of change they show (U5-NFR-02).</summary>
    public event Action<SessionChange>? Changed;

    /// <summary>The followed scan finished; <c>MainLayout</c> opens its report or shows a snackbar (UR-14).</summary>
    public event Action<ScanEnding>? ScanEnded;

    /// <summary>Text for screen readers: every notice, and the snackbars the pages show (U5-NFR-07).</summary>
    public event Action<string>? Announced;

    public SessionState State
    {
        get
        {
            lock (gate)
            {
                return state;
            }
        }
    }

    /// <summary>The server's limits, for the checks before uploading (UR-08).</summary>
    public LimitsOptions Limits { get; }

    public DotNetObjectReference<UploadSink> Sink { get; }

    /// <summary>Starts the four feeds and learns the browser's time zone, once per circuit (UR-03, UR-34).</summary>
    public async Task StartAsync()
    {
        lock (gate)
        {
            if (started || disposed)
            {
                return;
            }

            started = true;
            var token = stopping.Token;
            feeds.Add(Task.Run(() => FollowSlotAsync(token), CancellationToken.None));
            feeds.Add(Task.Run(() => FollowLiveLogAsync(token), CancellationToken.None));
            feeds.Add(Task.Run(() => ReadHealthAsync(token), CancellationToken.None));
        }

        var zone = UiFormat.ResolveZone(await js.TimeZoneAsync());
        Update(current => current with { TimeZone = zone }, SessionChange.TimeZone);
        await js.WatchConnectionAsync();
    }

    public void SetDirectoryPath(string? path) => Update(current => current with { DirectoryPath = path ?? string.Empty }, SessionChange.None);

    public async Task UnstageAsync(string stagedId)
    {
        await js.UnstageAsync(stagedId);
        Update(current => current with { Staged = current.Staged.RemoveAll(artifact => artifact.Id == stagedId) }, SessionChange.Staging);
    }

    /// <summary>UR-08 to UR-11: the checks before uploading, then the submission through the page's script.</summary>
    public async Task<SubmissionOutcome> ExecuteAsync()
    {
        var current = State;
        if (!current.CanExecute)
        {
            return new SubmissionOutcome(SubmissionKind.NotSubmitted);
        }

        // Q-U5-1: while any file is staged, only the artifacts are sent.
        if (current.Staged.IsEmpty)
        {
            var path = ScanSubmissionTarget.ForPath(current.DirectoryPath);
            return await SubmitAsync(() => js.SubmitScanAsync(path, Sink), uploadBytes: null, clearFirst: true);
        }

        if (UploadLimits.Check(current.Staged.Count, current.StagedBytes, Limits) is { } refusal)
        {
            SetNotice(new InlineNotice(NoticeKind.Refusal, refusal.Message, null));
            return new SubmissionOutcome(SubmissionKind.Refused);
        }

        var artifacts = ScanSubmissionTarget.ForArtifacts([.. current.Staged.Select(artifact => artifact.Id)]);
        return await SubmitAsync(() => js.SubmitScanAsync(artifacts, Sink), current.StagedBytes, clearFirst: true);
    }

    /// <summary>UR-33: the shown scan's target again, under the rules of Execute.</summary>
    public async Task<SubmissionOutcome> RescanAsync(Scan scan)
    {
        ArgumentNullException.ThrowIfNull(scan);
        if (!State.CanExecute)
        {
            return new SubmissionOutcome(SubmissionKind.NotSubmitted);
        }

        if (scan.Target is DirectoryTarget directory)
        {
            var path = ScanSubmissionTarget.ForPath(directory.Path);
            return await SubmitAsync(() => js.SubmitScanAsync(path, Sink), uploadBytes: null, clearFirst: true);
        }

        var artifacts = ((ArtifactsTarget)scan.Target).Artifacts;
        var outcome = await SubmitAsync(
            () => js.ResubmitScanAsync(scan.Id, Sink), artifacts.Sum(artifact => artifact.SizeBytes), clearFirst: false);
        if (outcome.Kind == SubmissionKind.NoFiles)
        {
            var names = string.Join(", ", artifacts.Select(artifact => artifact.FileName));
            SetNotice(new InlineNotice(NoticeKind.RescanHint, "Re-select the artifacts to rescan: " + names, scan.Id));
        }

        return outcome;
    }

    /// <summary>UR-20: ends whatever holds the active slot, whoever started it.</summary>
    public async Task<EmergencyStopResult> StopAsync(CancellationToken cancellationToken) =>
        await emergencyStop.ExecuteAsync(cancellationToken);

    public void DismissNotice() => Update(current => current with { Notice = null }, SessionChange.Notice);

    public void Announce(string text) => Announced?.Invoke(text);

    public async ValueTask DisposeAsync()
    {
        Task[] running;
        TaskCompletionSource<SubmissionAnswer>? pending;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            running = followTask is null ? [.. feeds] : [.. feeds, followTask];
            pending = pendingAnswer;
        }

        // The followed scan's subscription is linked to this token, so it ends with the feeds.
        await stopping.CancelAsync();
        pending?.TrySetResult(SubmissionAnswer.NoAnswer);
        await Task.WhenAll(running);
        Sink.Dispose();
        stopping.Dispose();
    }

    private async Task<SubmissionOutcome> SubmitAsync(Func<ValueTask<UploadStart>> start, long? uploadBytes, bool clearFirst)
    {
        var answer = new TaskCompletionSource<SubmissionAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (disposed || pendingAnswer is not null)
            {
                return new SubmissionOutcome(SubmissionKind.NotSubmitted);
            }

            pendingAnswer = answer;
        }

        // UR-10, UR-18: Execute clears the notice and the terminal; Rescan of files waits until the page holds them.
        var upload = uploadBytes is { } bytes ? new UploadProgress(0, bytes) : null;
        Update(
            current => (clearFirst ? Cleared(current) : current) with { Submitting = true, Upload = upload },
            SessionChange.Upload | (clearFirst ? SessionChange.Notice | SessionChange.Lines : SessionChange.None));

        var started = await start();
        if (started != UploadStart.Started)
        {
            EndSubmission(answer);
            if (started == UploadStart.Failed)
            {
                SetNotice(new InlineNotice(NoticeKind.Interrupted, InterruptedMessage, null));
                return new SubmissionOutcome(SubmissionKind.Interrupted);
            }

            return new SubmissionOutcome(SubmissionKind.NoFiles);
        }

        if (!clearFirst)
        {
            Update(Cleared, SessionChange.Notice | SessionChange.Lines);
        }

        var result = await answer.Task;
        return Conclude(answer, result);
    }

    private SubmissionOutcome Conclude(TaskCompletionSource<SubmissionAnswer> answer, SubmissionAnswer result)
    {
        if (result.Status == StatusCodes.Status202Accepted && ScanId.TryParse(result.ScanId, out var id))
        {
            // UR-14: should the scan have finished before its 202 arrived, this tab still opens its report.
            var finishedMeanwhile = Update(
                current =>
                {
                    var finished = current.Current is { IsFinished: true } scan && scan.Id == id ? current.Current : null;
                    return (current with { Submitting = false, Upload = null, Submitted = current.Submitted.Add(id) }, finished);
                },
                SessionChange.Upload);
            lock (gate)
            {
                pendingAnswer = pendingAnswer == answer ? null : pendingAnswer;
            }

            if (finishedMeanwhile is not null)
            {
                ScanEnded?.Invoke(new ScanEnding(finishedMeanwhile, SubmittedHere: true));
            }

            return new SubmissionOutcome(SubmissionKind.Accepted, id);
        }

        EndSubmission(answer);
        if (result.Status == 0)
        {
            SetNotice(new InlineNotice(NoticeKind.Interrupted, InterruptedMessage, null));
            return new SubmissionOutcome(SubmissionKind.Interrupted);
        }

        // UR-10: a problem's detail, such as "A scan is already in progress".
        var text = ExternalText.Clean(result.Detail ?? result.Title ?? $"The server answered {result.Status}");
        SetNotice(new InlineNotice(NoticeKind.Refusal, text, null));
        return new SubmissionOutcome(SubmissionKind.Refused);
    }

    private void EndSubmission(TaskCompletionSource<SubmissionAnswer> answer)
    {
        lock (gate)
        {
            pendingAnswer = pendingAnswer == answer ? null : pendingAnswer;
        }

        Update(current => current with { Submitting = false, Upload = null }, SessionChange.Upload);
    }

    private static SessionState Cleared(SessionState current) => current with { Notice = null, Lines = [] };

    private void SetNotice(InlineNotice notice)
    {
        Update(current => current with { Notice = notice }, SessionChange.Notice);
        Announce(notice.Announcement);
    }

    // UR-07: the page skips a file whose name and size match a staged one; the session checks again.
    private void OnStaged(IReadOnlyList<StagedFile> files) =>
        Update(
            current =>
            {
                var staged = current.Staged;
                foreach (var file in files)
                {
                    if (file is { Id: not null, Name: not null }
                        && !staged.Exists(artifact => artifact.FileName == file.Name && artifact.SizeBytes == file.Size))
                    {
                        staged = staged.Add(new StagedArtifact(file.Id, file.Name, Math.Max(file.Size, 0)));
                    }
                }

                return current with { Staged = staged };
            },
            SessionChange.Staging);

    private void OnProgress(long sentBytes, long totalBytes) =>
        Update(
            current => current.Submitting ? current with { Upload = new UploadProgress(sentBytes, totalBytes) } : current,
            SessionChange.Upload);

    private void OnCompleted(SubmissionAnswer answer)
    {
        TaskCompletionSource<SubmissionAnswer>? pending;
        lock (gate)
        {
            pending = pendingAnswer;
        }

        pending?.TrySetResult(answer);
    }

    // UR-13, UR-18: the slot's holder is followed, whoever started it; a new holder starts the terminal afresh.
    private async Task FollowSlotAsync(CancellationToken token)
    {
        try
        {
            var first = true;
            ScanId? lastHolder = null;
            await foreach (var active in watchActive.SubscribeAsync(token))
            {
                var holder = active.ScanId;
                var fresh = !first && holder is not null && holder != lastHolder;
                first = false;
                lastHolder = holder ?? lastHolder;
                Update(
                    current => current with
                    {
                        Active = active,

                        // A tab that executed started afresh already; a tab that opens while a scan runs keeps its lines.
                        Lines = fresh && !current.Submitting ? current.Lines.RemoveAll(line => line.ScanId != holder) : current.Lines,
                    },
                    SessionChange.Slot | (fresh ? SessionChange.Lines : SessionChange.None));

                if (holder is { } id && !IsFollowing(id))
                {
                    await TryFollowAsync(id, token);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.SessionFeedFailed(exception, "the active slot", ExternalText.Clean(exception.Message));
        }
    }

    private bool IsFollowing(ScanId id)
    {
        lock (gate)
        {
            return followed == id;
        }
    }

    // A reservation has no scan to follow yet: its stream is empty, and the slot's next change, when the scan starts,
    // brings another attempt. A scan's stream starts with its snapshot.
    private async Task TryFollowAsync(ScanId id, CancellationToken token)
    {
        var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var events = watchScan.SubscribeAsync(id, stop.Token).GetAsyncEnumerator(stop.Token);
        var handedOver = false;
        try
        {
            if (!await events.MoveNextAsync())
            {
                return;
            }

            CancellationTokenSource? previous;
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                previous = followStop;
                followStop = stop;
                followed = id;
                handedOver = true;
            }

            CancelQuietly(previous);
            Apply(id, events.Current);
            var follow = Task.Run(() => FollowScanAsync(id, events, stop), CancellationToken.None);
            lock (gate)
            {
                followTask = follow;
            }
        }
        finally
        {
            if (!handedOver)
            {
                await events.DisposeAsync();
                stop.Dispose();
            }
        }
    }

    // The scan followed before ends on its own before another takes the slot; this only makes sure of it.
    private static void CancelQuietly(CancellationTokenSource? previous)
    {
        try
        {
            previous?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // It had ended already.
        }
    }

    private async Task FollowScanAsync(ScanId id, IAsyncEnumerator<ScanEvent> events, CancellationTokenSource stop)
    {
        try
        {
            while (await events.MoveNextAsync())
            {
                Apply(id, events.Current);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.SessionFeedFailed(exception, "a scan", ExternalText.Clean(exception.Message));
        }
        finally
        {
            await events.DisposeAsync();
            lock (gate)
            {
                followStop = followStop == stop ? null : followStop;
                stop.Dispose();
            }
        }
    }

    // UR-13: the snapshot first, then the scan's latest version at each phase and progress event; its own log events
    // are ignored, since the terminal shows the Live Log, which carries them.
    private void Apply(ScanId id, ScanEvent scanEvent)
    {
        switch (scanEvent)
        {
            case ScanSnapshot snapshot:
                Update(current => current with { Current = snapshot.Scan }, SessionChange.Scan);
                break;
            case PhaseChanged or PackagesCorrelated:
                if (getScan.Execute(id) is { } latest)
                {
                    Update(current => current.Current?.Id == id ? current with { Current = latest } : current, SessionChange.Scan);
                }

                break;
            case ScanFinished:
                Finish(id, getScan.Execute(id));
                break;
        }
    }

    // UR-14: the submitter opens the report; every other tab gets the snackbar and the notice. Failed and Cancelled
    // scans bring their reason to every tab that follows them.
    private void Finish(ScanId id, Scan? final)
    {
        var (ending, notice) = Update<(ScanEnding? Ending, InlineNotice? Notice)>(
            current =>
            {
                var scan = final ?? current.Current;
                if (scan is null || scan.Id != id)
                {
                    return (current, (null, null));
                }

                var submittedHere = current.Submitted.Contains(id);
                var notice = NoticeFor(scan, submittedHere);
                return (current with { Current = scan, Notice = notice ?? current.Notice }, (new ScanEnding(scan, submittedHere), notice));
            },
            SessionChange.Scan | SessionChange.Notice);

        if (notice is not null)
        {
            Announce(notice.Announcement);
        }

        if (ending is not null)
        {
            ScanEnded?.Invoke(ending);
        }
    }

    private static InlineNotice? NoticeFor(Scan scan, bool submittedHere) => scan.Status switch
    {
        ScanStatus.Completed or ScanStatus.Incomplete when !submittedHere =>
            new InlineNotice(NoticeKind.Finished, "Scan " + UiFormat.Status(scan.Status), scan.Id),
        ScanStatus.Failed => new InlineNotice(NoticeKind.Failed, "Scan failed: " + scan.Failure?.Reason, scan.Id),
        ScanStatus.Cancelled => new InlineNotice(NoticeKind.Cancelled, "Scan cancelled: " + scan.CancellationReason, scan.Id),
        _ => null,
    };

    // UR-17: every line of the Live Log, the server's replay first; the oldest go beyond 1,000.
    private async Task FollowLiveLogAsync(CancellationToken token)
    {
        try
        {
            await foreach (var entry in watchLiveLog.SubscribeAsync(token))
            {
                var line = new TerminalLine(Interlocked.Increment(ref nextSequence), entry.Timestamp, entry.Level, entry.Message, entry.ScanId);
                Update(current => current with { Lines = Append(current.Lines, line) }, SessionChange.Lines);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.SessionFeedFailed(exception, "the Live Log", ExternalText.Clean(exception.Message));
        }
    }

    private static ImmutableList<TerminalLine> Append(ImmutableList<TerminalLine> lines, TerminalLine line)
    {
        var added = lines.Add(line);
        return added.Count > MaxLines ? added.RemoveRange(0, added.Count - MaxLines) : added;
    }

    // UR-04: health at once, then every 10 seconds on the Application's clock; it never calls NVD.
    private async Task ReadHealthAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(HealthInterval, clock);
        try
        {
            do
            {
                try
                {
                    var health = await getHealth.ExecuteAsync(token);
                    Update(current => current with { Health = health }, SessionChange.Health);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.SessionFeedFailed(exception, "health", ExternalText.Clean(exception.Message));
                }
            }
            while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private void Update(Func<SessionState, SessionState> change, SessionChange changed)
    {
        lock (gate)
        {
            state = change(state);
        }

        if (changed != SessionChange.None)
        {
            Changed?.Invoke(changed);
        }
    }

    private T Update<T>(Func<SessionState, (SessionState State, T Result)> change, SessionChange changed)
    {
        T result;
        lock (gate)
        {
            (state, result) = change(state);
        }

        if (changed != SessionChange.None)
        {
            Changed?.Invoke(changed);
        }

        return result;
    }
}