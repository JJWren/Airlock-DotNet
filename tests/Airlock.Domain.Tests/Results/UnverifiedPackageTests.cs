using Airlock.Domain.Results;

namespace Airlock.Domain.Tests.Results;

// BR-18
public sealed class UnverifiedPackageTests
{
    [Fact]
    public void An_unknown_version_reads_as_version_unknown()
    {
        var package = UnverifiedPackage.VersionUnknown(A.Package("syft", "UNKNOWN", "binary"));

        Assert.Equal(UnverifiedReason.VersionUnknown, package.Reason);
        Assert.Equal("version unknown", package.Detail);
    }

    [Fact]
    public void A_failed_lookup_keeps_the_last_failure_reason()
    {
        var package = UnverifiedPackage.LookupFailed(A.Package(), "NVD returned 503.");

        Assert.Equal(UnverifiedReason.LookupFailed, package.Reason);
        Assert.Equal("NVD returned 503.", package.Detail);
    }

    [Fact]
    public void Invalid_input_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => UnverifiedPackage.VersionUnknown(null!));
        Assert.Throws<ArgumentException>(() => UnverifiedPackage.LookupFailed(A.Package(), " "));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnverifiedPackage(A.Package(), (UnverifiedReason)42, "x"));
    }
}
