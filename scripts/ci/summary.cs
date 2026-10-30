#!/usr/bin/env dotnet
#:property PublishAot=false
// The run summary (U6-NFR-13, U6-NFR-16), appended to $GITHUB_STEP_SUMMARY, or printed when that is unset. It reads what
// the job's checks left:
//   --tests <folder>   each test project's TRX results, in <folder>/<project>/, and the coverage checker's coverage.json;
//   --image <folder>   the tested and the pushed digests, the OCI archive's compressed size against the 150 MiB budget,
//                      and Grype's findings by severity and fix state, with the accepted ones.
// It fails nothing: the checks do. Past the size budget it adds a warning annotation. Native AOT is off: this is only
// ever run.
//
//   dotnet run scripts/ci/summary.cs -- [--tests TestResults] [--image <work folder>]

using System.Formats.Tar;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

const double BudgetMiB = 150;

var markdown = new StringBuilder();
if (Summary.Option(args, "--tests") is { } tests)
{
    Summary.Tests(markdown, Path.GetFullPath(tests));
}

if (Summary.Option(args, "--image") is { } image)
{
    Summary.Image(markdown, Path.GetFullPath(image), BudgetMiB);
}

if (long.TryParse(Environment.GetEnvironmentVariable("AIRLOCK_JOB_STARTED"), out var started))
{
    var elapsed = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(started);
    markdown.AppendLine(CultureInfo.InvariantCulture, $"The job ran for {(int)elapsed.TotalMinutes} min {elapsed.Seconds} s up to this summary.");
}

var target = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
if (string.IsNullOrEmpty(target))
{
    Console.Write(markdown.ToString());
}
else
{
    File.AppendAllText(target, markdown.ToString());
}

return 0;

internal static class Summary
{
    private static readonly string[] Severities = ["Critical", "High", "Medium", "Low", "Negligible", "Unknown"];

    public static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    public static void Tests(StringBuilder markdown, string results)
    {
        markdown.AppendLine(CultureInfo.InvariantCulture, $"### Tests on {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}").AppendLine();
        markdown.AppendLine("| Project | Passed | Failed | Skipped |").AppendLine("|---|---:|---:|---:|");
        DirectoryInfo[] folders = Directory.Exists(results)
            ? [.. new DirectoryInfo(results).EnumerateDirectories().OrderBy(folder => folder.Name, StringComparer.Ordinal)]
            : [];
        foreach (var folder in folders)
        {
            var trx = folder.EnumerateFiles("*.trx").OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault();
            if (trx is null)
            {
                continue;
            }

            var outcomes = XDocument.Load(trx.FullName).Descendants().Where(element => element.Name.LocalName == "UnitTestResult")
                .GroupBy(result => (string?)result.Attribute("outcome") ?? "Unknown")
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            markdown.AppendLine(CultureInfo.InvariantCulture,
                $"| {folder.Name} | {outcomes.GetValueOrDefault("Passed")} | {outcomes.GetValueOrDefault("Failed")} | {outcomes.GetValueOrDefault("NotExecuted")} |");
        }

        var coverage = Path.Combine(results, "coverage.json");
        if (!File.Exists(coverage))
        {
            markdown.AppendLine().AppendLine("No coverage.json: the coverage checker did not run.").AppendLine();
            return;
        }

        markdown.AppendLine().AppendLine("| Coverage | Lines | Target | Branches | Target | |").AppendLine("|---|---:|---:|---:|---:|---|");
        foreach (var project in JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(coverage)).EnumerateArray())
        {
            var passed = project.GetProperty("passed").GetBoolean();
            markdown.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {project.GetProperty("project").GetString()} | {project.GetProperty("lines").GetDouble():0.0} % | {project.GetProperty("linesTarget").GetInt32()} % | {project.GetProperty("branches").GetDouble():0.0} % | {project.GetProperty("branchesTarget").GetInt32()} % | {(passed ? "ok" : "**below**")} |"));
        }

