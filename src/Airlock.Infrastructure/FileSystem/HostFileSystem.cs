using Airlock.Application.Ports;

namespace Airlock.Infrastructure.FileSystem;

/// <summary>Answers the Application's path questions with the host's own rules (IR-19).</summary>
public sealed class HostFileSystem : IHostFileSystem
{
    private const int MaxLinks = 40;

    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    // IR-19: ignoring case on Windows and macOS, exactly on Linux.
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    // IR-19: rooted on the host, so "/uploads" counts on Windows too.
    public bool IsAbsolute(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Path.IsPathRooted(path);
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public string GetFullPath(string path) => Path.GetFullPath(path);

    // IR-19: the path walked segment by segment, each symbolic link replaced by its target, giving up after 40 links.
    public string ResolveLinks(string path)
    {
        var full = Path.GetFullPath(path);
        var links = 0;
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var current = root;
        var pending = new Queue<string>(full[root.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries));
        while (pending.TryDequeue(out var segment))
        {
            var next = Path.Combine(current, segment);
            FileSystemInfo entry = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
            if (entry.LinkTarget is not { } target)
            {
                current = next;
                continue;
            }

            if (++links > MaxLinks)
            {
                throw new IOException($"More than {MaxLinks} symbolic links on the way to the path.");
            }

            // Start again from the target, followed by the segments not yet walked.
            var resolved = Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(current, target));
            root = Path.GetPathRoot(resolved) ?? string.Empty;
            current = root;
            pending = new Queue<string>(
                resolved[root.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries).Concat(pending));
        }

        return current;
    }

    // IR-19: segment by segment, so "/uploads-old" is not inside "/uploads".
    public bool IsWithin(string path, string root)
    {
        var fullPath = Trimmed(Path.GetFullPath(path));
        var fullRoot = Trimmed(Path.GetFullPath(root));
        if (!fullPath.StartsWith(fullRoot, PathComparison))
        {
            return false;
        }

        return fullPath.Length == fullRoot.Length
            || IsSeparator(fullRoot[^1])
            || IsSeparator(fullPath[fullRoot.Length]);
    }

    private static string Trimmed(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        return fullPath.Length > root.Length ? fullPath.TrimEnd(Separators) : fullPath;
    }

    private static bool IsSeparator(char c) => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
}
