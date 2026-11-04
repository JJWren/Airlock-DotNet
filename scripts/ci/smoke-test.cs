#!/usr/bin/env dotnet
#:property PublishAot=false
// The image's smoke test (DR-13, U6-NFR-05, U6-NFR-08, U6-NFR-11, U6-NFR-12, U6-NFR-16). It runs the built image
// through docker-compose.yml and tests/smoke/compose.smoke.yml, as the compose project airlock-smoke, in two phases:
// without a key, then with a stand-in key and NVD unreachable. Compose reads no .env, which may hold a real key: it
// gets an empty env file of its own, and every variable it reads from here. On a failure the test prints the
// container's log; it always removes the container and its volume. Native AOT is off: this is only ever run.
//
//   dotnet run scripts/ci/smoke-test.cs -- --image airlock-dotnet:smoke [--port 18080]

using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

var image = Smoke.Option(args, "--image")
    ?? throw new ArgumentException("usage: dotnet run scripts/ci/smoke-test.cs -- --image <image> [--port <port>]");
var port = Smoke.Option(args, "--port") ?? "18080";
var root = Smoke.RepositoryRoot();
var emptyEnvFile = Path.Combine(Path.GetTempPath(), $"airlock-smoke-{Guid.NewGuid():N}.env");
File.WriteAllText(emptyEnvFile, string.Empty);
var compose = new Compose(root, emptyEnvFile, image, port);
var expected = Smoke.ExpectedWarnings(Path.Combine(root, "tests", "smoke", "expected-warnings.txt"));
using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(30) };
List<string> logs = [];
var failed = false;
try
{
    // Phase 1: no key.
    await compose.UpAsync(Phase.Keyless);
    await Smoke.HealthAsync(http);
    await Smoke.PageAndAssetsAsync(http);
    await Smoke.RefusedScanAsync(http);
    await Smoke.RunsAsAppAsync(compose);
    var keyless = await compose.LogsAsync(Phase.Keyless);
    logs.Add(keyless);
    Smoke.StartWarnings(keyless, expected, "the keyless start");

    // Phase 2: the stand-in key, and NVD at a closed port.
    await compose.UpAsync(Phase.NvdUnreachable);
    var scanId = await Smoke.AcceptedScanAsync(http);
    await Smoke.IncompleteScanAsync(http, scanId);
    await Smoke.ReportAsync(http, scanId);
    var unreachable = await compose.LogsAsync(Phase.NvdUnreachable);
    logs.Add(unreachable);
    Smoke.StartWarnings(unreachable, expected, "the restart with NVD unreachable");

    // Both phases' logs, and the image itself.
    Smoke.Check(logs.All(log => !log.Contains(Smoke.HealthPath, StringComparison.Ordinal)), "no log line mentions the health path");
    Smoke.Check(logs.All(log => !log.Contains(Smoke.Canary, StringComparison.Ordinal)), "the stand-in key is in no log line");
    var inspect = await Shell.RunAsync("docker", ["image", "inspect", image]);
    var history = await Shell.RunAsync("docker", ["image", "history", "--no-trunc", "--format", "{{.CreatedBy}}", image]);
    Smoke.Check(
        inspect.ExitCode == 0 && history.ExitCode == 0
            && !inspect.Output.Contains(Smoke.Canary, StringComparison.Ordinal) && !history.Output.Contains(Smoke.Canary, StringComparison.Ordinal),
        "the stand-in key is in neither the image's configuration nor its history");
    Console.WriteLine("Smoke test passed.");
}
catch (Exception exception) when (exception is SmokeFailure or HttpRequestException or TaskCanceledException or JsonException
    or KeyNotFoundException or InvalidOperationException)
{
    failed = true;
    Console.Error.WriteLine($"FAILED: {exception.Message}");
    foreach (var log in logs)
    {
        Console.Error.WriteLine("---- an earlier phase's log ----");
        Console.Error.WriteLine(log);
    }

    Console.Error.WriteLine("---- the container's log ----");
    var current = await compose.RunAsync(Phase.Keyless, "logs", "--no-color");
    Console.Error.WriteLine(current.Output + current.Error);
}
finally
{
    await compose.RunAsync(Phase.Keyless, "down", "--volumes", "--remove-orphans");
    File.Delete(emptyEnvFile);
}

