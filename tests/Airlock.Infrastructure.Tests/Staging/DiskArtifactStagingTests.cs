using Airlock.Application.Ports;
using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;
using Airlock.Infrastructure.Options;
using Airlock.Infrastructure.Staging;
using Airlock.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Airlock.Infrastructure.Tests.Staging;

// IR-11 to IR-13 — the layout, the byte limit mid-stream, the .zip suffix, escaping names, deletion; defect 5.
public sealed class DiskArtifactStagingTests : IDisposable
{
    private readonly TemporaryFolder folder = new();
    private readonly FakeTimeProvider clock = new(Test.StartTime);
    private readonly FakeLogger<DiskArtifactStaging> logger = new();

    private DiskArtifactStaging Staging =>
        new(Microsoft.Extensions.Options.Options.Create(new StagingOptions { Root = folder.Path }), clock, logger);

    [Fact]
    public async Task Each_scan_has_a_marked_folder_with_manifests_and_artifacts_inside()
    {
        var id = ScanId.New();

        await using var area = await Staging.CreateAsync(id, Test.Token);

        var root = folder.Combine(id.ToString());
        Assert.True(File.Exists(Path.Combine(root, StagingFolders.MarkerName)));
        Assert.Equal(Path.Combine(root, "manifests"), area.ManifestsDirectory);
        Assert.True(Directory.Exists(Path.Combine(root, "artifacts")));
    }

    [Fact]
    public async Task Files_sit_in_numbered_folders_under_their_own_names_with_wheels_and_NuGet_packages_as_zip()
    {
        var id = ScanId.New();
        await using var area = await Staging.CreateAsync(id, Test.Token);

        foreach (var (name, size) in new[]
        {
            ("requirements.txt", 10), ("app-image.tar", 20), ("PyYAML-5.3-cp38-cp38-win_amd64.whl", 30), ("package-lock.json", 40),
            ("Newtonsoft.Json.12.0.1.nupkg", 50),
        })
        {
            Assert.IsType<ArtifactStaged>(await AddAsync(area, name, size, long.MaxValue));
        }

        var root = folder.Combine(id.ToString());
        Assert.Equal(
            [
                "manifests/0/requirements.txt", "artifacts/1/app-image.tar", "artifacts/2/PyYAML-5.3-cp38-cp38-win_amd64.whl.zip",
                "manifests/3/package-lock.json", "artifacts/4/Newtonsoft.Json.12.0.1.nupkg.zip",
            ],
            area.Artifacts.Select(staged => Path.GetRelativePath(root, staged.Path).Replace('\\', '/')));
        Assert.Equal("PyYAML-5.3-cp38-cp38-win_amd64.whl", area.Artifacts[2].Artifact.FileName);
        Assert.Equal(ArtifactKind.Manifest, area.Artifacts[0].Artifact.Kind);
        Assert.Equal(30, new FileInfo(area.Artifacts[2].Path).Length);
        Assert.Equal(150, area.TotalBytes);
    }

