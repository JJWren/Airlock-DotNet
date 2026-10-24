using System.Text;
using Airlock.Application.Ports;
using Airlock.Domain.Intelligence;
using Airlock.Infrastructure.Syft;
using Airlock.Infrastructure.Tests.Support;

namespace Airlock.Infrastructure.Tests.Syft;

// IR-03 — every recorded S0 output (S1 to S12); CPE sources; image detection; unreadable and unrepresentable output.
public sealed class SyftJsonParserTests
{
    public static TheoryData<string, int, bool> Outputs => new()
    {
        { "manifest-file", 4, false },
        { "lockfile-file", 3, false },
        { "manifests-dir", 7, false },
        { "jar-file", 1, false },
        { "whl-as-zip", 1, false },
        { "nupkg-dll-as-zip", 1, false },
        { "zip-file", 7, false },
        { "targz-file", 7, false },
        { "image-file", 14, true },
        { "gobin-file", 281, false },
        { "dotnet-file", 1, false },
    };

    [Theory]
    [MemberData(nameof(Outputs))]
    public async Task Every_recorded_output_maps_to_its_packages(string output, int packages, bool image)
    {
        var produced = await ParseAsync(output);

        Assert.Equal(packages, produced.Packages.Count);
        Assert.Equal(image, produced.IsContainerImage);
    }

    [Fact]
    public async Task A_requirements_file_gives_its_four_python_packages()
    {
        var produced = await ParseAsync("manifest-file");

        Assert.Equal(
            ["python:flask 0.12.2", "python:jinja2 2.10", "python:pyyaml 5.3", "python:requests 2.25.1"],
            produced.Packages.Select(package => $"{package.Package.Type}:{package.Package}"));
    }

    [Fact]
    public async Task A_lock_file_lists_its_own_project_with_its_dependencies()
    {
        var produced = await ParseAsync("lockfile-file");

        Assert.Equal(
            ["npm:fixture-app 1.0.0", "npm:lodash 4.17.20", "npm:minimist 1.2.5"],
            produced.Packages.Select(package => $"{package.Package.Type}:{package.Package}"));
    }

    [Fact]
    public async Task Candidate_CPEs_keep_Syfts_order_and_their_source()
    {
        var produced = await ParseAsync("manifest-file");

        var pyyaml = produced.Packages.Single(package => package.Package.Name == "pyyaml");
        Assert.Equal(12, pyyaml.CandidateCpes.Count);
        Assert.All(pyyaml.CandidateCpes, cpe => Assert.Equal(CpeSource.Generated, cpe.Source));
        Assert.Equal("python-pyyaml:python-pyyaml", $"{pyyaml.CandidateCpes[0].Vendor}:{pyyaml.CandidateCpes[0].Product}");
        Assert.Equal("pyyaml:pyyaml", $"{pyyaml.CandidateCpes[^1].Vendor}:{pyyaml.CandidateCpes[^1].Product}");
        var requests = produced.Packages.Single(package => package.Package.Name == "requests");
        Assert.Contains(requests.CandidateCpes, cpe => cpe.Source == CpeSource.Dictionary);
    }

    [Fact]
    public async Task Syfts_own_binary_keeps_versions_and_candidates_as_reported()
    {
        var produced = await ParseAsync("gobin-file");

        Assert.Equal("UNKNOWN", produced.Packages.Single(package => package.Package.Name == "syft").Package.Version);
        Assert.Equal(10, produced.Packages.Count(package => package.CandidateCpes.Count == 0));
        Assert.Contains(produced.Packages, package => package.Package is { Type: "go-module", Version: "v0.23.3" });
    }

    [Fact]
    public async Task An_image_archive_is_marked_and_keeps_distribution_revisions()
    {
        var produced = await ParseAsync("image-file");

        Assert.True(produced.IsContainerImage);
        Assert.Equal("1.36.1-r31", produced.Packages.Single(package => package.Package.Name == "busybox").Package.Version);
    }

    [Fact]
    public async Task A_dotnet_assembly_has_no_candidate_CPEs()
    {
        var package = Assert.Single((await ParseAsync("dotnet-file")).Packages);

        Assert.Equal("dotnet:System.Text.Json 10.0.11", $"{package.Package.Type}:{package.Package}");
        Assert.Empty(package.CandidateCpes);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"artifacts\": [")]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task Output_that_is_not_Syft_JSON_is_unreadable(string text)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));

        var parse = await SyftJsonParser.ParseAsync(stream, Test.Token);

        Assert.Null(parse.Document);
        Assert.False(string.IsNullOrWhiteSpace(parse.Problem));
    }

    [Theory]
    [InlineData("{\"artifacts\":[{\"type\":\"python\",\"name\":\"evil\\u001b[31m\",\"version\":\"1.0\"}]}")]
    [InlineData("{\"artifacts\":[{\"type\":\"python\",\"name\":\"\",\"version\":\"1.0\"}]}")]
    [InlineData("{\"artifacts\":[{\"type\":\"python\",\"name\":\"x\",\"version\":\"1.0\",\"cpes\":[{\"cpe\":\"not-a-cpe\"}]}]}")]
    public async Task A_package_Airlock_cannot_represent_fails_the_whole_output(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var parse = await SyftJsonParser.ParseAsync(stream, Test.Token);

        Assert.ThrowsAny<ArgumentException>(() => SyftJsonParser.Map(parse.Document!));
    }

    [Fact]
    public async Task A_version_over_the_Domains_limit_fails_the_whole_output()
    {
        var json = "{\"artifacts\":[{\"type\":\"npm\",\"name\":\"x\",\"version\":\"" + new string('9', 300) + "\"}]}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var parse = await SyftJsonParser.ParseAsync(stream, Test.Token);

        Assert.ThrowsAny<ArgumentException>(() => SyftJsonParser.Map(parse.Document!));
    }

    private static async Task<SbomProduced> ParseAsync(string output)
    {
        await using var stream = File.OpenRead(Fixtures.SyftOutput(output));
        var parse = await SyftJsonParser.ParseAsync(stream, Test.Token);
        return SyftJsonParser.Map(parse.Document ?? throw new InvalidOperationException(parse.Problem));
    }
}
