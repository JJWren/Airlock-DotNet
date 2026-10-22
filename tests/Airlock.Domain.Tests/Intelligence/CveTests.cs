using Airlock.Domain.Intelligence;

namespace Airlock.Domain.Tests.Intelligence;

// BR-17, and validation of NVD data
public sealed class CveTests
{
    [Fact]
    public void A_CVE_links_to_its_NVD_page_and_knows_its_preferred_score()
    {
        var score = A.Score(9.8m, Severity.Critical);
        var cve = new Cve("CVE-2020-1747", "PyYAML full_load issue.", [score], ["pyyaml:pyyaml"]);

        Assert.Equal(new Uri("https://nvd.nist.gov/vuln/detail/CVE-2020-1747"), cve.NvdUrl);
        Assert.Same(score, cve.PreferredScore);
        Assert.Equal(["pyyaml:pyyaml"], cve.AffectedProducts);
    }

    [Theory]
    [InlineData("CVE-2020-1747")]
    [InlineData("CVE-2025-60876")]
    public void Well_formed_ids_are_accepted(string id)
    {
        Assert.Equal(id, A.Cve(id).Id);
    }

    [Theory]
    [InlineData("cve-2020-1747")]
    [InlineData("CVE-20-1747")]
    [InlineData("CVE-2020-174")]
    [InlineData("GHSA-xxxx-yyyy")]
    [InlineData("")]
    public void Malformed_ids_are_rejected(string id)
    {
        Assert.Throws<ArgumentException>(() => A.Cve(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_description_reads_as_not_available(string? description)
    {
        Assert.Equal(Cve.NoDescription, new Cve("CVE-2020-1747", description, [], []).Description);
    }

    [Fact]
    public void A_description_may_span_lines_but_not_carry_control_characters()
    {
        Assert.Equal("Line one.\n\tLine two.", new Cve("CVE-2020-1747", "Line one.\n\tLine two.", [], []).Description);
        Assert.Throws<ArgumentException>(() => new Cve("CVE-2020-1747", "Line one.\r\nLine two.", [], []));
        Assert.Throws<ArgumentException>(() => new Cve("CVE-2020-1747", "\u001b[31mred", [], []));
    }

    [Fact]
    public void Null_or_unsafe_collections_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new Cve("CVE-2020-1747", "d", null!, []));
        Assert.Throws<ArgumentNullException>(() => new Cve("CVE-2020-1747", "d", [], null!));
        Assert.Throws<ArgumentException>(() => new Cve("CVE-2020-1747", "d", [null!], []));
        Assert.Throws<ArgumentException>(() => new Cve("CVE-2020-1747", "d", [], ["vendor:\nproduct"]));
    }
}
