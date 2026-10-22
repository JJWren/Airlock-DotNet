using Airlock.Domain.Intelligence;
using Airlock.Domain.Text;

namespace Airlock.Domain.Results;

/// <summary>How a package was linked to NVD records (GLOSSARY: Match Method).</summary>
public abstract record MatchMethod
{
    private protected MatchMethod()
    {
    }
}

/// <summary>Linked through one of the package's candidate CPEs.</summary>
public sealed record CpeMatch : MatchMethod
{
    public CpeMatch(CandidateCpe cpe)
    {
        ArgumentNullException.ThrowIfNull(cpe);
        Cpe = cpe;
    }

    public CandidateCpe Cpe { get; }
}

/// <summary>Linked by product name from any vendor (ADR-0006); <see cref="Product"/> is the name that was matched.</summary>
public sealed record ProductMatch : MatchMethod
{
    public ProductMatch(string product) => Product = DomainText.Require(product, DomainLimits.PackageName, nameof(product));

    public string Product { get; }
}
