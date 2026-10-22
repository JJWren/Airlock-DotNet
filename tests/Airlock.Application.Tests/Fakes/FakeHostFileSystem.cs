using Airlock.Application.Ports;

namespace Airlock.Application.Tests.Fakes;

public enum HostMode
{
    Linux,
    Windows,
}

/// <summary>
/// A host file system in memory with either Linux rules (<c>/</c>, case-sensitive) or Windows rules (drive letters,
/// backslashes, case-insensitive), so the target rules can be tested both ways on any operating system (U2-NFR-12).
/// </summary>
internal sealed class FakeHostFileSystem(HostMode mode) : IHostFileSystem
{
    private const char Backslash = (char)92;

    private readonly HashSet<string> directories = new(Comparer(mode));
    private readonly HashSet<string> files = new(Comparer(mode));
    private readonly Dictionary<string, string> links = new(Comparer(mode));

    public HostMode Mode => mode;

    private char Separator => mode == HostMode.Windows ? Backslash : '/';

    private StringComparison Comparison => mode == HostMode.Windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public FakeHostFileSystem WithDirectory(string path)
    {
        directories.Add(GetFullPath(path));
        return this;
    }

    public FakeHostFileSystem WithFile(string path)
    {
        files.Add(GetFullPath(path));
        return this;
    }

    /// <summary>A directory that is a symbolic link to <paramref name="target"/>.</summary>
    public FakeHostFileSystem WithLink(string path, string target)
    {
        links[GetFullPath(path)] = GetFullPath(target);
        directories.Add(GetFullPath(path));
        return this;
    }

    public bool IsAbsolute(string path) =>
        mode == HostMode.Linux
            ? path.StartsWith('/')
            : path.StartsWith('/') || path.StartsWith(Backslash) || (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':');

    public bool DirectoryExists(string path) => directories.Contains(GetFullPath(path));

    public bool FileExists(string path) => files.Contains(GetFullPath(path));

    public string GetFullPath(string path)
    {
        var unified = mode == HostMode.Windows ? path.Replace('/', Backslash) : path;
        string root;
        string rest;
        if (mode == HostMode.Linux)
        {
            root = "/";
            rest = unified;
        }
        else if (unified.Length >= 2 && unified[1] == ':')
        {
            root = char.ToUpperInvariant(unified[0]) + ":" + Backslash;
            rest = unified[2..];
        }
        else
        {
            root = "C:" + Backslash;
            rest = unified;
        }

        List<string> segments = [];
        foreach (var segment in rest.Split(Separator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }
            }
            else if (segment != ".")
            {
                segments.Add(segment);
            }
        }

        return root + string.Join(Separator, segments);
    }

    public string ResolveLinks(string path)
    {
        var full = GetFullPath(path);
        for (var hop = 0; hop < 32; hop++)
        {
            var link = links.Keys.FirstOrDefault(candidate => IsWithin(full, candidate));
            if (link is null)
            {
                return full;
            }

            full = GetFullPath(links[link] + Separator + full[link.Length..]);
        }

        throw new IOException("Too many levels of symbolic links.");
    }

    public bool IsWithin(string path, string root)
    {
        var fullPath = GetFullPath(path);
        var fullRoot = GetFullPath(root).TrimEnd(Separator);
        return fullPath.Equals(fullRoot, Comparison) || fullPath.StartsWith(fullRoot + Separator, Comparison);
    }

    private static StringComparer Comparer(HostMode mode) =>
        mode == HostMode.Windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