return failed ? 1 : 0;

/// <summary>Each phase's settings: the key, and NVD's address. The smoke override gives NVD's real one by default.</summary>
internal sealed record Phase(string Name, string? ApiKey, string? NvdUrl)
{
    public static readonly Phase Keyless = new("keyless", null, null);

    public static readonly Phase NvdUnreachable = new("NVD unreachable", Smoke.Canary, Smoke.UnreachableNvd);
}

/// <summary>A check that did not hold.</summary>
internal sealed class SmokeFailure(string message) : Exception(message);

/// <summary>docker compose, for the project airlock-smoke, with docker-compose.yml and the smoke override only.</summary>
internal sealed class Compose(string root, string emptyEnvFile, string image, string port)
{
    // Every variable the two files read, and Compose's own that could add a file, a profile or another project name.
    private static readonly string[] Cleared =
    [
        "NVD_API_KEY", "LOG_LEVEL", "SCAN_UPLOAD_PATH", "AIRLOCK_PORT", "AIRLOCK_IMAGE_TAG", "AIRLOCK_SMOKE_IMAGE",
        "AIRLOCK_SMOKE_NVD_URL", "COMPOSE_FILE", "COMPOSE_PROJECT_NAME", "COMPOSE_PROFILES", "COMPOSE_ENV_FILES",
    ];

    public async Task UpAsync(Phase phase)
    {
        var started = Stopwatch.StartNew();
        var up = await RunAsync(phase, "up", "--detach", "--wait", "--wait-timeout", "30", "--no-build");
        Smoke.Check(up.ExitCode == 0, $"{phase.Name}: healthy within 30 seconds, after {started.Elapsed.TotalSeconds:0.0} s", up.Error);
    }

    public async Task<string> LogsAsync(Phase phase)
    {
        var logs = await RunAsync(phase, "logs", "--no-color", "--no-log-prefix", "airlock");
        Smoke.Check(logs.ExitCode == 0, $"{phase.Name}: the container's log is read", logs.Error);
        return logs.Output;
    }

    public Task<ShellResult> RunAsync(Phase phase, params string[] arguments)
    {
        var environment = Cleared.ToDictionary(name => name, _ => (string?)null, StringComparer.Ordinal);
        environment["AIRLOCK_SMOKE_IMAGE"] = image;
        environment["SCAN_UPLOAD_PATH"] = Path.Combine(root, "tests", "smoke", "uploads");
        environment["AIRLOCK_PORT"] = port;
        environment["LOG_LEVEL"] = "Information";
        environment["NVD_API_KEY"] = phase.ApiKey;
        environment["AIRLOCK_SMOKE_NVD_URL"] = phase.NvdUrl;
        return Shell.RunAsync(
            "docker",
            [
                "compose", "--project-name", Smoke.Project, "--env-file", emptyEnvFile,
                "--file", Path.Combine(root, "docker-compose.yml"),
                "--file", Path.Combine(root, "tests", "smoke", "compose.smoke.yml"),
                .. arguments,
            ],
            environment,
            root);
    }
}

internal sealed record ShellResult(int ExitCode, string Output, string Error);

internal static class Shell
{
    public static async Task<ShellResult> RunAsync(
        string file, IEnumerable<string> arguments, IReadOnlyDictionary<string, string?>? environment = null, string? workingDirectory = null)
    {
        var start = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory ?? string.Empty,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in environment ?? new Dictionary<string, string?>())
        {
            if (value is null)
            {
                start.Environment.Remove(name);
            }
            else
            {
                start.Environment[name] = value;
            }
        }

        using var process = Process.Start(start) ?? throw new SmokeFailure($"{file} did not start");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ShellResult(process.ExitCode, await output, await error);
    }
}

internal static partial class Smoke
{
    public const string Project = "airlock-smoke";

    // A fixed stand-in that NVD would refuse, easy to search for, and sent nowhere but a closed port (U6-NFR-05).
    public const string Canary = "airlock-smoke-canary-not-an-nvd-key";

    // A closed port on this machine's loopback: every lookup fails, so the scan must end Incomplete (ADR-0002).
    public const string UnreachableNvd = "https://127.0.0.1:9/rest/json/cves/2.0";

    public const string HealthPath = "/api/v2/system/health";

