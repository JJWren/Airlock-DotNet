namespace Airlock.Domain.Targets;

/// <summary>How a package's version is judged and sent to NVD (BR-10).</summary>
public static class PackageVersion
{
    private const string GoModuleType = "go-module";

    // BR-10: empty, whitespace or UNKNOWN (any case) means the version is unknown.
    public static bool IsKnown(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return !string.IsNullOrWhiteSpace(package.Version)
            && !string.Equals(package.Version.Trim(), "UNKNOWN", StringComparison.OrdinalIgnoreCase);
    }

    // BR-10: sent as reported, except that a Go module drops a leading "v" when a digit follows it.
    public static string ForNvd(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var version = package.Version;
        var isGoModule = string.Equals(package.Type, GoModuleType, StringComparison.Ordinal);
        return isGoModule && version.Length > 1 && version[0] == 'v' && char.IsAsciiDigit(version[1])
            ? version[1..]
            : version;
    }
}
