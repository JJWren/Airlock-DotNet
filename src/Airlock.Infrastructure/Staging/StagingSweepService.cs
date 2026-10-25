using Airlock.Application.Logging;
using Airlock.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Airlock.Infrastructure.Staging;

/// <summary>
/// At startup, takes the staging root's lock and deletes the marked folders an earlier run left under it (IR-13). The
/// lock is held until the host stops, so a second Airlock on the same root stops at startup instead.
/// </summary>
internal sealed class StagingSweepService(
    IOptions<StagingOptions> options, TimeProvider timeProvider, ILogger<StagingSweepService> logger) : IHostedService, IDisposable
{
    private FileStream? rootLock;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var root = options.Value.Root;
        rootLock ??= StagingFolders.LockRoot(root);

        List<string> folders;
        try
        {
            folders = [.. Directory.EnumerateDirectories(root)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LeftoverNotRemoved(ExternalText.Clean(exception.Message));
            return;
        }

        var removed = 0;
        foreach (var folder in folders)
        {
            // IR-13: only folders named like a scan or Syft run that carry Airlock's marker; nothing else is touched.
            if (!Guid.TryParseExact(Path.GetFileName(folder), "D", out _) || !StagingFolders.IsMarked(folder))
            {
                continue;
            }

            try
            {
                await StagingFolders.DeleteAsync(folder, timeProvider, logger, cancellationToken);
                removed++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LeftoverNotRemoved(ExternalText.Clean(exception.Message));
            }
        }

        if (removed > 0)
        {
            logger.LeftoversRemoved(removed);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        rootLock?.Dispose();
        rootLock = null;
    }
}
