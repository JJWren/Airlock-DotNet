using Microsoft.Extensions.Time.Testing;

namespace Airlock.Web.Tests.Support;

/// <summary>What every test shares: its token, and bounds that turn a hang into a failure.</summary>
internal static class Test
{
    public static readonly DateTimeOffset StartTime = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>The work's result, or a failed test after <paramref name="seconds"/> of real time instead of a hang.</summary>
    public static Task<T> Within<T>(this Task<T> work, int seconds = 30) => work.WaitAsync(TimeSpan.FromSeconds(seconds), Token);

    public static Task Within(this Task work, int seconds = 30) => work.WaitAsync(TimeSpan.FromSeconds(seconds), Token);

    /// <summary>Moves a fake clock forward until the work is done, letting its continuations run in between.</summary>
    public static async Task<T> DriveAsync<T>(this FakeTimeProvider clock, Task<T> work, TimeSpan step, int maxSteps = 5_000)
    {
        for (var i = 0; i < maxSteps && !work.IsCompleted; i++)
        {
            await Task.Delay(1, Token);
            clock.Advance(step);
        }

        return await work.Within();
    }

    /// <summary>Polls a condition in real time, for state another thread changes.</summary>
    public static async Task EventuallyAsync(Func<bool> condition, string because, int seconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, because);
            await Task.Delay(10, Token);
        }
    }
}

/// <summary>A folder of the test's own under the system's temporary folder, deleted afterwards.</summary>
internal sealed class TemporaryFolder : IDisposable
{
    public TemporaryFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "airlock-web-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        if (!Directory.Exists(Path))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A test that keeps a file open leaves it for the system to clean.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
