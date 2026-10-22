using System.Globalization;
using Airlock.Application.Correlation;
using Airlock.Application.Ports;
using Airlock.Application.Tests.Fakes;
using Airlock.Domain.Intelligence;
using Airlock.Domain.Results;
using Airlock.Domain.Targets;
using Microsoft.Extensions.Logging.Abstractions;

namespace Airlock.Application.Tests.Quality;

// U2-NFR-06: every verdict matches an independent copy of the decision tables (AR-13 to AR-16).
public sealed class CorrelationOracleTests
{
    private static readonly string[] Names = ["pyyaml", "flask", "minimist", "requests", "auth", "core", "io", "jinja2", "lodash"];
    private static readonly string[] Versions = ["1.0", "2.25.1", "v0.23.3", "UNKNOWN", ""];
    private static readonly string[] Types = ["python", "npm", "go-module"];
    private static readonly string[] Vendors = ["acme", "python", "vendor"];

    public static TheoryData<int> Seeds => [.. Enumerable.Range(1, 25)];

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task Every_verdict_matches_the_decision_tables(int seed)
    {
        var random = new Random(seed);
        for (var i = 0; i < 40; i++)
        {
            var catalog = new FakeVulnerabilityCatalog();
            var answers = new Dictionary<string, Answer>(StringComparer.Ordinal);
            var package = RandomPackage(random);
            var candidates = Enumerable.Range(0, random.Next(4))
                .Select(_ => A.Cpe(Vendors[random.Next(Vendors.Length)] + ":" + package.Name.Split('/')[^1], "1.0", (CpeSource)random.Next(2)))
                .ToList();
            var cveNumber = 0;
            foreach (var key in candidates.Select(cpe => cpe.Value).Append(ProductKey(package)))
            {
                answers.TryAdd(key, RandomAnswer(random, ref cveNumber, seed, i));
            }

            foreach (var (key, answer) in answers)
            {
                Configure(catalog, key, answer);
            }

            var correlator = new PackageCorrelator(catalog, NullLogger<PackageCorrelator>.Instance);
            var actual = await correlator.CorrelateAsync(
                new SbomPackage(package, candidates), new NvdOutageBreaker(1_000), TestContext.Current.CancellationToken);

            Assert.Equal(Oracle(package, candidates, answers), Describe(actual));
        }
    }

    // The decision tables, written again without the production code.
    private static string Oracle(Package package, IReadOnlyList<CandidateCpe> candidates, Dictionary<string, Answer> answers)
    {
        if (string.IsNullOrWhiteSpace(package.Version) || package.Version.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase))
        {
            return "unverified VersionUnknown version unknown";
        }

        foreach (var cpe in CandidateCpe.Prioritize(package, candidates))
        {
            var answer = answers.GetValueOrDefault(cpe.Value, Answer.None);
            if (answer.Failed is { } reason)
            {
                return "unverified LookupFailed " + reason;
            }

            if (answer.Records.Count > 0)
            {
                return FromMatch("cpe " + cpe.Value, answer.Records);
            }
        }

        var product = ProductName.ForProductMatch(package);
        if (product is null)
        {
            return "clean none []";
        }

        var productAnswer = answers.GetValueOrDefault(ProductKey(package), Answer.None);
        if (productAnswer.Failed is { } productReason)
        {
            return "unverified LookupFailed " + productReason;
        }

        return FromMatch("product " + product, productAnswer.Records);
    }

    private static string FromMatch(string method, IReadOnlyList<Cve> records)
    {
        var findings = records
            .Where(cve => cve.PreferredScore is { } score && score.Severity >= Severity.High)
            .OrderByDescending(cve => cve.PreferredScore!.Severity)
            .ThenByDescending(cve => cve.PreferredScore!.BaseScore)
            .ThenBy(cve => cve.Id, StringComparer.Ordinal)
            .Select(cve => cve.Id)
            .ToList();
        var unscored = records.Where(cve => cve.PreferredScore is null).Select(cve => cve.Id).Distinct().ToList();
        return findings.Count > 0
            ? $"vulnerable {method} [{string.Join(",", findings)}] [{string.Join(",", unscored)}]"
            : $"clean {method} [{string.Join(",", unscored)}]";
    }

    private static string Describe(PackageCorrelation correlation) => correlation switch
    {
        UnverifiedCorrelation unverified => $"unverified {unverified.Result.Reason} {unverified.Result.Detail}",
        VulnerableCorrelation vulnerable =>
            $"vulnerable {Method(vulnerable.Result.Method)} [{string.Join(",", vulnerable.Result.Findings.Select(f => f.Cve.Id))}] [{string.Join(",", vulnerable.UnscoredCveIds)}]",
        CleanCorrelation { Method: null } => "clean none []",
        CleanCorrelation clean => $"clean {Method(clean.Method)} [{string.Join(",", clean.UnscoredCveIds)}]",
        _ => throw new ArgumentOutOfRangeException(nameof(correlation)),
    };

    private static string Method(MatchMethod? method) => method switch
    {
        CpeMatch cpe => "cpe " + cpe.Cpe.Value,
        ProductMatch product => "product " + product.Product,
        _ => "none",
    };

    private static Package RandomPackage(Random random)
    {
        var type = Types[random.Next(Types.Length)];
        var name = Names[random.Next(Names.Length)];
        return new Package(type, type == "go-module" ? "example.com/" + name : name, Versions[random.Next(Versions.Length)]);
    }

    private static Answer RandomAnswer(Random random, ref int cveNumber, int seed, int item)
    {
        switch (random.Next(4))
        {
            case 0:
                return Answer.None;
            case 1:
                return new Answer([], "NVD returned 503");
            default:
                List<Cve> records = [];
                for (var n = random.Next(1, 4); n > 0; n--)
                {
                    var id = string.Create(CultureInfo.InvariantCulture, $"CVE-2099-{seed:D2}{item:D2}{++cveNumber:D2}");
                    records.Add(random.Next(5) switch
                    {
                        0 => A.UnscoredCve(id),
                        1 => A.Cve(id, 5.0m, Severity.Medium),
                        2 => A.Cve(id, 7.5m, Severity.High),
                        3 => A.Cve(id, 9.8m, Severity.Critical),
                        _ => A.Cve(id, 2.0m, Severity.Low),
                    });
                }

                return new Answer(records, null);
        }
    }

    private static void Configure(FakeVulnerabilityCatalog catalog, string key, Answer answer)
    {
        CatalogLookup lookup = answer.Failed is { } reason ? new CatalogFailed(reason)
            : answer.Records.Count > 0 ? new CatalogFound(answer.Records)
            : new CatalogNoRecords();
        var previous = catalog.Fallback;
        catalog.Fallback = (asked, token) => asked == key ? Task.FromResult(lookup) : previous?.Invoke(asked, token) ?? Task.FromResult<CatalogLookup>(new CatalogNoRecords());
    }

    private static string ProductKey(Package package) =>
        FakeVulnerabilityCatalog.ProductKey(ProductName.ForProductMatch(package) ?? "-", package.Version);

    private sealed record Answer(IReadOnlyList<Cve> Records, string? Failed)
    {
        public static Answer None { get; } = new([], null);
    }
}
