using Airlock.Domain.Results;
using Airlock.Domain.Targets;

namespace Airlock.Application.Correlation;

/// <summary>The verdict for one package (AR-13 to AR-16).</summary>
public abstract record PackageCorrelation(Package Package);

/// <summary>
/// No finding. <paramref name="Method"/> is the CPE or product match that found none; null means the package was not
/// product-matched because its name is too generic (AR-15).
/// </summary>
public sealed record CleanCorrelation(Package Package, MatchMethod? Method, IReadOnlyList<string> UnscoredCveIds)
    : PackageCorrelation(Package);

/// <summary>At least one finding.</summary>
public sealed record VulnerableCorrelation(VulnerablePackage Result, IReadOnlyList<string> UnscoredCveIds)
    : PackageCorrelation(Result.Package);

/// <summary>Correlation could not be completed; never counted as clean.</summary>
public sealed record UnverifiedCorrelation(UnverifiedPackage Result) : PackageCorrelation(Result.Package);
