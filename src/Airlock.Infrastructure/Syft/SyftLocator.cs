namespace Airlock.Infrastructure.Syft;

/// <summary>Where the Syft binary is (IR-06).</summary>
internal sealed class SyftLocator(string? configuredPath, string baseDirectory, string? searchPath, Func<string, bool> fileExists)
{
    public SyftLocator(string? configuredPath)
        : this(configuredPath, AppContext.BaseDirectory, Environment.GetEnvironmentVariable("PATH"), File.Exists)
    {
    }

    /// <summary>Syft's file name on this system.</summary>
    public static string FileName => OperatingSystem.IsWindows() ? "syft.exe" : "syft";

    // IR-06: the configured path, relative paths taken from the application's folder; without one, syft on the path, then
    // bin/syft next to the application.
    public SyftLocation Locate()
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var configured = Path.GetFullPath(configuredPath, baseDirectory);
            if (Directory.Exists(configured))
            {
                configured = Path.Combine(configured, FileName);
            }

            return new SyftLocation(configured, fileExists(configured));
        }

        foreach (var entry in (searchPath ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            // Only full folders: a relative entry such as "." would run whatever binary the working directory holds.
            var folder = entry.Trim().Trim('"');
            if (Path.IsPathFullyQualified(folder) && Path.Combine(folder, FileName) is var candidate && fileExists(candidate))
            {
                return new SyftLocation(candidate, true);
            }
        }

        var bundled = Path.Combine(baseDirectory, "bin", FileName);
        return new SyftLocation(bundled, fileExists(bundled));
    }
}

/// <summary>Where Syft was looked for, and whether it is there.</summary>
internal sealed record SyftLocation(string Path, bool Exists);
