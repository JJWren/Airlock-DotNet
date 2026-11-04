using Airlock.Delivery.Tests.Support;

namespace Airlock.Delivery.Tests;

// DR-28, U6-NFR-04 — zizmor's settings: a commit SHA for every action, GitHub's own actions only, and every accepted
// finding in .github/zizmor.yml, still naming the line it was written for.
public sealed class ZizmorConfigTests
{
    [Fact]
    public void Every_action_must_be_hash_pinned_and_only_GitHubs_own_are_allowed()
    {
        var zizmor = Yaml.Load(".github/zizmor.yml");

        Assert.Equal(["*"], zizmor.Keys("rules", "unpinned-uses", "config", "policies"));
        Assert.Equal("hash-pin", zizmor.Text("rules", "unpinned-uses", "config", "policies", "*"));
        Assert.Equal(["actions/*", "github/codeql-action/*"], zizmor.Texts("rules", "forbidden-uses", "config", "allow"));
    }

    [Fact]
    public void No_finding_is_ignored_inline()
    {
        Assert.All([.. Repository.Workflows, ".github/dependabot.yml"], file =>
            Assert.DoesNotContain("zizmor: ignore", Repository.Read(file), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("dependabot-cooldown", ".github/dependabot.yml", "- package-ecosystem: dotnet-sdk")]
    [InlineData("self-repository", ".github/workflows/release.yml", "uses: ./.github/workflows/ci.yml")]
    public void Each_accepted_finding_names_the_line_it_was_written_for(string audit, string file, string line)
    {
        var ignore = Assert.Single(Yaml.Load(".github/zizmor.yml").Texts("rules", audit, "ignore"));
        var at = ignore.Split(':');

        Assert.Equal(Path.GetFileName(file), at[0]);
        Assert.Equal(line, Repository.Lines(file)[int.Parse(at[1], System.Globalization.CultureInfo.InvariantCulture) - 1].Trim());
    }

    [Fact]
    public void Those_are_the_only_findings_accepted()
    {
        var accepted = Yaml.Load(".github/zizmor.yml").Entries("rules").Where(rule => rule.Value.Find("ignore") is not null).Select(rule => rule.Key);

        Assert.Equal(["dependabot-cooldown", "self-repository"], accepted);
    }
}
