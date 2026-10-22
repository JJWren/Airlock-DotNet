using Airlock.Domain.Intelligence;
using Airlock.Domain.Results;

namespace Airlock.Domain.Tests.Results;

// BR-06, BR-07
public sealed class VulnerablePackageTests
{
    [Fact]
    public void Risk_is_the_highest_severity_whatever_order_the_findings_arrive_in()
    {
        var package = A.Vulnerable(
            A.Package(),
            A.Finding("CVE-2020-14343", 7.5m, Severity.High),
            A.Finding("CVE-2020-1747", 9.8m, Severity.Critical));

        Assert.Equal(Severity.Critical, package.HighestSeverity);
        Assert.Equal(["CVE-2020-1747", "CVE-2020-14343"], package.Findings.Select(finding => finding.Cve.Id));
    }

    [Fact]
    public void Repeated_findings_are_kept_once()
    {
        var finding = A.Finding("CVE-2020-1747", 9.8m, Severity.Critical);

        Assert.Single(A.Vulnerable(A.Package(), finding, finding).Findings);
    }

    [Fact]
    public void A_vulnerable_package_needs_at_least_one_finding()
    {
        Assert.Throws<ArgumentException>(() => A.Vulnerable(A.Package()));
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        var finding = A.Finding("CVE-2020-1747", 9.8m, Severity.Critical);
        var method = new ProductMatch("pyyaml");

        Assert.Throws<ArgumentNullException>(() => new VulnerablePackage(null!, method, [finding]));
        Assert.Throws<ArgumentNullException>(() => new VulnerablePackage(A.Package(), null!, [finding]));
        Assert.Throws<ArgumentNullException>(() => new VulnerablePackage(A.Package(), method, null!));
        Assert.Throws<ArgumentException>(() => new VulnerablePackage(A.Package(), method, [null!]));
    }
}
