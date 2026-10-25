using Airlock.Application.Outcomes;
using Airlock.Application.Ports;
using Airlock.Application.Scanning;
using Airlock.Application.Tests.Fakes;
using Airlock.Domain.Targets;

namespace Airlock.Application.Tests.UseCases;

// AR-04 to AR-07 — every row of the targets table, in Linux and Windows modes (U2-NFR-12).
public sealed class ScanTargetTests
{
    public static TheoryData<HostMode> Modes => new() { HostMode.Linux, HostMode.Windows };

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task Nothing_to_scan_is_refused(HostMode mode)
    {
        using var harness = new ScanHarness(mode);
        await using var reservation = await harness.ReserveAsync();

        var started = await reservation.StartAsync("   ", harness.Token);

        AssertRefused(started, "Choose a directory or upload at least one artifact");
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task A_directory_and_artifacts_together_are_refused(HostMode mode)
    {
        using var harness = new ScanHarness(mode);
        await using var reservation = await harness.ReserveAsync();
        using (var content = new MemoryStream([1]))
        {
            Assert.True((await reservation.AddArtifactAsync("requirements.txt", content, harness.Token)).IsSuccess);
        }

        var started = await reservation.StartAsync(ScanHarness.Directory, harness.Token);

        AssertRefused(started, "Choose a directory or upload artifacts, not both");
    }

    [Theory]
    [InlineData(HostMode.Linux, "uploads/acme-app", "Target path must be absolute")]
    [InlineData(HostMode.Windows, "uploads/acme-app", "Target path must be absolute")]
    [InlineData(HostMode.Linux, "/uploads/acme-ap", "Target path does not exist")]
    [InlineData(HostMode.Windows, "/uploads/acme-ap", "Target path does not exist")]
    [InlineData(HostMode.Linux, "/UPLOADS/acme-app", "Target path does not exist")]
    [InlineData(HostMode.Linux, "/uploads/acme-app/requirements.txt", "Target path is a file, not a directory")]
    [InlineData(HostMode.Windows, "/uploads/acme-app/requirements.txt", "Target path is a file, not a directory")]
    public async Task A_directory_that_cannot_be_scanned_is_refused(HostMode mode, string path, string reason)
    {
        using var harness = new ScanHarness(mode);
        harness.FileSystem.WithFile("/uploads/acme-app/requirements.txt");
        await using var reservation = await harness.ReserveAsync();

        var started = await reservation.StartAsync(path, harness.Token);

        AssertRefused(started, reason);
        Assert.Empty(harness.Registry.List());
    }

    [Fact]
    public async Task Both_kinds_of_target_can_be_refused_before_anything_is_staged()
    {
        using var harness = new ScanHarness();
        await using var reservation = await harness.ReserveAsync();

        var refusal = reservation.RefuseBothTargets();

        Assert.Equal(new Refusal(RefusalCode.InvalidTarget, "Choose a directory or upload artifacts, not both"), refusal);
        Assert.Equal(refusal, (await reservation.StartAsync(ScanHarness.Directory, harness.Token)).Refusal);
        Assert.Empty(harness.Staging.Created);
        Assert.Empty(harness.Registry.List());
    }

    [Fact]
    public async Task An_unreadable_target_path_is_not_valid_and_keeps_refusing()
    {
        using var harness = new ScanHarness();
        await using var reservation = await harness.ReserveAsync();

        var refusal = reservation.RefuseUnreadableTargetPath();
        using var content = new MemoryStream([1]);
        var upload = await reservation.AddArtifactAsync("requirements.txt", content, harness.Token);

        Assert.Equal(new Refusal(RefusalCode.InvalidTarget, "Target path is not valid"), refusal);
        Assert.Equal(refusal, upload.Refusal);
        Assert.Equal(refusal, reservation.RefuseBothTargets());
    }

    [Fact]
    public async Task An_early_refusal_after_another_keeps_the_first()
    {
        using var harness = new ScanHarness(configure: options => options.Limits.MaxArtifactsPerScan = 1);
        await using var reservation = await harness.ReserveAsync();
        using (var first = new MemoryStream([1]))
        {
            Assert.True((await reservation.AddArtifactAsync("requirements.txt", first, harness.Token)).IsSuccess);
        }

        using var second = new MemoryStream([1]);
        var tooMany = (await reservation.AddArtifactAsync("package-lock.json", second, harness.Token)).Refusal;

        Assert.Equal(tooMany, reservation.RefuseBothTargets());
    }

    [Fact]
    public async Task A_path_the_domain_would_reject_is_not_valid()
    {
        using var harness = new ScanHarness();
        await using var reservation = await harness.ReserveAsync();

        AssertRefused(await reservation.StartAsync($"/uploads/acme{(char)7}app", harness.Token), "Target path is not valid");
        AssertRefused(await reservation.StartAsync("/" + new string('a', 5_000), harness.Token), "Target path is not valid");
    }

    [Theory]
    [InlineData(HostMode.Linux, "/etc")]
    [InlineData(HostMode.Windows, "/etc")]
    [InlineData(HostMode.Linux, "/uploads2/acme")]
    [InlineData(HostMode.Windows, "/uploads2/acme")]
    [InlineData(HostMode.Linux, "/uploads/link-to-etc")]
    [InlineData(HostMode.Windows, "/uploads/link-to-etc")]
    public async Task A_path_outside_the_allowed_roots_is_refused(HostMode mode, string path)
    {
        using var harness = new ScanHarness(mode, options => options.AllowedScanRoots = ["/uploads"]);
        harness.FileSystem.WithDirectory("/etc").WithDirectory("/uploads2/acme").WithLink("/uploads/link-to-etc", "/etc");
        await using var reservation = await harness.ReserveAsync();

        var started = await reservation.StartAsync(path, harness.Token);

        AssertRefused(started, "Path is outside the allowed scan roots");
    }

    [Theory]
    [InlineData(HostMode.Linux, "/uploads/acme-app/../acme-app", "/uploads/acme-app")]
    [InlineData(HostMode.Windows, "/uploads/acme-app/../acme-app", "C:/uploads/acme-app")]
    [InlineData(HostMode.Windows, "/UPLOADS/ACME-APP", "C:/UPLOADS/ACME-APP")]
    public async Task A_path_inside_the_allowed_roots_is_scanned_as_its_full_path(HostMode mode, string path, string scanned)
    {
        using var harness = new ScanHarness(mode, options => options.AllowedScanRoots = ["/uploads"]);

        var scan = await harness.RunDirectoryScanAsync(path);

        var expected = mode == HostMode.Windows ? scanned.Replace('/', (char)92) : scanned;
        Assert.Equal(expected, Assert.IsType<DirectoryTarget>(scan.Target).Path);
        Assert.Equal(expected, Assert.IsType<DirectorySource>(Assert.Single(harness.Sbom.Calls)).Path);
    }

    [Fact]
    public async Task Without_allowed_roots_any_existing_directory_is_scanned()
    {
        using var harness = new ScanHarness();
        harness.FileSystem.WithDirectory("/etc");

        var scan = await harness.RunDirectoryScanAsync("/etc");

        Assert.Equal("/etc", Assert.IsType<DirectoryTarget>(scan.Target).Path);
    }

    [Fact]
    public async Task Twenty_five_files_are_accepted_and_the_twenty_sixth_refused()
    {
        using var harness = new ScanHarness();
        await using var reservation = await harness.ReserveAsync();

        for (var i = 0; i < 25; i++)
        {
            using var content = new MemoryStream([1]);
            Assert.True((await reservation.AddArtifactAsync($"file-{i}.bin", content, harness.Token)).IsSuccess);
        }

        using var last = new MemoryStream([1]);
        var refused = await reservation.AddArtifactAsync("file-25.bin", last, harness.Token);

        Assert.Equal(RefusalCode.TooManyArtifacts, refused.Refusal!.Code);
        Assert.Equal("At most 25 files per scan", refused.Refusal.Message);
    }

    [Theory]
    [InlineData(1024L * 1024 * 1024, "Uploads are limited to 1 GB per scan")]
    [InlineData(3L * 1024 * 1024, "Uploads are limited to 3 MB per scan")]
    public async Task The_byte_limit_stops_an_upload_while_it_streams(long limit, string message)
    {
        using var harness = new ScanHarness(configure: options => options.Limits.MaxBytesPerScan = limit);
        await using var reservation = await harness.ReserveAsync();
        using (var first = new MemoryStream(new byte[1024]))
        {
            Assert.True((await reservation.AddArtifactAsync("first.bin", first, harness.Token)).IsSuccess);
        }

        using var oversized = new ZeroStream(limit);
        var refused = await reservation.AddArtifactAsync("second.bin", oversized, harness.Token);

        Assert.Equal(RefusalCode.TooLarge, refused.Refusal!.Code);
        Assert.Equal(message, refused.Refusal.Message);
        Assert.Single(harness.Staging.Created.Single().Artifacts);
    }

    [Fact]
    public async Task Uploaded_names_are_sanitized_and_duplicates_are_kept()
    {
        using var harness = new ScanHarness();

        var scan = await harness.RunUploadScanAsync("../../evil.txt", "requirements.txt", "requirements.txt");

        var area = Assert.Single(harness.Staging.Created);
        Assert.Equal(["evil.txt", "requirements.txt", "requirements.txt"], area.ReceivedNames);
        Assert.Equal(3, Assert.IsType<ArtifactsTarget>(scan.Target).Artifacts.Count);
    }

    [Theory]
    [InlineData("../../evil.txt", "evil.txt")]
    [InlineData("C:/Users/analyst/requirements.txt", "requirements.txt")]
    [InlineData("a<b>c.txt", "a_b_c.txt")]
    [InlineData("what?.json", "what_.json")]
    [InlineData("pipe|name*.zip", "pipe_name_.zip")]
    [InlineData("trailing. . ", "trailing")]
    [InlineData("...", "artifact")]
    [InlineData("", "artifact")]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("nul", "_nul")]
    [InlineData("lpt1.tar.gz", "_lpt1.tar.gz")]
    [InlineData("console.log", "console.log")]
    public void Artifact_names_are_safe_on_Windows_and_Linux(string uploaded, string stored)
    {
        Assert.Equal(stored, ArtifactName.Sanitize(uploaded));
    }

