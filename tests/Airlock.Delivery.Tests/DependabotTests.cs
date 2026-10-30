using Airlock.Delivery.Tests.Support;
using YamlDotNet.RepresentationModel;

namespace Airlock.Delivery.Tests;

// DR-16, U6-NFR-02 — weekly updates for the four ecosystems, minor and patch grouped, a 7-day cooldown with its named
// exceptions, Syft's image alone, and Airlock held on .NET 10 LTS.
public sealed class DependabotTests
{
    private static readonly YamlMappingNode Dependabot = Yaml.Load(".github/dependabot.yml");

    [Fact]
    public void Four_ecosystems_are_updated_weekly()
    {
        Assert.Equal("2", Dependabot.Text("version"));
        Assert.Equal(["nuget", "docker", "github-actions", "dotnet-sdk"], Updates().Select(update => update.Text("package-ecosystem")));
        Assert.All(Updates(), update => Assert.Equal("weekly", update.Text("schedule", "interval")));
        Assert.Equal(["/"], Update("nuget").Texts("directory"));
        Assert.Equal(["/", "/.github/ci-tools"], Update("docker").Texts("directories"));
        Assert.Equal(["/"], Update("github-actions").Texts("directory"));
        Assert.Equal(["/"], Update("dotnet-sdk").Texts("directory"));
    }

    [Theory]
    [InlineData("nuget")]
    [InlineData("docker")]
    [InlineData("github-actions")]
    public void Minor_and_patch_updates_come_in_one_group_and_wait_7_days(string ecosystem)
    {
        var group = Assert.Single(Update(ecosystem).Entries("groups")).Value;

        Assert.Equal(["*"], group.Texts("patterns"));
        Assert.Equal(["minor", "patch"], group.Texts("update-types"));
        Assert.Equal("7", Update(ecosystem).Text("cooldown", "default-days"));
    }

    [Fact]
    public void Syfts_image_comes_alone_and_Microsofts_images_do_not_wait()
    {
        var docker = Update("docker");

        Assert.Equal(["*anchore/syft*"], Assert.Single(docker.Entries("groups")).Value.Texts("exclude-patterns"));
        Assert.Equal(["*dotnet/*"], docker.Texts("cooldown", "exclude"));
    }

    [Fact]
    public void The_SDK_does_not_wait()
    {
        Assert.Null(Update("dotnet-sdk").Find("cooldown"));
        Assert.Null(Update("dotnet-sdk").Find("groups"));
    }

    [Theory]
    [InlineData("nuget", "Microsoft.Extensions.*")]
    [InlineData("nuget", "Microsoft.AspNetCore.*")]
    [InlineData("docker", "*dotnet/*")]
    [InlineData("dotnet-sdk", "*")]
    public void No_major_update_of_NET_itself_is_proposed(string ecosystem, string dependency)
    {
        var rule = Assert.Single(Update(ecosystem).Items("ignore"), rule => rule.Text("dependency-name") == dependency);

        Assert.Equal(["version-update:semver-major"], rule.Texts("update-types"));
    }

    [Fact]
    public void Nothing_else_is_ignored()
    {
        Assert.Equal(2, Update("nuget").Items("ignore").Count);
        Assert.Single(Update("docker").Items("ignore"));
        Assert.Empty(Update("github-actions").Items("ignore"));
        Assert.Single(Update("dotnet-sdk").Items("ignore"));
    }

    private static IReadOnlyList<YamlNode> Updates() => Dependabot.Items("updates");

    private static YamlNode Update(string ecosystem) => Assert.Single(Updates(), update => update.Text("package-ecosystem") == ecosystem);
}
