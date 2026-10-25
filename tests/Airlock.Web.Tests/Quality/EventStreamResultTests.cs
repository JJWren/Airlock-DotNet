using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using Airlock.Web.Api.Streams;
using Airlock.Web.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Airlock.Web.Tests.Quality;

// U4-NFR-05, U4-NFR-09 — the stream writer on its own: keep-alive comments, a closed connection, a client that stopped
// reading, shutdown.
public sealed class EventStreamResultTests : IDisposable
{
    private readonly FakeTimeProvider clock = new(Test.StartTime);
    private readonly StoppingLifetime lifetime = new();
    private readonly FakeLogCollector logs = new();
    private readonly RecordingStream body = new();
    private readonly CancellationTokenSource aborted = new();

    [Fact]
    public async Task Events_are_framed_and_flushed_as_they_come()
    {
        var running = Run(Items(new EventStreamItem("phase", "{\"phase\":\"CORRELATION\"}"u8.ToArray()), new EventStreamItem("finished", "{\"status\":\"COMPLETED\"}"u8.ToArray())));

        await running.Within();

        Assert.Equal("event: phase\ndata: {\"phase\":\"CORRELATION\"}\n\nevent: finished\ndata: {\"status\":\"COMPLETED\"}\n\n", body.Text);
    }

    [Fact]
    public async Task A_silent_stream_receives_a_keep_alive_comment_every_15_seconds()
    {
        var source = new EndlessSource();
        var running = Run(source.ReadAsync);
        await source.Started.Task.Within();

        clock.Advance(TimeSpan.FromSeconds(14));
        await Task.Delay(50, Test.Token);
        Assert.Equal(string.Empty, body.Text);
        await DriveUntilAsync(() => body.Text == ": keep-alive\n\n");
        await DriveUntilAsync(() => body.Text == ": keep-alive\n\n: keep-alive\n\n");

        await aborted.CancelAsync();
        await running.Within();
        Assert.True(source.Disposed);
    }

    [Fact]
    public async Task A_client_that_closes_its_connection_ends_the_subscription()
    {
        var source = new EndlessSource();
        var running = Run(source.ReadAsync);
        await source.Started.Task.Within();

        await aborted.CancelAsync();

        await running.Within();
        Assert.True(source.Disposed);
    }

    [Fact]
    public async Task A_client_that_stops_reading_ends_the_subscription_at_the_next_keep_alive()
    {
        var source = new EndlessSource();
        var running = Run(source.ReadAsync, clientGone: true);
        await source.Started.Task.Within();

        await DriveUntilAsync(() => running.IsCompleted);

        Assert.True(source.Disposed);
    }

    [Fact]
    public async Task A_client_that_stops_reading_ends_the_subscription_at_the_next_event()
    {
        var source = new EndlessSource(new EventStreamItem("phase", "{\"phase\":\"CORRELATION\"}"u8.ToArray()));
        var running = Run(source.ReadAsync, clientGone: true);

        await running.Within();

        Assert.True(source.Disposed);
    }

    [Fact]
    public async Task Shutdown_ends_a_stream_a_second_after_it_begins()
    {
        var source = new EndlessSource();
        var running = Run(source.ReadAsync);
        await source.Started.Task.Within();

        lifetime.Stop();
        clock.Advance(TimeSpan.FromMilliseconds(900));
        await Task.Delay(50, Test.Token);
        Assert.False(running.IsCompleted);
        clock.Advance(TimeSpan.FromMilliseconds(200));

        await running.Within();
        Assert.True(source.Disposed);
        Assert.Contains(logs.GetSnapshot(), record => record.Id.Id == 3301);
    }

    public void Dispose()
    {
        aborted.Dispose();
        body.Dispose();
        lifetime.Dispose();
    }

    private Task Run(Func<CancellationToken, IAsyncEnumerable<EventStreamItem>> source, bool clientGone = false)
    {
        var services = new ServiceCollection()
            .AddSingleton<TimeProvider>(clock)
            .AddSingleton<IHostApplicationLifetime>(lifetime)
            .AddSingleton(logs)
            .AddLogging(logging => logging.SetMinimumLevel(LogLevel.Debug).AddProvider(new FakeLoggerProvider(logs)))
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services, RequestAborted = aborted.Token };
        context.Response.Body = body;
        if (clientGone)
        {
            context.Features.Set<IHttpResponseBodyFeature>(new GoneClientBody());
        }

        return Task.Run(() => new EventStreamResult(source).ExecuteAsync(context), Test.Token);
    }

    private static Func<CancellationToken, IAsyncEnumerable<EventStreamItem>> Items(params EventStreamItem[] items) =>
        token => Enumerate(items, token);

    private static async IAsyncEnumerable<EventStreamItem> Enumerate(EventStreamItem[] items, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
    }

    private async Task DriveUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(250));
            await Task.Delay(10, Test.Token);
        }

        Assert.True(condition(), "Written so far: " + body.Text);
    }

    /// <summary>A source that yields its first items, if any, and then never again; it records that it was started and disposed.</summary>
    private sealed class EndlessSource(params EventStreamItem[] first)
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Disposed { get; private set; }

        public async IAsyncEnumerable<EventStreamItem> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            try
            {
                Started.TrySetResult();
                foreach (var item in first)
                {
                    yield return item;
                }

                await Task.Delay(Timeout.Infinite, cancellationToken);
                yield break;
            }
            finally
            {
                Disposed = true;
            }
        }
    }

    /// <summary>A response whose client stopped reading without aborting the request: every flush reports it completed.</summary>
    private sealed class GoneClientBody : IHttpResponseBodyFeature
    {
        private readonly Pipe pipe = new();

        public GoneClientBody() => pipe.Reader.Complete();

        public Stream Stream => Stream.Null;

        public PipeWriter Writer => pipe.Writer;

        public void DisableBuffering()
        {
        }

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task CompleteAsync() => pipe.Writer.CompleteAsync().AsTask();
    }

    private sealed class StoppingLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => stopping.Token;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void Stop() => stopping.Cancel();

        public void StopApplication() => Stop();

        public void Dispose() => stopping.Dispose();
    }

    /// <summary>A response body that keeps what was written, safe to read from the test thread.</summary>
    private sealed class RecordingStream : Stream
    {
        private readonly Lock gate = new();
        private readonly StringBuilder text = new();

        public string Text
        {
            get
            {
                lock (gate)
                {
                    return text.ToString();
                }
            }
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            lock (gate)
            {
                text.Append(Encoding.UTF8.GetString(buffer));
            }
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Write(buffer.AsSpan(offset, count));
            return Task.CompletedTask;
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
