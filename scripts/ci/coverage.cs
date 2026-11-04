#!/usr/bin/env dotnet
#:property PublishAot=false
// The coverage checker (DR-12): each project's line and branch coverage, read from its own test project's Cobertura
// report in TestResults/<test project>/, against the targets the units set. Generated files, those under obj/ with the
// Razor components' generated code among them, are left out, as each unit measured it. It writes coverage.json beside
// the reports for the run summary, and fails when a project is below a target or has no report. Native AOT is off:
// this is only ever run.
//
//   dotnet test --coverage --coverage-output-format cobertura
//   dotnet run scripts/ci/coverage.cs [-- --results TestResults]

using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

Target[] targets =
[
    new("Airlock.Domain", "Airlock.Domain.Tests", 95, 90),
    new("Airlock.Application", "Airlock.Application.Tests", 90, 85),
    new("Airlock.Infrastructure", "Airlock.Infrastructure.Tests", 85, 80),
    new("Airlock.Web", "Airlock.Web.Tests", 90, 85),
];

var root = Coverage.RepositoryRoot();
var resultsOption = Array.IndexOf(args, "--results") is var index and >= 0 && index + 1 < args.Length ? args[index + 1] : null;
var results = Path.GetFullPath(resultsOption ?? Path.Combine(root, "TestResults"));
var measured = targets.Select(target => Coverage.Measure(target, results)).ToList();

Console.WriteLine("Coverage of each project from its own tests' report (lines, branches):");
foreach (var project in measured)
{
    Console.WriteLine(project.Report is null
        ? $"  FAIL  {project.Project}: no Cobertura report in {Path.Combine(results, project.TestProject)}"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"  {(project.Passed ? "ok  " : "FAIL")}  {project.Project}: lines {project.Lines:0.0}% (target {project.LinesTarget}%), branches {project.Branches:0.0}% (target {project.BranchesTarget}%)"));
}

File.WriteAllText(Path.Combine(results, "coverage.json"), JsonSerializer.Serialize(measured, Coverage.Json));
return measured.TrueForAll(project => project.Passed) ? 0 : 1;

internal sealed record Target(string Project, string TestProject, int LinesTarget, int BranchesTarget);

internal sealed record Measured(
    string Project, string TestProject, string? Report, int LinesTarget, int BranchesTarget,
    int CoveredLines, int ValidLines, int CoveredBranches, int ValidBranches)
{
    public double Lines => ValidLines == 0 ? 0 : 100.0 * CoveredLines / ValidLines;

    public double Branches => ValidBranches == 0 ? 100 : 100.0 * CoveredBranches / ValidBranches;

    public bool Passed => Report is not null && ValidLines > 0 && Lines >= LinesTarget && Branches >= BranchesTarget;
}

internal static class Coverage
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The folder holding Airlock.slnx, found from the working folder up.</summary>
    public static string RepositoryRoot()
    {
        for (var folder = new DirectoryInfo(Environment.CurrentDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Airlock.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException("Run the coverage checker inside the repository: no Airlock.slnx above " + Environment.CurrentDirectory);
    }

    public static Measured Measure(Target target, string results)
    {
        var folder = Path.Combine(results, target.TestProject);
        var report = Directory.Exists(folder)
            ? new DirectoryInfo(folder).EnumerateFiles("*.cobertura.xml").OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault()
            : null;
        var package = report is null
            ? null
            : XDocument.Load(report.FullName).Descendants("package").FirstOrDefault(element => (string?)element.Attribute("name") == target.Project);
        if (report is null || package is null)
        {
            return new Measured(target.Project, target.TestProject, null, target.LinesTarget, target.BranchesTarget, 0, 0, 0, 0);
        }

        // A line can be listed by several classes, a type's and its compiler-generated ones: each counts once, with its
        // best hits and branches.
        Dictionary<(string File, int Number), (bool Hit, int Covered, int Total)> lines = [];
        foreach (var type in package.Descendants("class"))
        {
            var file = (string?)type.Attribute("filename") ?? string.Empty;
            if (file.Replace('\\', '/').Contains("/obj/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var line in type.Elements("lines").Elements("line"))
            {
                var key = (file, (int)line.Attribute("number")!);
                var hit = long.Parse((string?)line.Attribute("hits") ?? "0", CultureInfo.InvariantCulture) > 0;
                var (covered, total) = Conditions((string?)line.Attribute("condition-coverage"));
                lines[key] = lines.TryGetValue(key, out var seen)
                    ? (seen.Hit || hit, Math.Max(seen.Covered, covered), Math.Max(seen.Total, total))
                    : (hit, covered, total);
            }
        }

        return new Measured(
            target.Project, target.TestProject, report.FullName, target.LinesTarget, target.BranchesTarget,
            lines.Values.Count(line => line.Hit), lines.Count, lines.Values.Sum(line => line.Covered), lines.Values.Sum(line => line.Total));
    }

    // "50% (1/2)": the covered and total branches.
    private static (int Covered, int Total) Conditions(string? coverage)
    {
        var open = coverage?.IndexOf('(', StringComparison.Ordinal) ?? -1;
        var slash = coverage?.IndexOf('/', StringComparison.Ordinal) ?? -1;
        var close = coverage?.IndexOf(')', StringComparison.Ordinal) ?? -1;
        return coverage is null || open < 0 || slash < open || close < slash
            ? (0, 0)
            : (int.Parse(coverage[(open + 1)..slash], CultureInfo.InvariantCulture), int.Parse(coverage[(slash + 1)..close], CultureInfo.InvariantCulture));
    }
}
