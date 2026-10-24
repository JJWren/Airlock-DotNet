using System.Text;
using Airlock.Domain.Intelligence;
using Airlock.Domain.Text;

namespace Airlock.Infrastructure.Nvd;

/// <summary>Turns NVD's records into the Domain's CVEs (IR-09).</summary>
internal static class NvdResponseMapper
{
    private const string Rejected = "Rejected";
    private const string English = "en";
    private const string Primary = "Primary";

    // U+200E and U+200F are the directional marks; U+202A to U+202E the embeddings, overrides and their pop;
    // U+2066 to U+2069 the isolates and their pop. The Domain refuses them, so descriptions lose them.
    private const char LeftToRightMark = (char)0x200E;
    private const char RightToLeftMark = (char)0x200F;
    private const char FirstEmbeddingOrOverride = (char)0x202A;
    private const char LastEmbeddingOrOverride = (char)0x202E;
    private const char FirstIsolate = (char)0x2066;
    private const char LastIsolate = (char)0x2069;

    // IR-09: a record the Domain rejects throws ArgumentException, which fails the whole lookup (decision 3); a record
    // still marked Rejected is dropped (decision 2).
    public static IReadOnlyList<Cve> Map(NvdCveResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var records = new List<Cve>();
        foreach (var vulnerability in response.Vulnerabilities ?? [])
        {
            var cve = vulnerability.Cve ?? throw new ArgumentException("NVD listed a vulnerability without its CVE record.");
            if (string.Equals(cve.VulnStatus, Rejected, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            records.Add(new Cve(cve.Id ?? string.Empty, Description(cve.Descriptions), Scores(cve.Metrics), AffectedProducts(cve.Configurations)));
        }

        return records;
    }

    // IR-09: the first English description, control characters other than line feeds and tabs removed, cut to 16,384.
    internal static string? Description(IReadOnlyList<NvdDescription>? descriptions)
    {
        var text = descriptions?.FirstOrDefault(description => description.Lang == English)?.Value;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var builder = new StringBuilder(Math.Min(text.Length, DomainLimits.CveDescription));
        foreach (var c in text)
        {
            if ((char.IsControl(c) && c is not ('\n' or '\t')) || IsBidirectionalControl(c))
            {
                continue;
            }

            if (builder.Length == DomainLimits.CveDescription)
            {
                if (char.IsHighSurrogate(builder[^1]))
                {
                    builder.Length--;
                }

                break;
            }

            builder.Append(c);
        }

        var cleaned = builder.ToString();
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    // IR-09: one score per metric entry, with its version and its source; the severity NVD gives, else derived (BR-03).
    private static List<CvssScore> Scores(NvdMetrics? metrics)
    {
        var scores = new List<CvssScore>();
        if (metrics is null)
        {
            return scores;
        }

        Add(scores, metrics.CvssMetricV31, CvssVersion.V31);
        Add(scores, metrics.CvssMetricV30, CvssVersion.V30);
        Add(scores, metrics.CvssMetricV40, CvssVersion.V40);
        Add(scores, metrics.CvssMetricV2, CvssVersion.V20);
        return scores;
    }

    private static void Add(List<CvssScore> scores, IReadOnlyList<NvdCvssMetric>? entries, CvssVersion version)
    {
        foreach (var entry in entries ?? [])
        {
            var baseScore = entry.CvssData?.BaseScore ?? throw new ArgumentException("NVD listed a CVSS metric without a base score.");
            var severity = SeverityScale.FromLabel(entry.CvssData.BaseSeverity)
                ?? SeverityScale.FromLabel(entry.BaseSeverity)
                ?? SeverityScale.FromScore(version, baseScore);
            var source = string.Equals(entry.Type, Primary, StringComparison.OrdinalIgnoreCase) ? CvssSource.Primary : CvssSource.Secondary;
            scores.Add(new CvssScore(baseScore, severity, version, source));
        }
    }

    // IR-09: each vulnerable criteria adds its "vendor:product", once.
    private static List<string> AffectedProducts(IReadOnlyList<NvdConfiguration>? configurations)
    {
        var products = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var match in (configurations ?? [])
            .SelectMany(configuration => configuration.Nodes ?? [])
            .SelectMany(node => node.CpeMatch ?? []))
        {
            if (!match.Vulnerable || match.Criteria is null)
            {
                continue;
            }

            var criteria = new CandidateCpe(match.Criteria, CpeSource.Dictionary);
            var product = criteria.Vendor + ":" + criteria.Product;
            if (seen.Add(product))
            {
                products.Add(product);
            }
        }

        return products;
    }

    private static bool IsBidirectionalControl(char c) =>
        c is LeftToRightMark or RightToLeftMark
        || c is (>= FirstEmbeddingOrOverride and <= LastEmbeddingOrOverride) or (>= FirstIsolate and <= LastIsolate);
}
