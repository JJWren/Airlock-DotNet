using Microsoft.Extensions.Time.Testing;

namespace Airlock.Infrastructure.Tests.Support;

/// <summary>Moves a fake clock forward until the work under test is done, letting its continuations run in between.</summary>
internal static class ClockDriver
{
    public static async Task<T> DriveAsync<T>(this FakeTimeProvider clock, Task<T> work, TimeSpan step, int maxSteps = 5_000)
    {
        await DriveAsync(clock, (Task)work, step, maxSteps);
        return await work;
    }

    public static async Task DriveAsync(this FakeTimeProvider clock, Task work, TimeSpan step, int maxSteps = 5_000)
    {
        for (var i = 0; i < maxSteps && !work.IsCompleted; i++)
        {
            await Task.Delay(1, Test.Token);
            clock.Advance(step);
        }

        await work.WaitAsync(TimeSpan.FromSeconds(30), Test.Token);
    }
}
