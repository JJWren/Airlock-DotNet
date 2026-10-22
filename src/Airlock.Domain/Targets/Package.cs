using Airlock.Domain.Text;

namespace Airlock.Domain.Targets;

/// <summary>One third-party component listed in the SBOM, identified by its type, name and version.</summary>
public sealed record Package
{
    public Package(string type, string name, string version)
    {
        Type = DomainText.Require(type, DomainLimits.PackageType, nameof(type));
        Name = DomainText.Require(name, DomainLimits.PackageName, nameof(name));
        Version = DomainText.AllowEmpty(version, DomainLimits.PackageVersion, nameof(version));
    }

    /// <summary>Syft's package type, such as <c>python</c>, <c>npm</c>, <c>java-archive</c> or <c>go-module</c>.</summary>
    public string Type { get; }

    public string Name { get; }

    /// <summary>The version as Syft reports it; it may be empty or <c>UNKNOWN</c> (BR-10).</summary>
    public string Version { get; }

    // BR-11: the type matches exactly, the name ignoring case, the version exactly.
    public bool Equals(Package? other) =>
        other is not null
        && string.Equals(Type, other.Type, StringComparison.Ordinal)
        && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Version, other.Version, StringComparison.Ordinal);

    public override int GetHashCode() =>
        HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(Type),
            StringComparer.OrdinalIgnoreCase.GetHashCode(Name),
            StringComparer.Ordinal.GetHashCode(Version));

    public override string ToString() => $"{Name} {Version}";
}
