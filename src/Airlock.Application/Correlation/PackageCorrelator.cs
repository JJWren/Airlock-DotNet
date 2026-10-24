using System.Globalization;
using Airlock.Application.Logging;
using Airlock.Application.Ports;
using Airlock.Domain.Intelligence;
using Airlock.Domain.Results;
using Airlock.Domain.Targets;
using Microsoft.Extensions.Logging;

namespace Airlock.Application.Correlation;

/// <summary>Decides one package's verdict from NVD's answers (AR-13 to AR-18). It never retries: that is the catalog's job.</summary>
public sealed class PackageCorrelator(IVulnerabilityCatalog catalog, ILogger<PackageCorrelator> logger)
{
    public async ValueTask<PackageCorrelation> CorrelateAsync(
        SbomPackage sbomPackage, NvdOutageBreaker breaker, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sbomPackage);
        ArgumentNullException.ThrowIfNull(breaker);
        var package = sbomPackage.Package;

        // AR-13
        if (!PackageVersion.IsKnown(package))
        {
            logger.VersionUnknown(package);
            return new UnverifiedCorrelation(UnverifiedPackage.VersionUnknown(package));
        }

        // AR-14
        foreach (var candidate in CandidateCpe.Prioritize(package, sbomPackage.CandidateCpes))
        {
            if (breaker.IsOpen)
            {
                return Unavailable(package, breaker);
            }

            cancellationToken.ThrowIfCancellationRequested();
            // IR-10: asked for, and recorded in the match, with its version part as NVD writes it.
            var cpe = candidate.ForNvd(package);
            logger.CpeSearch(cpe.Value);
            var lookup = await catalog.FindByCpeAsync(cpe, cancellationToken);
            if (lookup is CatalogFound found)
            {
                breaker.RecordAnswer();
                return FromMatch(package, new CpeMatch(cpe), found.Records);
            }

            if (lookup is CatalogFailed failed)
            {
                var reason = ExternalText.Clean(failed.Reason);
                logger.CpeSearchFailed(cpe.Value, reason);
                return Failed(package, reason, breaker);
            }

            breaker.RecordAnswer();
        }

        // AR-15
        var product = ProductName.ForProductMatch(package);
        if (product is null)
        {
            logger.NameTooGeneric(package);
            return new CleanCorrelation(package, null, []);
        }

        if (breaker.IsOpen)
        {
            return Unavailable(package, breaker);
        }

        cancellationToken.ThrowIfCancellationRequested();
        logger.ProductSearch(product, package.Version);
        var productLookup = await catalog.FindByProductAsync(package, product, cancellationToken);
        if (productLookup is CatalogFound productFound)
        {
            breaker.RecordAnswer();
            return FromMatch(package, new ProductMatch(product), productFound.Records);
        }

        if (productLookup is CatalogFailed productFailed)
        {
            var reason = ExternalText.Clean(productFailed.Reason);
            logger.ProductSearchFailed(package, reason);
            return Failed(package, reason, breaker);
        }

        breaker.RecordAnswer();
        logger.ProductClean(package);
        return new CleanCorrelation(package, new ProductMatch(product), []);
    }

    // AR-16
    private PackageCorrelation FromMatch(Package package, MatchMethod method, IReadOnlyList<Cve> records)
    {
        var selection = Finding.Select(records);
        if (selection.Findings.Count == 0)
        {
            if (method is CpeMatch cpeMatch)
            {
                logger.CpeClean(cpeMatch.Cpe.Value);
            }
            else
            {
                logger.ProductClean(package);
            }

            return new CleanCorrelation(package, method, selection.UnscoredCveIds);
        }

        if (method is CpeMatch)
        {
            logger.CpeFindings(selection.Findings.Count, package);
        }
        else
        {
            logger.ProductFindings(selection.Findings.Count, package);
        }

        return new VulnerableCorrelation(new VulnerablePackage(package, method, selection.Findings), selection.UnscoredCveIds);
    }

    // AR-14, AR-15, AR-18
    private UnverifiedCorrelation Failed(Package package, string reason, NvdOutageBreaker breaker)
    {
        if (breaker.RecordFailure())
        {
            logger.NvdUnavailable(breaker.Threshold);
        }

        return new UnverifiedCorrelation(UnverifiedPackage.LookupFailed(package, reason));
    }

    // AR-18
    private static UnverifiedCorrelation Unavailable(Package package, NvdOutageBreaker breaker) =>
        new(UnverifiedPackage.LookupFailed(
            package,
            string.Create(
                CultureInfo.InvariantCulture,
                $"NVD unavailable — not checked after {breaker.Threshold} failed lookups in a row")));
}
