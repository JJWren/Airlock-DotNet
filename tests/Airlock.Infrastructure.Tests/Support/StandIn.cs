using Airlock.Infrastructure.Syft;

namespace Airlock.Infrastructure.Tests.Support;

/// <summary>The stand-in program that plays Syft (tests/Airlock.Infrastructure.StandIn), copied next to the tests.</summary>
internal static class StandIn
{
    public static string Path { get; } = System.IO.Path.Combine(
        AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Airlock.Infrastructure.StandIn.exe" : "Airlock.Infrastructure.StandIn");

    /// <summary>A run of the stand-in in <paramref name="mode"/>, scripted by STANDIN_* variables.</summary>
    public static SyftRunRequest Request(string mode, TimeSpan? timeLimit = null, params (string Name, string Value)[] variables) =>
        new(Path, [mode], timeLimit ?? TimeSpan.FromMinutes(1))
        {
            Environment = variables.ToDictionary(variable => variable.Name, variable => variable.Value, StringComparer.Ordinal),
        };

    /// <summary>Whether the process with this id is still running.</summary>
    public static bool IsRunning(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Waits up to <paramref name="limit"/> for the process to be gone.</summary>
    public static async Task<bool> IsGoneWithinAsync(int processId, TimeSpan limit)
    {
        var deadline = DateTime.UtcNow + limit;
        while (IsRunning(processId))
        {
            if (DateTime.UtcNow > deadline)
            {
                return false;
            }

            await Task.Delay(20, Test.Token);
        }

        return true;
    }
}
