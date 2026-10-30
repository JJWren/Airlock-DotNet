using System.Globalization;
using System.Text.RegularExpressions;
using Airlock.Delivery.Tests.Support;

namespace Airlock.Delivery.Tests;

// DR-30, U6-NFR-06 — Grype's gate, and acceptances that expire: each names its vulnerability and package, and gives a
// reason that begins with a review date still to come (UTC), so a passed date fails CI until someone reviews it.
public sealed partial class GrypePolicyTests
{
    [Fact]
    public void A_High_or_Critical_vulnerability_with_a_fix_fails_the_scan()
    {
        var policy = Yaml.Load(".grype.yaml");

        Assert.Equal("true", policy.Text("only-fixed"));
        Assert.Equal("high", policy.Text("fail-on-severity"));
        Assert.NotNull(policy.Find("ignore"));
    }

    [Fact]
    public void Each_acceptance_names_its_vulnerability_and_package_and_is_still_under_review()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.All(Yaml.Load(".grype.yaml").Items("ignore"), acceptance =>
        {
            Assert.Matches(@"^(CVE-\d{4}-\d+|GO-\d{4}-\d+|GHSA(-[23456789cfghjmpqrvwx]{4}){3})$", acceptance.Text("vulnerability") ?? string.Empty);
            Assert.False(string.IsNullOrWhiteSpace(acceptance.Text("package", "name")), "an acceptance names its package");
            var review = ReviewBy().Match(acceptance.Text("reason") ?? string.Empty);
            Assert.True(review.Success, $"the reason of {acceptance.Text("vulnerability")} begins with review-by YYYY-MM-DD:");
            var date = DateOnly.ParseExact(review.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            Assert.True(date >= today, $"{acceptance.Text("vulnerability")} was to be reviewed by {date:yyyy-MM-dd}");
        });
    }

    [GeneratedRegex(@"^review-by (\d{4}-\d{2}-\d{2}): \S")]
    private static partial Regex ReviewBy();
}
