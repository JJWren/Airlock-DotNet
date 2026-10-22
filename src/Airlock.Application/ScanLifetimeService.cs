using Airlock.Application.Logging;
using Airlock.Application.Options;
using Airlock.Application.Ports;
using Airlock.Application.Scanning;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Airlock.Application;

/// <summary>
/// At start, warns about allowed scan roots that do not exist yet; at shutdown, cancels the running scan and any open
/// reservation and waits for their cleanup, within the host's shutdown timeout (AR-30).
/// </summary>
internal sealed class ScanLifetimeService(
    ScanCoordinator coordinator,
    IHostFileSystem fileSystem,
    IOptions<AirlockOptions> options,
    ILogger<ScanLifetimeService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var root in options.Value.AllowedScanRoots)
        {
            if (!fileSystem.DirectoryExists(fileSystem.GetFullPath(root)))
            {
                logger.ScanRootMissing(root);
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => coordinator.StopAllAsync().WaitAsync(cancellationToken);
}
