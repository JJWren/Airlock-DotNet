using Airlock.Application.Ports;
using Airlock.Domain.Intelligence;
using Airlock.Domain.Targets;

namespace Airlock.Web.Tests.Support;

/// <summary>Builders for the values tests feed the fakes.</summary>
internal static class A
{
    /// <summary>The S0 requirements.txt: flask 0.12.2, jinja2 2.10, pyyaml 5.3, requests 2.25.1 (56 bytes).</summary>
    public const string RequirementsText = "flask==0.12.2\njinja2==2.10\npyyaml==5.3\nrequests==2.25.1\n";

    public static Package Package(string name, string version, string type = "python") => new(type, name, version);

    public static string CpeFor(string name, string version) => $"cpe:2.3:a:{name}:{name}:{version}:*:*:*:*:*:*:*";

    /// <summary>A package as Syft reports it, with one generated candidate CPE.</summary>
    public static SbomPackage Sbom(string name, string version, string type = "python") =>
        new(Package(name, version, type), [new CandidateCpe(CpeFor(name, version), CpeSource.Generated)]);

    public static Cve Cve(string id, decimal score = 9.8m, Severity severity = Severity.Critical, string description = "A test vulnerability.") =>
        new(id, description, [new CvssScore(score, severity, CvssVersion.V31, CvssSource.Primary)], []);

    public static byte[] Bytes(string text) => System.Text.Encoding.UTF8.GetBytes(text);
}
