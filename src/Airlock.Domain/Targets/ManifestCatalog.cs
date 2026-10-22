using System.Collections.Frozen;

namespace Airlock.Domain.Targets;

/// <summary>Recognizes text dependency declarations among uploaded artifacts (BR-12).</summary>
public static class ManifestCatalog
{
    private static readonly FrozenSet<string> ExactNames = new[]
    {
        "Pipfile.lock", "poetry.lock", "uv.lock", "pdm.lock",
        "package.json", "package-lock.json", "npm-shrinkwrap.json", "yarn.lock", "pnpm-lock.yaml", "bun.lock",
        "go.mod", "go.sum", "Cargo.lock", "pom.xml", "gradle.lockfile", "Gemfile.lock", "composer.lock",
        "packages.lock.json", "packages.config", "Package.resolved", "Podfile.lock", "pubspec.lock", "mix.lock",
        "conan.lock", "conanfile.txt", "stack.yaml.lock", "cabal.project.freeze",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // BR-12: the catalogue of names; requirements*.txt, *.gemspec and *.deps.json by pattern.
    public static bool IsManifest(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return ExactNames.Contains(fileName)
            || (fileName.StartsWith("requirements", StringComparison.OrdinalIgnoreCase)
                && fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            || fileName.EndsWith(".gemspec", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase);
    }
}
