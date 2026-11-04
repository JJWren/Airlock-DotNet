using System.Text.RegularExpressions;
using Airlock.Delivery.Tests.Support;

namespace Airlock.Delivery.Tests;

// DR-27, U6-NFR-01 — everything CI runs is pinned: actions by their full commit SHA, images by tag and digest, and
// Syft's archives by the SHA-256 the install scripts carry.
public sealed partial class PinTests
{
    public static TheoryData<string> Workflows => [.. Repository.Workflows];

    [Theory]
    [MemberData(nameof(Workflows))]
    public void Every_action_is_named_by_its_full_commit_SHA_with_its_version_as_a_comment(string workflow)
    {
        var uses = Repository.Lines(workflow)
            .Select((text, index) => (Text: text, Line: index + 1))
            .Where(line => UsesKey().IsMatch(line.Text))
            .ToList();

        Assert.NotEmpty(uses);
        Assert.All(uses, use => Assert.True(PinnedUses().IsMatch(use.Text), $"{workflow}:{use.Line}: {use.Text.Trim()}"));
    }

    [Theory]
    [InlineData("Dockerfile")]
    [InlineData(".github/ci-tools/Dockerfile")]
    public void Every_image_is_named_by_its_tag_and_its_digest(string dockerfile)
    {
        var images = DockerfileModel.Stages(dockerfile).Select(stage => stage.Image).ToList();

        Assert.NotEmpty(images);
        Assert.All(images, image => Assert.Matches(PinnedImage(), image));
    }

    [Fact]
    public void The_install_scripts_carry_the_same_SHA_256_for_each_archive_they_can_fetch()
    {
        var shell = Pins(ShellPin(), "scripts/install-tools.sh");
        var powershell = Pins(PowerShellPin(), "scripts/install-tools.ps1");
        string[] unix = ["darwin_amd64", "darwin_arm64", "linux_amd64", "linux_arm64"];
        var version = SyftVersionTests.InstallShVersion();

        Assert.Equal(unix.Select(platform => $"syft_{version}_{platform}.tar.gz").Order(StringComparer.Ordinal), shell.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(
            [.. unix.Select(platform => $"syft_{version}_{platform}.tar.gz"), $"syft_{version}_windows_amd64.zip", $"syft_{version}_windows_arm64.zip"],
            powershell.Keys.Order(StringComparer.Ordinal));
        Assert.All(shell, pin => Assert.Equal(pin.Value, powershell[pin.Key]));
    }

    private static Dictionary<string, string> Pins(Regex pin, string script) =>
        pin.Matches(Repository.Read(script)).ToDictionary(match => match.Groups["archive"].Value, match => match.Groups["sha"].Value, StringComparer.Ordinal);

    [GeneratedRegex(@"^\s*(-\s+)?uses:")]
    private static partial Regex UsesKey();

    // A reusable workflow of this repository, at this commit; or an action at a full SHA, its version as a comment.
    [GeneratedRegex(@"^\s*(-\s+)?uses:\s+(\./\.github/workflows/[\w.-]+\.yml|[\w.-]+/[\w./-]+@[0-9a-f]{40} # v\d+\.\d+\.\d+)$")]
    private static partial Regex PinnedUses();

    [GeneratedRegex(@"^[a-z0-9.-]+(/[a-z0-9._-]+)+:[\w.-]+@sha256:[0-9a-f]{64}$")]
    private static partial Regex PinnedImage();

    [GeneratedRegex(@"^\s*(?<archive>syft_[\d.]+_\w+\.tar\.gz)\)\s+pinned=(?<sha>[0-9a-f]{64})\s*;;", RegexOptions.Multiline)]
    private static partial Regex ShellPin();

    [GeneratedRegex(@"'(?<archive>syft_[\d.]+_\w+\.(tar\.gz|zip))'\s*=\s*'(?<sha>[0-9a-f]{64})'")]
    private static partial Regex PowerShellPin();
}
