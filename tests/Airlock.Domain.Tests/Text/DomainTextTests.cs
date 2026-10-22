using Airlock.Domain.Intelligence;
using Airlock.Domain.Targets;
using Airlock.Domain.Text;

namespace Airlock.Domain.Tests.Text;

// U1-NFR-04: bounded text without control or bidirectional formatting characters.
public sealed class DomainTextTests
{
    public static TheoryData<char> ForbiddenCharacters => new()
    {
        '\0', '\a', '\b', '\t', '\n', '\r', '\u001b', '\u007f', '\u0085', '\u009b',
        (char)0x200E, (char)0x200F, (char)0x202A, (char)0x202B, (char)0x202C, (char)0x202D, (char)0x202E, (char)0x2066, (char)0x2067, (char)0x2068, (char)0x2069,
    };

    [Theory]
    [MemberData(nameof(ForbiddenCharacters))]
    public void Control_and_bidirectional_characters_are_rejected(char forbidden)
    {
        var exception = Assert.Throws<ArgumentException>(() => new Package("python", $"py{forbidden}yaml", "5.3"));
        Assert.Contains($"U+{(int)forbidden:X4}", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData('\n', true)]
    [InlineData('\t', true)]
    [InlineData('\r', false)]
    [InlineData('\u001b', false)]
    [InlineData((char)0x202E, false)]
    [InlineData('a', true)]
    public void Only_line_feeds_and_tabs_are_allowed_where_line_breaks_are(char c, bool allowed)
    {
        Assert.Equal(!allowed, DomainText.IsForbidden(c, allowLineBreaks: true));
    }

    [Fact]
    public void Each_limit_is_enforced_at_its_boundary()
    {
        AssertLimit(DomainLimits.PackageType, length => new Package(new string('t', length), "pyyaml", "5.3"));
        AssertLimit(DomainLimits.PackageName, length => new Package("python", new string('n', length), "5.3"));
        AssertLimit(DomainLimits.PackageVersion, length => new Package("python", "pyyaml", new string('1', length)));
        AssertLimit(DomainLimits.Cpe, length => new CandidateCpe("cpe:2.3:" + new string('a', length - 8), CpeSource.Generated));
        AssertLimit(DomainLimits.FileName, length => new Artifact(new string('f', length), 1));
        AssertLimit(DomainLimits.CveDescription, length => new Cve("CVE-2020-1747", new string('d', length), [], []));
    }

    [Fact]
    public void Optional_text_may_be_absent_but_not_blank()
    {
        Assert.Null(DomainText.Optional(null, 10, "value"));
        Assert.Throws<ArgumentException>(() => DomainText.Optional(" ", 10, "value"));
    }

    private static void AssertLimit(int limit, Func<int, object> create)
    {
        Assert.NotNull(create(limit));
        var exception = Assert.Throws<ArgumentException>(() => create(limit + 1));
        Assert.Contains("at most", exception.Message, StringComparison.Ordinal);
    }
}