        markdown.AppendLine();
    }

    public static void Image(StringBuilder markdown, string work, double budgetMiB)
    {
        markdown.AppendLine("### Image").AppendLine();
        var tested = Path.Combine(work, "image-id");
        if (File.Exists(tested))
        {
            markdown.AppendLine(CultureInfo.InvariantCulture, $"- Configuration digest, as tested: `{File.ReadAllText(tested).Trim()}`");
        }

        var pushed = Path.Combine(work, "image-digest");
        if (File.Exists(pushed))
        {
            markdown.AppendLine(CultureInfo.InvariantCulture, $"- Pushed as `{File.ReadAllText(pushed).Trim()}`");
        }

        var archive = Path.Combine(work, "image.oci.tar");
        if (File.Exists(archive))
        {
            var (layers, bytes) = CompressedSize(archive);
            var mebibytes = bytes / 1024.0 / 1024.0;
            markdown.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"- Download size: {mebibytes:0.0} MiB in {layers} layers, against a budget of {budgetMiB:0} MiB (U6-NFR-13)"));
            if (mebibytes > budgetMiB)
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"::warning title=Image size::The image downloads in {mebibytes:0.0} MiB, over the {budgetMiB:0} MiB budget (U6-NFR-13)"));
            }
        }

        markdown.AppendLine();
        var grype = Path.Combine(work, "grype.json");
        if (File.Exists(grype))
        {
            Findings(markdown, JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(grype)));
        }
    }

    // The registry serves the layers as they are in the archive, compressed: their sizes, from the image's manifest.
    private static (int Layers, long Bytes) CompressedSize(string archive)
    {
        Dictionary<string, byte[]> small = new(StringComparer.Ordinal);
        using (var reader = new TarReader(File.OpenRead(archive)))
        {
            while (reader.GetNextEntry() is { } entry)
            {
                if (entry.DataStream is { Length: < 1024 * 1024 } data)
                {
                    using var copy = new MemoryStream();
                    data.CopyTo(copy);
                    small[entry.Name] = copy.ToArray();
                }
            }
        }

        var descriptor = JsonSerializer.Deserialize<JsonElement>(small["index.json"]).GetProperty("manifests")[0];
        var manifest = Blob(small, descriptor);
        if (manifest.TryGetProperty("manifests", out var platforms))
        {
            manifest = Blob(small, platforms.EnumerateArray().First(entry =>
                entry.TryGetProperty("platform", out var platform) && platform.GetProperty("architecture").GetString() == "amd64"));
        }

        var layers = manifest.GetProperty("layers").EnumerateArray().ToList();
        return (layers.Count, layers.Sum(layer => layer.GetProperty("size").GetInt64()) + manifest.GetProperty("config").GetProperty("size").GetInt64());
    }

    private static JsonElement Blob(Dictionary<string, byte[]> small, JsonElement descriptor) =>
        JsonSerializer.Deserialize<JsonElement>(small["blobs/sha256/" + descriptor.GetProperty("digest").GetString()!["sha256:".Length..]]);

    private static void Findings(StringBuilder markdown, JsonElement report)
    {
        var fixable = Count(report, "matches", accepted: null);
        var unfixed = Count(report, "ignoredMatches", accepted: false);
        var accepted = Count(report, "ignoredMatches", accepted: true);
        markdown.AppendLine("| Grype | With a fix | Without a fix | Accepted in `.grype.yaml` |").AppendLine("|---|---:|---:|---:|");
        foreach (var severity in Severities)
        {
            markdown.AppendLine(CultureInfo.InvariantCulture,
                $"| {severity} | {fixable.GetValueOrDefault(severity)} | {unfixed.GetValueOrDefault(severity)} | {accepted.GetValueOrDefault(severity)} |");
        }

        markdown.AppendLine();
        foreach (var match in Elements(report, "ignoredMatches").Where(IsAccepted))
        {
            var rule = match.GetProperty("appliedIgnoreRules").EnumerateArray().First(IsNamed);
            markdown.AppendLine(CultureInfo.InvariantCulture,
                $"- Accepted: {match.GetProperty("vulnerability").GetProperty("id").GetString()} in {match.GetProperty("artifact").GetProperty("name").GetString()} {match.GetProperty("artifact").GetProperty("version").GetString()}: {(rule.TryGetProperty("reason", out var reason) ? reason.GetString() : "no reason given")}");
        }

        markdown.AppendLine();
    }

    private static Dictionary<string, int> Count(JsonElement report, string list, bool? accepted) =>
        Elements(report, list)
            .Where(match => accepted is null || IsAccepted(match) == accepted)
            .GroupBy(match => match.GetProperty("vulnerability").GetProperty("severity").GetString() ?? "Unknown", StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    private static JsonElement[] Elements(JsonElement report, string list) =>
        report.TryGetProperty(list, out var matches) && matches.ValueKind == JsonValueKind.Array ? [.. matches.EnumerateArray()] : [];

    // only-fixed adds rules by fix state alone; an acceptance in .grype.yaml names its vulnerability.
    private static bool IsAccepted(JsonElement match) =>
        match.TryGetProperty("appliedIgnoreRules", out var rules) && rules.EnumerateArray().Any(IsNamed);

    private static bool IsNamed(JsonElement rule) =>
        rule.TryGetProperty("vulnerability", out var id) && !string.IsNullOrEmpty(id.GetString());
}
