using System.Text.RegularExpressions;
using Airlock.Delivery.Tests.Support;

namespace Airlock.Delivery.Tests;

// DR-04 — Syft has one version: the two install scripts, the Dockerfile's syft stage and the README name the same one,
// so a Syft update is one pull request that moves all of them.
public sealed partial class SyftVersionTests
{
    [Fact]
    public void The_install_scripts_the_Dockerfile_and_the_README_name_one_Syft_version()
    {
        var version = InstallShVersion();
        var syft = DockerfileModel.Stages("Dockerfile").Single(stage => stage.Name == "syft").Image;
        var readme = Repository.Read("README.md");

        Assert.Equal(version, Single(PowerShellVersion(), "scripts/install-tools.ps1"));
        Assert.Equal(version, SyftImageVersion().Match(syft).Groups[1].Value);
        Assert.Contains($"Syft v{version}", readme, StringComparison.Ordinal);
        Assert.All(ReadmeSyftVersion().Matches(readme), named => Assert.Equal(version, named.Groups[1].Value));
    }

    [Theory]
    [InlineData("scripts/install-tools.sh")]
    [InlineData("scripts/install-tools.ps1")]
    public void Every_archive_a_script_carries_a_checksum_for_is_of_that_version(string script)
    {
        var archives = ArchiveVersion().Matches(Repository.Read(script)).Select(archive => archive.Groups[1].Value).ToList();

        Assert.NotEmpty(archives);
        Assert.All(archives, archive => Assert.Equal(InstallShVersion(), archive));
    }

    internal static string InstallShVersion() => Single(ShellVersion(), "scripts/install-tools.sh");

    private static string Single(Regex version, string file) =>
        Assert.Single(version.Matches(Repository.Read(file))).Groups[1].Value;

    [GeneratedRegex(@"^version=(\d+\.\d+\.\d+)$", RegexOptions.Multiline)]
    private static partial Regex ShellVersion();

    [GeneratedRegex(@"^\$version = '(\d+\.\d+\.\d+)'$", RegexOptions.Multiline)]
    private static partial Regex PowerShellVersion();

    [GeneratedRegex(@"^ghcr\.io/anchore/syft:v(\d+\.\d+\.\d+)@")]
    private static partial Regex SyftImageVersion();

    [GeneratedRegex(@"\bSyft v(\d+\.\d+\.\d+)")]
    private static partial Regex ReadmeSyftVersion();

    [GeneratedRegex(@"syft_(\d+\.\d+\.\d+)_\w+\.(tar\.gz|zip)")]
    private static partial Regex ArchiveVersion();
}
