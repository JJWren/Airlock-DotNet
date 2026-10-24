using Airlock.Application.Logging;
using Airlock.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Airlock.Infrastructure.Staging;

/// <summary>At startup, deletes the marked folders an earlier run left under the staging root (IR-13).</summary>
internal sealed class StagingSweepService(
    IOptions<StagingOptions> options, TimeProvider timeProvider, ILogger<StagingSweepService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var root = options.Value.Root;
        List<string> folders;
        try
        {
            folders = Directory.Exists(root) ? [.. Directory.EnumerateDirectories(root)] : [];
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

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
