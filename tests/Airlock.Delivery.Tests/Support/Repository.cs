namespace Airlock.Delivery.Tests.Support;

/// <summary>The repository's own files, found by walking up from the tests to <c>Airlock.slnx</c>. Nothing is written.</summary>
internal static class Repository
{
    public static readonly string Root = FindRoot();

    public static readonly string[] Workflows = [".github/workflows/ci.yml", ".github/workflows/codeql.yml", ".github/workflows/release.yml"];

    public static string PathOf(string relativePath) => Path.Combine(Root, relativePath);

    /// <summary>A file's text with its line endings made <c>\n</c>, as a Windows checkout may give them as <c>\r\n</c>.</summary>
    public static string Read(string relativePath) => File.ReadAllText(PathOf(relativePath)).ReplaceLineEndings("\n");

    public static string[] Lines(string relativePath) => Read(relativePath).Split('\n');

    /// <summary>Every tracked-looking file under a folder, as a repository-relative path with forward slashes.</summary>
    public static IEnumerable<string> FilesUnder(string relativeFolder, string pattern) =>
        Directory.EnumerateFiles(PathOf(relativeFolder), pattern, SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Root, path).Replace('\\', '/'))
            .Where(path => !path.Contains("/bin/", StringComparison.Ordinal) && !path.Contains("/obj/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    private static string FindRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Airlock.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException("No Airlock.slnx above " + AppContext.BaseDirectory);
    }
}
