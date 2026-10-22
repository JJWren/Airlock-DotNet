using System.Collections.Frozen;
using Airlock.Domain.Targets;

namespace Airlock.Domain.Intelligence;

/// <summary>Product names used to compare candidate CPEs and to ask NVD for a product match.</summary>
public static class ProductName
{
    private const string GoModuleType = "go-module";
    private const string NpmType = "npm";
    private const int MinimumLength = 3;

    // BR-09: names too generic to identify a product on their own.
    private static readonly FrozenSet<string> GenericNames = new[]
    {
        "api", "app", "auth", "base", "cli", "client", "cmd", "common", "config", "core", "data", "http", "https",
        "internal", "io", "json", "lib", "libs", "log", "logging", "main", "model", "models", "net", "os", "pkg",
        "plugin", "plugins", "sdk", "server", "stdlib", "test", "tests", "tool", "tools", "types", "util", "utils",
        "web", "xml",
    }.ToFrozenSet(StringComparer.Ordinal);

    public static string Normalize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.ToLowerInvariant();
    }

    // BR-09: the product to ask NVD about, or null when the name cannot identify a product.
    public static string? ForProductMatch(Package package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var name = package.Name;
        if (string.Equals(package.Type, GoModuleType, StringComparison.Ordinal))
        {
            name = name[(name.LastIndexOf('/') + 1)..];
        }
        else if (string.Equals(package.Type, NpmType, StringComparison.Ordinal) && name.StartsWith('@'))
        {
            name = name[(name.IndexOf('/') + 1)..];
        }

        var product = Normalize(name);
        return product.Length < MinimumLength || product.Any(char.IsWhiteSpace) || GenericNames.Contains(product)
            ? null
            : product;
    }

    // BR-08: names are compared in lower case, with '-', '_' and '.' treated alike.
    internal static string ComparisonKey(string name) => Normalize(name).Replace('_', '-').Replace('.', '-');
}
