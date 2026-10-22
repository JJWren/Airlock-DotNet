using Airlock.Domain.Targets;

namespace Airlock.Domain.Tests.Targets;

// BR-10 — rows of the versions table.
public sealed class PackageVersionTests
{
    [Theory]
    [InlineData("UNKNOWN")]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_whitespace_or_UNKNOWN_is_unknown(string version)
    {
        Assert.False(PackageVersion.IsKnown(A.Package("syft", version, "binary")));
    }

    [Fact]
    public void A_real_version_is_known()
    {
        Assert.True(PackageVersion.IsKnown(A.Package("pyyaml", "5.3")));
    }

    [Theory]
    [InlineData("go-module", "cloud.google.com/go/auth", "v0.23.3", "0.23.3")]
    [InlineData("apk", "busybox", "1.36.1-r31", "1.36.1-r31")]
    [InlineData("npm", "left-pad", "v1.3.0", "v1.3.0")]
    [InlineData("go-module", "example.com/m", "v1", "1")]
    [InlineData("go-module", "example.com/m", "vendor", "vendor")]
    [InlineData("go-module", "example.com/m", "v", "v")]
    public void Only_Go_modules_drop_a_leading_v(string type, string name, string version, string sent)
    {
        Assert.Equal(sent, PackageVersion.ForNvd(A.Package(name, version, type)));
    }

    [Fact]
    public void Null_input_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => PackageVersion.IsKnown(null!));
        Assert.Throws<ArgumentNullException>(() => PackageVersion.ForNvd(null!));
    }
}
