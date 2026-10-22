using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Airlock.Domain.Text;

namespace Airlock.Domain.Intelligence;

/// <summary>A published vulnerability record from NVD.</summary>
public sealed partial record Cve
{
    public const string NoDescription = "No description available.";

    public Cve(string id, string? description, IEnumerable<CvssScore> scores, IEnumerable<string> affectedProducts)
    {
        Id = DomainText.Require(id, DomainLimits.CveId, nameof(id));
        if (!IdPattern().IsMatch(Id))
        {
            throw new ArgumentException("Must look like CVE-YYYY-NNNN.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(scores);
        ArgumentNullException.ThrowIfNull(affectedProducts);

        Description = string.IsNullOrWhiteSpace(description)
            ? NoDescription
            : DomainText.Require(description, DomainLimits.CveDescription, nameof(description), allowLineBreaks: true);
        Scores = scores
            .Select(score => score ?? throw new ArgumentException("Must not contain null.", nameof(scores)))
            .ToImmutableArray();
        AffectedProducts = affectedProducts
            .Select(product => DomainText.Require(product, DomainLimits.Cpe, nameof(affectedProducts)))
            .ToImmutableArray();
        NvdUrl = new Uri("https://nvd.nist.gov/vuln/detail/" + Id); // BR-17
        PreferredScore = CvssScore.SelectPreferred(Scores);
    }

    public string Id { get; }

    public string Description { get; }

    public Uri NvdUrl { get; }

    /// <summary>Every score NVD published for this CVE; may be empty.</summary>
    public IReadOnlyList<CvssScore> Scores { get; }

    /// <summary>The <c>vendor:product</c> pairs named in the CVE's configurations.</summary>
    public IReadOnlyList<string> AffectedProducts { get; }

    /// <summary>The score Airlock uses (BR-01, BR-02); null when NVD has not scored the CVE.</summary>
    public CvssScore? PreferredScore { get; }

    [GeneratedRegex("^CVE-[0-9]{4}-[0-9]{4,}$")]
    private static partial Regex IdPattern();
}
