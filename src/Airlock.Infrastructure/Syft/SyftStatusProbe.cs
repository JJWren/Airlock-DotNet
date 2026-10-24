using System.Text;
using Airlock.Application.Logging;
using Airlock.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Airlock.Infrastructure.Syft;

/// <summary>Whether Syft can run and which version it is, checked on first use and then cached (IR-05).</summary>
internal sealed class SyftStatusProbe(
    SyftLocator locator, SyftProcessRunner runner, SyftEnvironment environment, TimeSpan cacheDuration, TimeProvider timeProvider,
    ILogger logger)
{
    /// <summary>How long <c>syft version</c> may take (IR-05).</summary>
    public static readonly TimeSpan VersionLimit = TimeSpan.FromSeconds(10);

    private const int MaxVersionOutput = 16 * 1024;

    private readonly Lock gate = new();
    private Task<SbomToolStatus>? probe;
    private DateTimeOffset expires = DateTimeOffset.MinValue;

    // IR-05: concurrent callers share one probe, and a caller's cancellation ends only its own wait.
    public Task<SbomToolStatus> GetAsync(CancellationToken cancellationToken)
    {
        Task<SbomToolStatus> current;
        lock (gate)
        {
            if (probe is null || (probe.IsCompleted && timeProvider.GetUtcNow() >= expires))
            {
                probe = Task.Run(ProbeAsync, CancellationToken.None);
            }

            current = probe;
        }

        return current.WaitAsync(cancellationToken);
    }

    internal static string? VersionFrom(string? output)
    {
        foreach (var line in (output ?? string.Empty).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
            {
                var version = trimmed["Version:".Length..].Trim();
                return version.Length == 0 ? null : ExternalText.Clean(version, 64);
            }
        }

        return null;
    }

    private static async Task<string> ReadTextAsync(Stream output, CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxVersionOutput];
        var length = 0;
        int read;
        while (length < buffer.Length && (read = await output.ReadAsync(buffer.AsMemory(length), cancellationToken)) > 0)
        {
            length += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, length);
    }

    private async Task<SbomToolStatus> ProbeAsync()
    {
        try
        {
            return await CheckAsync();
        }
        finally
        {
            lock (gate)
            {
                expires = timeProvider.GetUtcNow() + cacheDuration;
            }
        }
    }

    // IR-05: available when the resolved binary exists and `syft version` exits 0 within 10 seconds.
    private async Task<SbomToolStatus> CheckAsync()
    {
        var location = locator.Locate();
        if (!location.Exists)
        {
            return Unavailable($"Syft not found at {location.Path}");
        }

        SyftRunResult<string> result;
        try
        {
            result = await runner.RunAsync(environment.Request(location.Path, ["version"], VersionLimit), ReadTextAsync, CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Unavailable($"Syft at {location.Path} did not run: {ExternalText.Clean(exception.Message)}");
        }

        var problem = result.End switch
        {
            SyftRunEnd.NotStarted => ExternalText.Clean(result.StartFailure),
            SyftRunEnd.TimedOut => "it did not answer within 10 seconds",
            _ when result.ExitCode != 0 => $"exit code {result.ExitCode}: {ExternalText.Clean(result.ErrorTail)}",
            _ => null,
        };
        if (problem is not null)
        {
            return Unavailable($"Syft at {location.Path} did not run: {problem}");
        }

        var version = VersionFrom(result.Output);
        var detail = version is null ? $"Syft at {location.Path}" : $"Syft {version} at {location.Path}";
        logger.SyftFound(detail);
        return new SbomToolStatus(true, version, detail);
    }

    private SbomToolStatus Unavailable(string detail)
    {
        logger.SyftUnavailable(detail);
        return new SbomToolStatus(false, null, detail);
    }
}
