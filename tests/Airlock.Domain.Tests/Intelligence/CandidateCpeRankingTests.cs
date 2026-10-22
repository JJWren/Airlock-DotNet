using Airlock.Domain.Intelligence;

namespace Airlock.Domain.Tests.Intelligence;

// BR-08 — rows of the candidate-ranking table (Syft's candidates from S0, in Syft's order).
public sealed class CandidateCpeRankingTests
{
    private static readonly string[] PyyamlCandidates =
    [
        "python-pyyaml:python-pyyaml", "python-pyyaml:python_pyyaml", "python_pyyaml:python-pyyaml",
        "python_pyyaml:python_pyyaml", "python-pyyaml:pyyaml", "python:python-pyyaml", "python:python_pyyaml",
        "python_pyyaml:pyyaml", "pyyaml:python-pyyaml", "pyyaml:python_pyyaml", "python:pyyaml", "pyyaml:pyyaml",
    ];

    private static readonly string[] FlaskCandidates =
    [
        "python-flask:python-flask", "python-flask:python_flask", "python_flask:python-flask",
        "python_flask:python_flask", "python:python-flask", "python:python_flask", "flask:python-flask",
        "flask:python_flask", "python-flask:flask", "python_flask:flask", "python:flask", "flask:flask",
    ];

    [Fact]
    public void Pyyamls_real_CPE_moves_from_last_to_first()
    {
        var ranked = Rank(A.Package("pyyaml", "5.3"), PyyamlCandidates);

        Assert.Equal(["pyyaml:pyyaml", "python-pyyaml:pyyaml", "python_pyyaml:pyyaml"], ranked);
    }

    [Fact]
    public void Log4j_core_keeps_all_three_including_NVDs_apache_log4j()
    {
        var ranked = Rank(
            A.Package("log4j-core", "2.14.1", "java-archive"),
            ["apache:log4j-core", "apache:log4j_core", "apache:log4j"]);

        Assert.Equal(["apache:log4j-core", "apache:log4j_core", "apache:log4j"], ranked);
    }

    [Fact]
    public void Flask_tries_the_three_name_matching_guesses()
    {
        var ranked = Rank(A.Package("flask", "0.12.2"), FlaskCandidates);

        Assert.Equal(["flask:flask", "python-flask:flask", "python_flask:flask"], ranked);
    }

    [Fact]
    public void Dictionary_candidates_come_first()
    {
        var ranked = CandidateCpe.Prioritize(
            A.Package("requests", "2.25.1"),
            [A.GeneratedCpe("requests", "requests"), A.DictionaryCpe("python", "requests")]);

        Assert.Equal(CpeSource.Dictionary, ranked[0].Source);
        Assert.Equal("python", ranked[0].Vendor);
    }

    [Fact]
    public void Exact_duplicates_are_dropped_and_Syfts_order_is_kept_within_a_tier()
    {
        var ranked = Rank(
            A.Package("widget", "1.0"),
            ["alpha:other", "alpha:other", "beta:other", "gamma:other"],
            max: 5);

        Assert.Equal(["alpha:other", "beta:other", "gamma:other"], ranked);
    }

    [Fact]
    public void The_cap_can_be_changed_but_must_be_positive()
    {
        Assert.Single(Rank(A.Package("pyyaml", "5.3"), PyyamlCandidates, max: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rank(A.Package(), PyyamlCandidates, max: 0));
    }

    [Fact]
    public void Null_input_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => CandidateCpe.Prioritize(null!, []));
        Assert.Throws<ArgumentNullException>(() => CandidateCpe.Prioritize(A.Package(), null!));
        Assert.Throws<ArgumentNullException>(() => CandidateCpe.Prioritize(A.Package(), [null!]));
    }

    [Fact]
    public void Vendor_and_product_are_read_with_escapes_removed()
    {
        var cpe = new CandidateCpe(@"cpe:2.3:a:go:auth\/oauth2adapt:v0.2.8:*:*:*:*:*:*:*", CpeSource.Generated);

        Assert.Equal("go", cpe.Vendor);
        Assert.Equal("auth/oauth2adapt", cpe.Product);
    }

    [Fact]
    public void A_CPE_with_too_few_components_has_no_vendor_or_product()
    {
        var cpe = new CandidateCpe("cpe:2.3:a", CpeSource.Generated);

        Assert.Equal(string.Empty, cpe.Vendor);
        Assert.Equal(string.Empty, cpe.Product);
    }

    [Fact]
    public void Only_CPE_2_3_strings_are_accepted()
    {
        Assert.Throws<ArgumentException>(() => new CandidateCpe("cpe:/a:python:requests:2.25.1", CpeSource.Generated));
        Assert.Throws<ArgumentException>(() => new CandidateCpe("", CpeSource.Generated));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CandidateCpe("cpe:2.3:a:x:y:1", (CpeSource)42));
    }

    private static string[] Rank(Domain.Targets.Package package, string[] vendorProducts, int max = 3) =>
    [
        .. CandidateCpe
            .Prioritize(package, vendorProducts.Select(pair => A.GeneratedCpe(pair.Split(':')[0], pair.Split(':')[1])), max)
            .Select(cpe => $"{cpe.Vendor}:{cpe.Product}"),
    ];
}
