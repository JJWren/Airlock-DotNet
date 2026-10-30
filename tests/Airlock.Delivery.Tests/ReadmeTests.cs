using Airlock.Delivery.Tests.Support;

namespace Airlock.Delivery.Tests;

// DR-17, DR-18, U6-NFR-16, U6-NFR-18 — the README's licensing lines, the notice NVD's terms ask for, an explanation of
// every warning a clean start may log, and links to this repository.
public sealed class ReadmeTests
{
    private static readonly string Readme = Repository.Read("README.md");

    [Fact]
    public void It_states_the_licences_and_QuestPDFs_revenue_threshold()
    {
        Assert.Contains("MIT License", Readme, StringComparison.Ordinal);
        Assert.Contains("QuestPDF Community License", Readme, StringComparison.Ordinal);
        Assert.Contains("USD 1,000,000", Readme, StringComparison.Ordinal);
        Assert.Contains("SIL Open Font License 1.1", Readme, StringComparison.Ordinal);
        Assert.Contains("Apache License 2.0", Readme, StringComparison.Ordinal);
        Assert.Contains("THIRD-PARTY-NOTICES.md", Readme, StringComparison.Ordinal);
    }

    [Fact]
    public void It_carries_the_notice_NVDs_terms_of_use_ask_for()
    {
        Assert.Contains("This product uses the NVD API but is not endorsed or certified by the NVD.", Readme, StringComparison.Ordinal);
    }

    [Fact]
    public void It_explains_every_warning_a_clean_start_may_log()
    {
        var expected = Repository.Lines("tests/smoke/expected-warnings.txt").Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')).ToList();

        Assert.NotEmpty(expected);
        Assert.All(expected, warning => Assert.Contains($"`{warning}`", Readme, StringComparison.Ordinal));
    }

    [Fact]
    public void Its_links_to_GitHub_point_at_this_repository_or_the_original()
    {
        var links = System.Text.RegularExpressions.Regex.Matches(Readme, @"https://github\.com/[\w.-]+/[\w.-]+").Select(link => link.Value).Distinct().ToList();

        Assert.Contains("https://github.com/JJWren/Airlock-DotNet", links);
        Assert.All(links, link => Assert.Contains(link, (string[])
        [
            "https://github.com/JJWren/Airlock-DotNet", "https://github.com/JJWren/Airlock", "https://github.com/anchore/syft",
            "https://github.com/anchore/grype", "https://github.com/rsms/inter", "https://github.com/JetBrains/JetBrainsMono",
        ]));
    }
}
