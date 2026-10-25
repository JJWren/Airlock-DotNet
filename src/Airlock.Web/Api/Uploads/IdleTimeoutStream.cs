namespace Airlock.Web.Api.Uploads;

/// <summary>
/// The submission's body, read with an idle timer on the <see cref="TimeProvider"/> (U4-NFR-04): a read that waits
/// <c>timeout</c> for data is cancelled, so a half-open connection cannot hold the single active slot. The timer runs
/// only while a read waits, so a slow disk on Airlock's side never counts against the client.
/// </summary>
internal sealed class IdleTimeoutStream : Stream
{
    private readonly Stream inner;
    private readonly TimeSpan timeout;
    private readonly CancellationTokenSource idle;
    private readonly ITimer timer;

    public IdleTimeoutStream(Stream inner, TimeSpan timeout, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.inner = inner;
        this.timeout = timeout;
        idle = new CancellationTokenSource();
        timer = timeProvider.CreateTimer(static state => ((CancellationTokenSource)state!).Cancel(), idle, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>True once a read waited too long; every later read is cancelled too.</summary>
    public bool TimedOut => idle.IsCancellationRequested;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, idle.Token);
        timer.Change(timeout, Timeout.InfiniteTimeSpan);
        try
        {
            return await inner.ReadAsync(buffer, linked.Token);
        }
        finally
        {
            timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    // Kestrel allows no synchronous reads of a request body, and the reader never makes one.
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask DisposeAsync()
    {
        await timer.DisposeAsync();
        idle.Dispose();
        await base.DisposeAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Dispose();
            idle.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// An artifact's content as the reservation reads it. It remembers when reading the request failed, so the reader can
/// tell a body that was cut off or broken from a disk that failed while the file was written.
/// </summary>
internal sealed class RequestBodyStream(Stream inner) : Stream
{
    /// <summary>True once a read of the request threw.</summary>
    public bool ReadFailed { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        try
        {
            return await inner.ReadAsync(buffer, cancellationToken);
        }
        catch
        {
            ReadFailed = true;
            throw;
        }
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
