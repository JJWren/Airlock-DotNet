using Airlock.Infrastructure.Options;
using Airlock.Infrastructure.Staging;
using Airlock.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Airlock.Infrastructure.Tests.Staging;

// U3-NFR-06, IR-13 — read-only entries, the startup sweep of marked folders, and nothing unmarked touched.
public sealed class StagingFoldersTests : IDisposable
{
    private readonly TemporaryFolder folder = new();
    private readonly FakeTimeProvider clock = new(Test.StartTime);
    private readonly FakeLogger<StagingSweepService> logger = new();

    [Fact]
    public async Task Read_only_files_and_folders_are_deleted_without_waiting()
    {
        var area = folder.Combine("area");
        var nested = Path.Combine(area, "unpacked", "inner");
        Directory.CreateDirectory(nested);
        var file = Path.Combine(nested, "read-only.txt");
        await File.WriteAllTextAsync(file, "x", Test.Token);
        File.SetAttributes(file, FileAttributes.ReadOnly);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(nested, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }

        await StagingFolders.DeleteAsync(area, clock, logger, Test.Token).WaitAsync(TimeSpan.FromSeconds(10), Test.Token);

        Assert.False(Directory.Exists(area));
    }

    [Fact]
    public async Task Deleting_a_folder_that_is_gone_already_is_fine()
    {
        await StagingFolders.DeleteAsync(folder.Combine("never-made"), clock, logger, Test.Token).WaitAsync(TimeSpan.FromSeconds(10), Test.Token);
    }

    [Fact]
    public async Task The_startup_sweep_removes_marked_folders_named_like_scans_and_nothing_else()
    {
        var marked = Folder(Guid.NewGuid().ToString("D"), marked: true);
        var unmarked = Folder(Guid.NewGuid().ToString("D"), marked: false);
        var markedButNamedOtherwise = Folder("my-documents", marked: true);
        var file = folder.WriteFile(Guid.NewGuid().ToString("D") + ".txt", "a user's file");

        await Sweep(folder.Path).StartAsync(Test.Token);

        Assert.False(Directory.Exists(marked));
        Assert.True(Directory.Exists(unmarked));
        Assert.True(Directory.Exists(markedButNamedOtherwise));
        Assert.True(File.Exists(file));
        Assert.Contains(logger.Collector.GetSnapshot(), record => record.Message == "🧹 Removed 1 staging folder(s) left by an earlier run");
    }

    [Fact]
    public async Task A_missing_root_has_nothing_to_sweep()
    {
        await Sweep(folder.Combine("not-there")).StartAsync(Test.Token);

        Assert.Empty(logger.Collector.GetSnapshot());
    }

    [Fact]
    public async Task A_leftover_that_cannot_be_removed_is_logged_and_the_others_still_go()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to delete a file that is open.");
        var stuck = Folder(Guid.NewGuid().ToString("D"), marked: true);
        var removable = Folder(Guid.NewGuid().ToString("D"), marked: true);
        using (new FileStream(Path.Combine(stuck, StagingFolders.MarkerName), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await clock.DriveAsync(Sweep(folder.Path).StartAsync(Test.Token), TimeSpan.FromMilliseconds(100));
        }

        Assert.True(Directory.Exists(stuck));
        Assert.False(Directory.Exists(removable));
        Assert.Contains(logger.Collector.GetSnapshot(), record => record.Message.StartsWith("⚠️ A staging folder left by an earlier run could not be removed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stopping_the_sweep_does_nothing()
    {
        await Sweep(folder.Path).StopAsync(Test.Token);
    }

    public void Dispose() => folder.Dispose();

    private StagingSweepService Sweep(string root) =>
        new(Microsoft.Extensions.Options.Options.Create(new StagingOptions { Root = root }), clock, logger);

    private string Folder(string name, bool marked)
    {
        var path = folder.Combine(name);
        if (marked)
        {
            StagingFolders.CreateMarked(path);
        }
        else
        {
            Directory.CreateDirectory(path);
        }

        File.WriteAllText(Path.Combine(path, "content.txt"), "x");
        return path;
    }
}