    [Fact]
    public async Task A_file_past_the_bytes_left_is_dropped_whole()
    {
        await using var area = await Staging.CreateAsync(ScanId.New(), Test.Token);

        Assert.IsType<ArtifactTooLarge>(await AddAsync(area, "big.tar", 1024 * 1024, 100));

        Assert.Empty(area.Artifacts);
        Assert.Equal(0, area.TotalBytes);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(Path.GetDirectoryName(area.ManifestsDirectory)!, "artifacts")));
    }

    [Fact]
    public async Task A_file_of_exactly_the_bytes_left_is_kept()
    {
        await using var area = await Staging.CreateAsync(ScanId.New(), Test.Token);

        Assert.IsType<ArtifactStaged>(await AddAsync(area, "exact.tar", 100, 100));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("..\\escape.txt")]
    [InlineData("sub/evil.txt")]
    [InlineData("C:evil.txt")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData(" ")]
    public async Task A_name_that_would_leave_the_area_is_refused(string name)
    {
        await using var area = await Staging.CreateAsync(ScanId.New(), Test.Token);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => AddAsync(area, name, 10, long.MaxValue));

        Assert.Empty(area.Artifacts);
        Assert.False(File.Exists(folder.Combine("escape.txt")));
        Assert.False(File.Exists(folder.Combine("evil.txt")));
    }

    [Fact]
    public async Task A_cancelled_upload_leaves_nothing_behind()
    {
        await using var area = await Staging.CreateAsync(ScanId.New(), Test.Token);
        using var content = new FailingStream(new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => area.AddAsync("broken.tar", content, long.MaxValue, Test.Token).AsTask());

        Assert.Empty(area.Artifacts);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(Path.GetDirectoryName(area.ManifestsDirectory)!, "artifacts")));
    }

    [Fact]
    public async Task Disposing_deletes_the_folder_and_everything_in_it_once()
    {
        var id = ScanId.New();
        var area = await Staging.CreateAsync(id, Test.Token);
        await AddAsync(area, "requirements.txt", 10, long.MaxValue);

        await area.DisposeAsync();
        await area.DisposeAsync();

        Assert.False(Directory.Exists(folder.Combine(id.ToString())));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => AddAsync(area, "late.txt", 1, long.MaxValue));
    }

    [Fact]
    public async Task A_locked_file_is_waited_for_before_the_area_is_deleted()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to delete a file that is open.");
        var id = ScanId.New();
        var area = await Staging.CreateAsync(id, Test.Token);
        await AddAsync(area, "locked.tar", 10, long.MaxValue);
        var locked = new FileStream(area.Artifacts[0].Path, FileMode.Open, FileAccess.Read, FileShare.None);
        var disposing = area.DisposeAsync().AsTask();
        await WaitForRetryAsync();
        await locked.DisposeAsync();

        await clock.DriveAsync(disposing, TimeSpan.FromMilliseconds(100));

        Assert.False(Directory.Exists(folder.Combine(id.ToString())));
        Assert.Contains(logger.Collector.GetSnapshot(), record => record.Message.Contains("still in use", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_file_locked_for_good_fails_the_deletion_after_five_attempts()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to delete a file that is open.");
        var area = await Staging.CreateAsync(ScanId.New(), Test.Token);
        await AddAsync(area, "locked.tar", 10, long.MaxValue);
        using (new FileStream(area.Artifacts[0].Path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAsync<IOException>(() => clock.DriveAsync(area.DisposeAsync().AsTask(), TimeSpan.FromMilliseconds(100)));
        }

        Assert.Equal(
            StagingFolders.DeleteAttempts - 1,
            logger.Collector.GetSnapshot().Count(record => record.Message.Contains("still in use", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Folders_and_files_are_owner_only_on_Linux_and_macOS()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows keeps the temporary folder's access rules.");
        await using var area = await Staging.CreateAsync(ScanId.New(), Test.Token);
        await AddAsync(area, "requirements.txt", 10, long.MaxValue);

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, new DirectoryInfo(area.ManifestsDirectory).UnixFileMode);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(area.Artifacts[0].Path));
        }
    }

    public void Dispose() => folder.Dispose();

    private async Task WaitForRetryAsync()
    {
        using var deadline = Test.Deadline(10);
        while (!logger.Collector.GetSnapshot().Any(record => record.Message.Contains("still in use", StringComparison.Ordinal)))
        {
            await Task.Delay(10, deadline.Token);
        }
    }

    private static async Task<StagingOutcome> AddAsync(IStagingArea area, string name, int size, long maxBytes)
    {
        using var content = new MemoryStream(new byte[size]);
        return await area.AddAsync(name, content, maxBytes, Test.Token);
    }

    private sealed class FailingStream(Exception failure) : MemoryStream(new byte[100])
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Position == 0 ? ReadOnceAsync(buffer) : ValueTask.FromException<int>(failure);

        private ValueTask<int> ReadOnceAsync(Memory<byte> buffer)
        {
            Position = 10;
            buffer.Span[..10].Clear();
            return ValueTask.FromResult(10);
        }
    }
}
