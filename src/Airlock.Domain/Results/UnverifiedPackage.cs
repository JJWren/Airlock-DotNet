using Airlock.Domain.Targets;
using Airlock.Domain.Text;

namespace Airlock.Domain.Results;

/// <summary>Why a package's correlation could not be completed (BR-18).</summary>
public enum UnverifiedReason
{
    LookupFailed,
    VersionUnknown,
}

/// <summary>A package whose correlation could not be completed; never counted as clean.</summary>
public sealed record UnverifiedPackage
{
    public UnverifiedPackage(Package package, UnverifiedReason reason, string detail)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        Package = package;
        Reason = reason;
        Detail = DomainText.Require(detail, DomainLimits.Message, nameof(detail));
    }

    public Package Package { get; }

    public UnverifiedReason Reason { get; }

    public string Detail { get; }

    // BR-18: NVD still failing after retries; the detail is the last failure reason.
    public static UnverifiedPackage LookupFailed(Package package, string lastFailure) =>
        new(package, UnverifiedReason.LookupFailed, lastFailure);

    // BR-10, BR-18
    public static UnverifiedPackage VersionUnknown(Package package) =>
        new(package, UnverifiedReason.VersionUnknown, "version unknown");
}