    public static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

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

        throw new SmokeFailure("run the smoke test inside the repository: no Airlock.slnx above " + Environment.CurrentDirectory);
    }

    /// <summary>The lines of expected-warnings.txt that are not comments: each a <c>category[event id]</c>.</summary>
    public static HashSet<string> ExpectedWarnings(string path) =>
        [.. File.ReadAllLines(path).Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#'))];

    public static void Check(bool holds, string check, string? detail = null)
    {
        if (!holds)
        {
            throw new SmokeFailure(string.IsNullOrWhiteSpace(detail) ? check : $"{check}: {detail.Trim()}");
        }

        Console.WriteLine($"ok   {check}");
    }

    public static async Task HealthAsync(HttpClient http)
    {
        using var response = await http.GetAsync(new Uri(HealthPath, UriKind.Relative));
        var health = Json(await response.Content.ReadAsStringAsync());
        var checks = health.GetProperty("checks").EnumerateArray().ToDictionary(check => check.GetProperty("name").GetString() ?? string.Empty);
        Check(
            response.StatusCode == HttpStatusCode.OK
                && health.GetProperty("status").GetString() == "DEGRADED"
                && checks["Syft"].GetProperty("passing").GetBoolean()
                && checks["Syft"].GetProperty("detail").GetString() == "1.55.0"
                && !checks["NVD API key"].GetProperty("passing").GetBoolean(),
            "health answers 200 DEGRADED, Syft 1.55.0 passing and the key check failing",
            health.GetRawText());
    }

    // The page's stylesheets and scripts, Blazor's own among them: a published build must serve the framework's assets,
    // which come from a package (Build and Test found blazor.web.js missing from an image).
    public static async Task PageAndAssetsAsync(HttpClient http)
    {
        using var page = await http.GetAsync(new Uri("/", UriKind.Relative));
        var html = await page.Content.ReadAsStringAsync();
        Check(page.StatusCode == HttpStatusCode.OK, "Control Center's page answers 200", page.StatusCode.ToString());

        var stylesheets = LinkTag().Matches(html)
            .Where(link => StylesheetRel().IsMatch(link.Value))
            .Select(link => Href().Match(link.Value))
            .Where(href => href.Success)
            .Select(href => WebUtility.HtmlDecode(href.Groups[1].Value))
            .ToList();
        var scripts = ScriptSrc().Matches(html).Select(script => WebUtility.HtmlDecode(script.Groups[1].Value)).ToList();
        Check(stylesheets.Count > 0, "the page names its stylesheets");
        Check(scripts.Exists(script => script.Contains("_framework/blazor.web", StringComparison.Ordinal)), "the page names Blazor's script", string.Join(", ", scripts));
        foreach (var asset in stylesheets.Concat(scripts))
        {
            using var response = await http.GetAsync(new Uri(http.BaseAddress!, asset));
            Check(response.StatusCode == HttpStatusCode.OK, $"its {asset} answers 200", response.StatusCode.ToString());
        }
    }

    public static async Task RefusedScanAsync(HttpClient http)
    {
        using var response = await SubmitAsync(http);
        var problem = Json(await response.Content.ReadAsStringAsync());
        Check(
            response.StatusCode == HttpStatusCode.ServiceUnavailable
                && (problem.GetProperty("type").GetString() ?? string.Empty).EndsWith("#not-ready", StringComparison.Ordinal)
                && (problem.GetProperty("detail").GetString() ?? string.Empty).Contains("NVD API key", StringComparison.Ordinal),
            "a scan of /uploads is refused with 503 and the missing-key problem",
            problem.GetRawText());
    }

    public static async Task RunsAsAppAsync(Compose compose)
    {
        // The UID column is found by its header: Compose's own columns, such as SERVICE, may come first.
        var top = await compose.RunAsync(Phase.Keyless, "top", "airlock");
        var rows = top.Output.Split('\n').Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();
        var header = rows.FirstOrDefault(fields => fields.Contains("UID") && fields.Contains("PID")) ?? [];
        var uid = Array.IndexOf(header, "UID");
        var pid = Array.IndexOf(header, "PID");
        var users = rows
            .Where(fields => uid >= 0 && fields.Length > Math.Max(uid, pid) && int.TryParse(fields[pid], out _))
            .Select(fields => fields[uid])
            .ToList();
        Check(top.ExitCode == 0 && users.Count > 0 && users.TrueForAll(user => user == "1654"), "every process runs as UID 1654", top.Output + top.Error);
    }

    public static async Task<string> AcceptedScanAsync(HttpClient http)
    {
        using var response = await SubmitAsync(http);
        var accepted = Json(await response.Content.ReadAsStringAsync());
        Check(
            response.StatusCode == HttpStatusCode.Accepted && response.Headers.Location is not null,
            "with the stand-in key, a scan of /uploads is accepted with 202",
            accepted.GetRawText());
        return accepted.GetProperty("id").GetString() ?? throw new SmokeFailure("the accepted scan has no id");
    }

    public static async Task IncompleteScanAsync(HttpClient http, string scanId)
    {
        var deadline = Stopwatch.StartNew();
        JsonElement scan;
        do
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            using var response = await http.GetAsync(new Uri($"/api/v2/scans/{scanId}", UriKind.Relative));
            scan = Json(await response.Content.ReadAsStringAsync());
        }
        while (scan.GetProperty("status").GetString() == "RUNNING" && deadline.Elapsed < TimeSpan.FromMinutes(3));

        var summary = scan.GetProperty("summary");
        var total = summary.ValueKind == JsonValueKind.Object ? summary.GetProperty("totalPackages").GetInt32() : 0;
        Check(
            scan.GetProperty("status").GetString() == "INCOMPLETE"
                && total > 0
                && summary.GetProperty("unverifiedPackages").GetInt32() == total
                && summary.GetProperty("vulnerablePackages").GetInt32() == 0
                && scan.GetProperty("unverifiedPackages").GetArrayLength() == total
                && scan.GetProperty("vulnerablePackages").GetArrayLength() == 0,
            $"the scan ends Incomplete within 3 minutes, after {deadline.Elapsed.TotalSeconds:0} s, with all {total} packages Unverified",
            scan.GetRawText());
    }

    public static async Task ReportAsync(HttpClient http, string scanId)
    {
        using var response = await http.GetAsync(new Uri($"/api/v2/scans/{scanId}/report.pdf", UriKind.Relative));
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Check(
            response.StatusCode == HttpStatusCode.OK
                && response.Content.Headers.ContentType?.MediaType == "application/pdf"
                && bytes.AsSpan().StartsWith("%PDF-"u8),
            $"its PDF report downloads, {bytes.Length:N0} bytes");
    }

    /// <summary>The start's warnings: the log up to "Application started". Each must be on the list, and no error may appear.</summary>
    public static void StartWarnings(string log, HashSet<string> expected, string start)
    {
        var lines = log.Split('\n');
        var started = Array.FindIndex(lines, line => line.Contains("Application started", StringComparison.Ordinal));
        Check(started >= 0, $"{start} logs 'Application started'");
        var unexpected = lines.Take(started + 1)
            .Select(line => LevelAndEvent().Match(line))
            .Where(match => match.Success && (match.Groups[1].Value != "warn" || !expected.Contains(match.Groups[2].Value)))
            .Select(match => match.Value)
            .ToList();
        Check(unexpected.Count == 0, $"{start} logs only the expected warnings", string.Join("; ", unexpected));
    }

    private static async Task<HttpResponseMessage> SubmitAsync(HttpClient http)
    {
        using var form = new MultipartFormDataContent { { new StringContent("/uploads"), "targetPath" } };
        return await http.PostAsync(new Uri("/api/v2/scans", UriKind.Relative), form);
    }

    private static JsonElement Json(string text) => JsonSerializer.Deserialize<JsonElement>(text);

    [GeneratedRegex("<link\\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LinkTag();

    [GeneratedRegex("\\brel=\"stylesheet\"", RegexOptions.IgnoreCase)]
    private static partial Regex StylesheetRel();

    [GeneratedRegex("\\bhref=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex Href();

    [GeneratedRegex("<script\\b[^>]*\\bsrc=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptSrc();

    // The console's single-line format: the time, then "warn: Category[event id]".
    [GeneratedRegex("^\\S+ (warn|fail|crit): (\\S+\\[\\d+\\])")]
    private static partial Regex LevelAndEvent();
}
