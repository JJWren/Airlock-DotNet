using Airlock.Application.Ports;
using Airlock.Domain.Intelligence;
using Airlock.Domain.Targets;

namespace Airlock.Application.Scanning;

internal static class PackageMerge
{
    // AR-12: one entry per package identity, the first spelling kept, candidate CPEs combined in run order.
    public static IReadOnlyList<SbomPackage> Merge(IEnumerable<SbomPackage> packages)
    {
        List<Package> order = [];
        Dictionary<Package, List<CandidateCpe>> candidates = [];
        foreach (var sbomPackage in packages)
        {
            if (!candidates.TryGetValue(sbomPackage.Package, out var list))
            {
                list = [];
                candidates.Add(sbomPackage.Package, list);
                order.Add(sbomPackage.Package);
            }

            list.AddRange(sbomPackage.CandidateCpes);
        }

        return [.. order.Select(package => new SbomPackage(package, candidates[package]))];
    }
}
