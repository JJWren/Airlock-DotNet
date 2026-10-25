using System.Net;
using System.Text;

namespace Airlock.Web.Tests.Support;

/// <summary>A read-only stream of <c>length</c> bytes that allocates nothing, counting what was read.</summary>
internal sealed class GeneratedStream(long length) : Stream
{
    private long position;

    public long BytesRead => Interlocked.Read(ref position);

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => length;

    public override long Position
    {
        get => BytesRead;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var count = (int)Math.Min(buffer.Length, length - BytesRead);
        buffer[..count].Fill((byte)'a');
        Interlocked.Add(ref position, count);
        return count;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Read(buffer.Span));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Task.FromResult(Read(buffer, offset, count));

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// A multipart body that sends its start — one artifact's headers and a first chunk — then waits for a gate before the
/// rest, as a slow or stalled client would. <see cref="Started"/> completes once the start has been sent.
/// </summary>
internal sealed class GatedUpload(string fileName, Task gate, int firstChunk = 1024, int rest = 1024) : HttpContent
{
    private const string Boundary = "airlock-gated";

    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static string ContentType => $"multipart/form-data; boundary={Boundary}";

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        await SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        var head = $"--{Boundary}\r\nContent-Disposition: form-data; name=\"artifacts\"; filename=\"{fileName}\"\r\n\r\n";
        await stream.WriteAsync(Encoding.UTF8.GetBytes(head), cancellationToken);
        await stream.WriteAsync(new byte[firstChunk], cancellationToken);
        await stream.FlushAsync(cancellationToken);
        Started.TrySetResult();
        await gate.WaitAsync(cancellationToken);
        await stream.WriteAsync(new byte[rest], cancellationToken);
        await stream.WriteAsync(Encoding.UTF8.GetBytes($"\r\n--{Boundary}--\r\n"), cancellationToken);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    public static GatedUpload Create(string fileName, Task gate, int firstChunk = 1024, int rest = 1024)
    {
        var content = new GatedUpload(fileName, gate, firstChunk, rest);
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(ContentType);
        return content;
    }
}