    [Fact]
    public void Artifact_names_lose_backslash_paths_quotes_and_control_characters_and_keep_their_extension()
    {
        var backslash = (char)92;

        Assert.Equal("a.txt", ArtifactName.Sanitize($"C:{backslash}x{backslash}a.txt"));
        Assert.Equal("quote_name", ArtifactName.Sanitize($"quote{'"'}name"));
        Assert.Equal("a_b.txt", ArtifactName.Sanitize($"a{(char)7}b.txt"));
        Assert.Equal("a_b.txt", ArtifactName.Sanitize($"a{(char)0x202E}b.txt"));
        Assert.Equal("artifact", ArtifactName.Sanitize(null));

        var shortened = ArtifactName.Sanitize(new string('a', 300) + ".txt");
        Assert.Equal(255, shortened.Length);
        Assert.EndsWith(".txt", shortened, StringComparison.Ordinal);
        Assert.Equal(255, ArtifactName.Sanitize(new string('b', 300)).Length);
    }

    private static void AssertRefused<T>(Outcome<T> outcome, string message)
    {
        Assert.False(outcome.IsSuccess);
        Assert.Equal(RefusalCode.InvalidTarget, outcome.Refusal!.Code);
        Assert.Equal(message, outcome.Refusal.Message);
    }
}
