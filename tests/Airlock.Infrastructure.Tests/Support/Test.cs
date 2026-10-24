namespace Airlock.Infrastructure.Tests.Support;

/// <summary>What every test shares: its token, cut off after 60 seconds so a broken test fails instead of hanging.</summary>
internal static class Test
{
    public static readonly DateTimeOffset StartTime = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>The test's token, cancelled after <paramref name="seconds"/> as well.</summary>
    public static CancellationTokenSource Deadline(int seconds = 60)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(Token);
        source.CancelAfter(TimeSpan.FromSeconds(seconds));
        return source;
    }

    /// <summary>The work's result, or a failed test after <paramref name="seconds"/> of real time instead of a hang.</summary>
    public static Task<T> Within<T>(this ValueTask<T> work, int seconds = 30) => work.AsTask().WaitAsync(TimeSpan.FromSeconds(seconds), Token);

    public static Task<T> Within<T>(this Task<T> work, int seconds = 30) => work.WaitAsync(TimeSpan.FromSeconds(seconds), Token);
}
