using Airlock.Domain.Intelligence;

namespace Airlock.Domain.Tests.Intelligence;

// BR-10, U3 decision 1 — a candidate's version part as NVD writes it (IR-10).
public sealed class CandidateCpeForNvdTests
{
    [Fact]
    public void A_Go_modules_leading_v_is_dropped_from_the_version_part()
    {
        var candidate = A.GeneratedCpe("golang", "crypto", "v0.23.3");

        var asked = candidate.ForNvd(A.Package("golang.org/x/crypto", "v0.23.3", "go-module"));

        Assert.Equal("cpe:2.3:a:golang:crypto:0.23.3:*:*:*:*:*:*:*", asked.Value);
        Assert.Equal(CpeSource.Generated, asked.Source);
    }

    [Fact]
    public void Escapes_before_the_version_part_are_kept()
    {
        var candidate = new CandidateCpe(@"cpe:2.3:a:go:auth\/oauth2adapt:v0.2.8:*:*:*:*:*:*:*", CpeSource.Dictionary);

        var asked = candidate.ForNvd(A.Package("cloud.google.com/go/auth/oauth2adapt", "v0.2.8", "go-module"));

        Assert.Equal(@"cpe:2.3:a:go:auth\/oauth2adapt:0.2.8:*:*:*:*:*:*:*", asked.Value);
        Assert.Equal(CpeSource.Dictionary, asked.Source);
    }

    [Theory]
    [InlineData("npm", "v1.3.0", "v1.3.0")]
    [InlineData("go-module", "0.23.3", "0.23.3")]
    [InlineData("go-module", "v0.23.3", "*")]
    [InlineData("go-module", "v0.23.3", "v0.23.2")]
    public void Any_other_candidate_is_returned_as_it_is(string type, string packageVersion, string cpeVersion)
    {
        var candidate = A.GeneratedCpe("vendor", "product", cpeVersion);

        Assert.Same(candidate, candidate.ForNvd(A.Package("product", packageVersion, type)));
    }

    [Fact]
    public void A_CPE_without_a_version_part_is_returned_as_it_is()
    {
        var candidate = new CandidateCpe("cpe:2.3:a:golang:crypto", CpeSource.Generated);

        Assert.Same(candidate, candidate.ForNvd(A.Package("golang.org/x/crypto", "v0.23.3", "go-module")));
    }

    [Fact]
    public void Null_input_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => A.GeneratedCpe("golang", "crypto").ForNvd(null!));
    }
}
