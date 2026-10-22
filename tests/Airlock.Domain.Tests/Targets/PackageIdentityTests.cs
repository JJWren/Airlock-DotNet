using Airlock.Domain.Targets;

namespace Airlock.Domain.Tests.Targets;

// BR-11 — rows of the identity table.
public sealed class PackageIdentityTests
{
    [Fact]
    public void Names_are_compared_ignoring_case()
    {
        var a = A.Package("PyYAML", "5.3");
        var b = A.Package("pyyaml", "5.3");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Different_types_or_versions_are_different_packages()
    {
        Assert.NotEqual(A.Package("requests", "2.25.1", "python"), A.Package("requests", "2.25.1", "npm"));
        Assert.NotEqual(A.Package("requests", "2.25.1"), A.Package("requests", "2.31.0"));
        Assert.NotEqual(A.Package("requests", "2.25.1", "Python"), A.Package("requests", "2.25.1", "python"));
        Assert.False(A.Package().Equals(null));
    }

    [Fact]
    public void A_package_reads_as_name_and_version()
    {
        Assert.Equal("pyyaml 5.3", A.Package().ToString());
    }

    [Fact]
    public void Type_and_name_are_required_but_the_version_may_be_empty()
    {
        Assert.Throws<ArgumentException>(() => new Package("", "pyyaml", "5.3"));
        Assert.Throws<ArgumentException>(() => new Package("python", " ", "5.3"));
        Assert.Throws<ArgumentNullException>(() => new Package("python", "pyyaml", null!));
        Assert.Equal(string.Empty, new Package("binary", "syft", "").Version);
    }
}
