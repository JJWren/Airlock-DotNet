using System.Text.Json;
using System.Text.RegularExpressions;
using Airlock.Delivery.Tests.Support;

namespace Airlock.Delivery.Tests;

// Q-U6-I1 — one SDK band: global.json names 10.0.403 with latestPatch, and the image builds with an SDK of that band,
// no older, so a Dependabot update of the image may merge before global.json's.
public sealed partial class SdkTests
{
    [Fact]
    public void Global_json_names_SDK_10_0_403_any_later_patch_and_the_testing_platform()
    {
        using var global = JsonDocument.Parse(Repository.Read("global.json"));
        var root = global.RootElement;

        Assert.Equal("10.0.403", root.GetProperty("sdk").GetProperty("version").GetString());
        Assert.Equal("latestPatch", root.GetProperty("sdk").GetProperty("rollForward").GetString());
        Assert.Equal("Microsoft.Testing.Platform", root.GetProperty("test").GetProperty("runner").GetString());
    }

    [Fact]
    public void The_image_builds_with_an_SDK_of_global_jsons_band_and_no_older()
    {
        using var global = JsonDocument.Parse(Repository.Read("global.json"));
        var pinned = Version.Parse(global.RootElement.GetProperty("sdk").GetProperty("version").GetString()!);
        var build = DockerfileModel.Stages("Dockerfile").Single(stage => stage.Name == "build").Image;
        var image = Version.Parse(SdkImageVersion().Match(build).Groups[1].Value);

        Assert.Equal((pinned.Major, pinned.Minor, pinned.Build / 100), (image.Major, image.Minor, image.Build / 100));
        Assert.True(image >= pinned, $"The image's SDK {image} is older than global.json's {pinned}");
    }

    [Fact]
    public void The_runtime_is_NET_10_on_chiseled_Ubuntu_24_04_with_ICU_and_time_zones()
    {
        var runtime = DockerfileModel.Stages("Dockerfile").Single(stage => stage.Name == "runtime").Image;

        Assert.Matches(@"^mcr\.microsoft\.com/dotnet/aspnet:10\.0\.\d+-noble-chiseled-extra@sha256:", runtime);
    }

    [GeneratedRegex(@"^mcr\.microsoft\.com/dotnet/sdk:(\d+\.\d+\.\d+)-noble@sha256:")]
    private static partial Regex SdkImageVersion();
}
