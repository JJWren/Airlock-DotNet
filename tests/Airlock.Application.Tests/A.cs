using Airlock.Application.Ports;
using Airlock.Domain.Intelligence;
using Airlock.Domain.Targets;

namespace Airlock.Application.Tests;

/// <summary>Test-data builders, read as "A.Package(...)"; the defaults are the S0 examples.</summary>
internal static class A
{
    public static Package Package(string name = "pyyaml", string version = "5.3", string type = "python") =>
        new(type, name, version);

    /// <summary>A Syft-generated candidate CPE, written as "vendor:product".</summary>
    public static CandidateCpe Cpe(string vendorProduct, string version, CpeSource source = CpeSource.Generated)
    {
        var parts = vendorProduct.Split(':');
        return new CandidateCpe($"cpe:2.3:a:{parts[0]}:{parts[1]}:{version}:*:*:*:*:*:*:*", source);
    }

    public static string CpeValue(string vendorProduct, string version) => Cpe(vendorProduct, version).Value;

    public static SbomPackage SbomPackage(Package package, params CandidateCpe[] candidates) => new(package, candidates);

    /// <summary>A package with Syft-generated candidates for each "vendor:product".</summary>
    public static SbomPackage SbomPackage(string name, string version, string type, params string[] vendorProducts) =>
        new(Package(name, version, type), [.. vendorProducts.Select(vendorProduct => Cpe(vendorProduct, version))]);

    public static Cve Cve(string id, decimal score, Severity severity, CvssVersion version = CvssVersion.V31) =>
        new(id, "A test description.", [new CvssScore(score, severity, version, CvssSource.Primary)], ["vendor:product"]);

    public static Cve UnscoredCve(string id) => new(id, null, [], []);
}
