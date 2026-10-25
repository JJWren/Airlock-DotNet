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
    ILogger<ScanLifetimeService> logger) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // AR-30: as soon as the host begins to stop — before a web server waits for its open requests — so a client following
    // the scan still receives its Cancelled status.
    public Task StoppingAsync(CancellationToken cancellationToken) => coordinator.StopAllAsync().WaitAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => coordinator.StopAllAsync().WaitAsync(cancellationToken);

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
