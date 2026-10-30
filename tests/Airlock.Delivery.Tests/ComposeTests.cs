using System.Text.RegularExpressions;
using Airlock.Delivery.Tests.Support;
using YamlDotNet.RepresentationModel;

namespace Airlock.Delivery.Tests;

// DR-07 to DR-09, DR-29, U6-NFR-09, U6-NFR-10, U6-NFR-12 — one hardened service on 127.0.0.1, its scan folder required
// and read-only, its writable places on the volume, every variable it reads in .env.example, and the smoke override
// kept to its three keys.
public sealed partial class ComposeTests
{
    private static readonly YamlMappingNode Compose = Yaml.Load("docker-compose.yml");

    private static readonly YamlNode Airlock = Compose.Mapping("services", "airlock");

    [Fact]
    public void One_service_runs_the_published_image_or_builds_this_copy()
    {
        Assert.Equal("airlock", Compose.Text("name"));
        Assert.Equal(["airlock"], Compose.Keys("services"));
        Assert.Equal("ghcr.io/jjwren/airlock-dotnet:${AIRLOCK_IMAGE_TAG:-latest}", Airlock.Text("image"));
        Assert.Equal(".", Airlock.Text("build", "context"));
        Assert.Equal("airlock", Airlock.Text("hostname"));
        Assert.Equal("unless-stopped", Airlock.Text("restart"));
    }

    [Fact]
    public void The_container_runs_hardened()
    {
        Assert.Equal("true", Airlock.Text("init"));
        Assert.Equal("true", Airlock.Text("read_only"));
        Assert.Equal(["ALL"], Airlock.Texts("cap_drop"));
        Assert.Equal(["no-new-privileges:true"], Airlock.Texts("security_opt"));
        Assert.Equal(["/tmp:rw,noexec,nosuid,nodev,size=64m"], Airlock.Texts("tmpfs"));
        foreach (var key in (string[])["privileged", "network_mode", "pid", "ipc", "cap_add", "devices", "user"])
        {
            Assert.Null(Airlock.Find(key));
        }

        Assert.DoesNotContain("docker.sock", Repository.Read("docker-compose.yml"), StringComparison.Ordinal);
    }

    [Fact]
    public void Only_this_machine_reaches_the_port()
    {
        Assert.Equal(["127.0.0.1:${AIRLOCK_PORT:-8080}:8080"], Airlock.Texts("ports"));
    }

    [Fact]
    public void It_passes_the_original_variables_and_scans_only_uploads()
    {
        Assert.Equal(
            ["NVD_API_KEY=${NVD_API_KEY:-}", "LOG_LEVEL=${LOG_LEVEL:-Information}", "Airlock__AllowedScanRoots__0=/uploads"],
            Airlock.Entries("environment").Select(variable => $"{variable.Key}={(variable.Value as YamlScalarNode)?.Value}"));
    }

    [Fact]
    public void The_scan_folder_is_required_and_read_only_and_the_volume_holds_the_staging_root_and_the_keys()
    {
        var volumes = Airlock.Items("volumes");
        var uploads = Assert.Single(volumes, volume => volume.Text("target") == "/uploads");
        var data = Assert.Single(volumes, volume => volume.Text("target") == "/var/lib/airlock");

        Assert.Equal(2, volumes.Count);
        Assert.Equal("bind", uploads.Text("type"));
        Assert.Equal("${SCAN_UPLOAD_PATH:?Set SCAN_UPLOAD_PATH in .env to the folder Airlock may scan}", uploads.Text("source"));
        Assert.Equal("true", uploads.Text("read_only"));
        Assert.Equal("false", uploads.Text("bind", "create_host_path"));
        Assert.Equal("volume", data.Text("type"));
        Assert.Equal("airlock-data", data.Text("source"));
        Assert.Equal(["airlock-data"], Compose.Keys("volumes"));

        // The image puts the staging root and the keys under that mount, never in /tmp, which is in memory.
        var environment = DockerfileTests.RuntimeEnvironment();
        Assert.StartsWith("/var/lib/airlock/", environment["Airlock__Staging__Root"], StringComparison.Ordinal);
        Assert.StartsWith("/var/lib/airlock/", environment["Airlock__DataProtection__KeysPath"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_example_env_file_names_every_variable_the_compose_file_reads_each_with_a_comment_and_no_secret()
    {
        var read = Variable().Matches(Repository.Read("docker-compose.yml")).Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal);
        var lines = Repository.Lines(".env.example");
        var named = lines.Select((line, index) => (Line: line, Index: index)).Where(line => Assignment().IsMatch(line.Line)).ToList();

        Assert.Equal(read, named.Select(line => line.Line[..line.Line.IndexOf('=', StringComparison.Ordinal)]).Order(StringComparer.Ordinal));
        Assert.All(named, line => Assert.StartsWith("#", lines[line.Index - 1], StringComparison.Ordinal));
        Assert.Contains("NVD_API_KEY=", lines);
        Assert.Contains("SCAN_UPLOAD_PATH=", lines);
    }

    [Fact]
    public void Git_ignores_the_env_file_and_the_operators_override()
    {
        var ignored = Repository.Lines(".gitignore");

        Assert.Contains(".env", ignored);
        Assert.Contains("docker-compose.override.yml", ignored);
    }

    [Fact]
    public void The_smoke_override_sets_only_the_image_never_pulled_and_NVDs_address()
    {
        var smoke = Yaml.Load("tests/smoke/compose.smoke.yml");

        Assert.Equal(["services"], smoke.Keys());
        Assert.Equal(["airlock"], smoke.Keys("services"));
        Assert.Equal(["image", "pull_policy", "environment"], smoke.Keys("services", "airlock"));
        Assert.StartsWith("${AIRLOCK_SMOKE_IMAGE:?", smoke.Text("services", "airlock", "image"), StringComparison.Ordinal);
        Assert.Equal("never", smoke.Text("services", "airlock", "pull_policy"));
        Assert.Equal(["Airlock__Nvd__BaseUrl"], smoke.Keys("services", "airlock", "environment"));
    }

    [GeneratedRegex(@"\$\{([A-Z_][A-Z0-9_]*)")]
    private static partial Regex Variable();

    [GeneratedRegex(@"^[A-Z_][A-Z0-9_]*=")]
    private static partial Regex Assignment();
}
