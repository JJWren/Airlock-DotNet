using Airlock.Web.Api.Uploads;
using Airlock.Web.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Airlock.Web.Tests.Quality;

// U4-NFR-04, U4-NFR-09 — an upload that sends nothing for 60 seconds is ended and frees the slot and its staging area.
public sealed class StalledUploadTests
{
    [Fact]
    public async Task A_read_that_waits_60_seconds_for_data_is_cancelled()
    {
        var clock = new FakeTimeProvider(Test.StartTime);
        using var silent = new SilentStream();
        await using var body = new IdleTimeoutStream(silent, TimeSpan.FromSeconds(60), clock);

        var read = body.ReadAsync(new byte[16], Test.Token).AsTask();
        await silent.Reading.Task.Within();
        clock.Advance(TimeSpan.FromSeconds(59));
        await Task.Delay(50, Test.Token);
        Assert.False(read.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read.Within());
        Assert.True(body.TimedOut);
    }

    [Fact]
    public async Task Time_spent_between_reads_does_not_count()
    {
        var clock = new FakeTimeProvider(Test.StartTime);
        await using var body = new IdleTimeoutStream(new MemoryStream(new byte[64]), TimeSpan.FromSeconds(60), clock);

        Assert.Equal(16, await body.ReadAsync(new byte[16], Test.Token));
        clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal(16, await body.ReadAsync(new byte[16], Test.Token));
        Assert.False(body.TimedOut);
    }

    [Fact]
    public async Task A_stalled_upload_is_ended_and_frees_the_slot_and_its_staging_area()
    {
        var clock = new FakeTimeProvider(Test.StartTime);
        await using var factory = new AirlockWebFactory(clock: clock);
        using var client = factory.CreateClient();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var uploading = client.PostAsync(Http.ScansUrl, GatedUpload.Create("app-image.tar", gate.Task), Test.Token);
        await Test.EventuallyAsync(() => factory.StagingFolders().Count == 1, "The upload never reached staging.");

        for (var i = 0; i < 200 && !uploading.IsCompleted; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(10, Test.Token);
        }

        await Assert.ThrowsAnyAsync<Exception>(() => uploading.Within());
        await Test.EventuallyAsync(() => factory.StagingFolders().Count == 0, "The staging area was kept.");
        Assert.Contains(factory.Logs.GetSnapshot(), record => record.Id.Id == 3104);
        gate.SetResult();
        await client.StartScanWhenFreeAsync(Http.Requirements);
    }

    /// <summary>A body whose reads wait until they are cancelled, as a half-open connection's would.</summary>
    private sealed class SilentStream : Stream
    {
        public TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

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
            Reading.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
