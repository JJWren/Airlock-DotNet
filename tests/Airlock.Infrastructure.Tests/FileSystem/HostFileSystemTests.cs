using Airlock.Infrastructure.FileSystem;
using Airlock.Infrastructure.Tests.Support;

namespace Airlock.Infrastructure.Tests.FileSystem;

// IR-19 — the host's own rules, on real folders and links of the machine running the tests.
public sealed class HostFileSystemTests : IDisposable
{
    private readonly HostFileSystem fileSystem = new();
    private readonly TemporaryFolder folder = new();

    [Theory]
    [InlineData("/uploads", true)]
    [InlineData("uploads", false)]
    [InlineData("./uploads", false)]
    [InlineData("../uploads", false)]
    public void A_path_is_absolute_when_it_is_rooted_on_the_host(string path, bool absolute)
    {
        Assert.Equal(absolute, fileSystem.IsAbsolute(path));
    }

    [Fact]
    public void A_drive_path_is_absolute_on_Windows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Drive letters exist only on Windows.");

        Assert.True(fileSystem.IsAbsolute(@"C:\uploads"));
    }

    [Fact]
    public void Full_paths_resolve_dot_segments_and_the_existence_checks_ask_the_disk()
    {
        var file = folder.WriteFile(Path.Combine("b", "c.txt"), "x");

        Assert.Equal(file, fileSystem.GetFullPath(Path.Combine(folder.Path, "a", "..", "b", ".", "c.txt")));
        Assert.True(fileSystem.FileExists(file));
        Assert.False(fileSystem.FileExists(folder.Combine("nothing.txt")));
        Assert.True(fileSystem.DirectoryExists(folder.Combine("b")));
        Assert.False(fileSystem.DirectoryExists(file));
    }

    [Fact]
    public void A_path_lies_within_a_root_segment_by_segment()
    {
        var root = folder.Combine("uploads");

        Assert.True(fileSystem.IsWithin(root, root));
        Assert.True(fileSystem.IsWithin(Path.Combine(root, "acme-app"), root));
        Assert.True(fileSystem.IsWithin(Path.Combine(root, "a", "b"), root + Path.DirectorySeparatorChar));
        Assert.True(fileSystem.IsWithin(Path.Combine(root, "a", "..", "b"), root));
        Assert.False(fileSystem.IsWithin(root + "-old", root));
        Assert.False(fileSystem.IsWithin(folder.Path, root));
        Assert.False(fileSystem.IsWithin(Path.Combine(root, "..", "elsewhere"), root));
    }

    [Fact]
    public void A_filesystem_root_holds_every_path_on_it()
    {
        var root = Path.GetPathRoot(folder.Path)!;

        Assert.True(fileSystem.IsWithin(folder.Path, root));
    }

    [Fact]
    public void Case_is_ignored_on_Windows_and_macOS_and_kept_on_Linux()
    {
        var root = folder.Combine("Uploads");
        var other = folder.Combine("uploads", "x");

        Assert.Equal(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(), fileSystem.IsWithin(other, root));
    }

    [Fact]
    public void A_path_without_links_resolves_to_its_full_path()
    {
        var path = folder.Combine("plain", "deeper");
        Directory.CreateDirectory(path);

        Assert.Equal(path, fileSystem.ResolveLinks(Path.Combine(folder.Path, "plain", ".", "deeper")));
        Assert.Equal(folder.Combine("missing", "part"), fileSystem.ResolveLinks(folder.Combine("missing", "part")));
    }

    [Fact]
    public void Each_symbolic_link_on_the_way_is_replaced_by_its_target()
    {
        var target = folder.Combine("target");
        Directory.CreateDirectory(Path.Combine(target, "sub"));
        var link = folder.Combine("link");
        CreateLinkOrSkip(link, target);

        Assert.Equal(Path.Combine(target, "sub"), fileSystem.ResolveLinks(Path.Combine(link, "sub")));
    }

    [Fact]
    public void A_relative_link_is_resolved_from_its_own_folder()
    {
        var target = folder.Combine("target");
        Directory.CreateDirectory(target);
        var link = folder.Combine("relative-link");
        CreateLinkOrSkip(link, "target");

        Assert.Equal(target, fileSystem.ResolveLinks(link));
    }

    [Fact]
    public void A_loop_of_links_gives_up()
    {
        var first = folder.Combine("first");
        var second = folder.Combine("second");
        CreateLinkOrSkip(first, second);
        CreateLinkOrSkip(second, first);

        Assert.Throws<IOException>(() => fileSystem.ResolveLinks(Path.Combine(first, "x")));
    }

    public void Dispose() => folder.Dispose();

    // Without the right to create symbolic links, Windows still lets anyone create a directory junction, which .NET
    // reports as a link too; junctions need full targets.
    private static void CreateLinkOrSkip(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(target))
            {
                Assert.Skip("This account may not create symbolic links (on Windows, enable Developer Mode).");
            }
        }

        using var junction = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            ArgumentList = { "/c", "mklink", "/J", link, target },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        })!;
        junction.WaitForExit();
        Assert.SkipWhen(junction.ExitCode != 0, "This account may create neither symbolic links nor junctions.");
    }
}
