using Airlock.Web.Api.Uploads;
using Airlock.Web.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Airlock.Web.Tests.Quality;

// U4-NFR-02, U4-NFR-04 — the two wrappers around the request body read forward only and say what failed.
public sealed class UploadStreamTests
{
    [Fact]
    public async Task The_idle_timeout_stream_reads_forward_only()
    {
        await using var body = new IdleTimeoutStream(new MemoryStream([1, 2, 3, 4]), TimeSpan.FromSeconds(60), new FakeTimeProvider(Test.StartTime));

        Assert.True(body.CanRead);
        Assert.False(body.CanSeek);
        Assert.False(body.CanWrite);
        Assert.Throws<NotSupportedException>(() => body.Length);
        Assert.Throws<NotSupportedException>(() => body.Position);
        Assert.Throws<NotSupportedException>(() => body.Position = 1);
        Assert.Throws<NotSupportedException>(() => body.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => body.SetLength(1));
        Assert.Throws<NotSupportedException>(() => body.Write([1], 0, 1));
        Assert.Throws<NotSupportedException>(() => body.Read(new byte[1], 0, 1));
        body.Flush();
        Task<int> read = body.ReadAsync(new byte[2], 0, 2, Test.Token);
        Assert.Equal(2, await read);
    }

    [Fact]
    public async Task The_request_body_stream_remembers_a_failed_read()
    {
        using var body = new RequestBodyStream(new FailingStream());

        Assert.True(body.CanRead);
        Assert.False(body.CanSeek);
        Assert.False(body.CanWrite);
        Assert.Throws<NotSupportedException>(() => body.Length);
        Assert.Throws<NotSupportedException>(() => body.Position);
        Assert.Throws<NotSupportedException>(() => body.Position = 1);
        Assert.Throws<NotSupportedException>(() => body.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => body.SetLength(1));
        Assert.Throws<NotSupportedException>(() => body.Write([1], 0, 1));
        Assert.Throws<NotSupportedException>(() => body.Read(new byte[1], 0, 1));
        body.Flush();
        Assert.False(body.ReadFailed);

        await Assert.ThrowsAsync<IOException>(() => body.ReadAsync(new byte[4], 0, 4, Test.Token));

        Assert.True(body.ReadFailed);
    }

    [Fact]
    public void The_idle_timeout_stream_can_be_disposed_synchronously()
    {
        var body = new IdleTimeoutStream(new MemoryStream(), TimeSpan.FromSeconds(60), new FakeTimeProvider(Test.StartTime));

        body.Dispose();

        Assert.False(body.TimedOut);
    }

    private sealed class FailingStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("The client reset the request stream."));
    }
}
