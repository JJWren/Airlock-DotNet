using Airlock.Domain.Results;
using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;

namespace Airlock.Domain.Tests;

// Validation and formatting of the small value types.
public sealed class SmallValueTests
{
    [Fact]
    public void Scan_ids_are_time_ordered_GUIDs_written_in_D_format()
    {
        var id = ScanId.New();

        Assert.Equal(7, id.Value.Version);
        Assert.Equal(id.Value.ToString("D"), id.ToString());
        Assert.True(ScanId.TryParse(id.ToString(), out var parsed));
        Assert.Equal(id, parsed);
        Assert.Throws<ArgumentException>(() => new ScanId(Guid.Empty));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("{0199f0f2-8f62-7c3e-9a51-3d3f5b5c1a2b}")]
    public void Malformed_or_empty_scan_ids_do_not_parse(string? text)
    {
        Assert.False(ScanId.TryParse(text, out var id));
        Assert.Equal(default, id);
    }

    [Fact]
    public void Progress_cannot_exceed_its_total_or_be_negative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScanProgress(-1, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScanProgress(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScanProgress(6, 5));
    }

    [Fact]
    public void Warnings_and_failures_need_a_known_code_and_a_message()
    {
        var warning = new ScanWarning(ScanWarningCode.CvesWithoutSeverity, "CVE-2025-60876 has no CVSS score yet.", "busybox 1.36.1-r31");
        Assert.Equal("busybox 1.36.1-r31", warning.Input);
        Assert.Null(new ScanWarning(ScanWarningCode.NoPackagesIdentified, "No packages identified in notes.docx.").Input);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScanWarning((ScanWarningCode)42, "x"));
        Assert.Throws<ArgumentException>(() => new ScanWarning(ScanWarningCode.NoPackagesIdentified, ""));

        var failure = new ScanFailure(ScanFailureCode.ScanTimedOut, "Scan exceeded the 60-minute limit.");
        Assert.Equal(ScanFailureCode.ScanTimedOut, failure.Code);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScanFailure((ScanFailureCode)42, "x"));
        Assert.Throws<ArgumentException>(() => new ScanFailure(ScanFailureCode.Unexpected, " "));
    }

    [Fact]
    public void Scan_targets_need_a_path_or_at_least_one_artifact()
    {
        Assert.Equal("/uploads", new DirectoryTarget("/uploads").Path);
        Assert.Throws<ArgumentException>(() => new DirectoryTarget(""));

        var artifacts = new ArtifactsTarget([new Artifact("requirements.txt", 56)]);
        Assert.Single(artifacts.Artifacts);
        Assert.Throws<ArgumentException>(() => new ArtifactsTarget([]));
        Assert.Throws<ArgumentException>(() => new ArtifactsTarget([null!]));
        Assert.Throws<ArgumentNullException>(() => new ArtifactsTarget(null!));
    }

    [Fact]
    public void Match_methods_name_their_CPE_or_product()
    {
        var cpe = A.DictionaryCpe("python", "requests", "2.25.1");

        Assert.Same(cpe, new CpeMatch(cpe).Cpe);
        Assert.Equal("minimist", new ProductMatch("minimist").Product);
        Assert.Throws<ArgumentNullException>(() => new CpeMatch(null!));
        Assert.Throws<ArgumentException>(() => new ProductMatch(""));
    }
}
