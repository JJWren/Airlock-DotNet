using System.Text.Json;
using Airlock.Delivery.Tests.Support;

namespace Airlock.Delivery.Tests;

// DR-02, DR-03, DR-05, DR-06, DR-10, DR-24, U6-NFR-05, U6-NFR-08 — the three stages, a runtime stage that needs no shell,
// its user, settings, health check and labels, no key in any build setting, and an allowlist build context.
public sealed class DockerfileTests
{
    private static readonly IReadOnlyList<Stage> Stages = DockerfileModel.Stages("Dockerfile");

    private static readonly Stage Runtime = Stages.Single(stage => stage.Name == "runtime");

    [Fact]
    public void Syft_the_SDK_and_the_chiseled_runtime_are_the_three_stages()
    {
        Assert.Equal(["syft", "build", "runtime"], Stages.Select(stage => stage.Name));
        Assert.StartsWith("ghcr.io/anchore/syft:", Stages[0].Image, StringComparison.Ordinal);
        Assert.StartsWith("mcr.microsoft.com/dotnet/sdk:", Stages[1].Image, StringComparison.Ordinal);
        Assert.StartsWith("mcr.microsoft.com/dotnet/aspnet:", Stages[2].Image, StringComparison.Ordinal);
    }

    [Fact]
    public void The_build_publishes_Airlock_Web_for_linux_x64_framework_dependent_without_an_apphost()
    {
        var publish = Assert.Single(Stages[1].All("RUN"), run => run.Arguments.Contains("dotnet publish", StringComparison.Ordinal)).Arguments;

        foreach (var part in (string[])["src/Airlock.Web/Airlock.Web.csproj", "--configuration Release", "--runtime linux-x64", "--self-contained false", "-p:UseAppHost=false", "--output /app"])
        {
            Assert.Contains(part, publish, StringComparison.Ordinal);
        }

        Assert.Contains(Stages[1].All("COPY"), copy => copy.Arguments == "global.json Directory.Build.props Directory.Packages.props .editorconfig ./");
    }

    [Fact]
    public void The_notices_join_the_application_and_nothing_in_it_is_writable_but_by_root()
    {
        var build = Stages[1].Instructions;
        var notices = build.ToList().FindIndex(instruction => instruction is { Keyword: "COPY", Arguments: "LICENSE THIRD-PARTY-NOTICES.md /app/" });
        var licences = build.ToList().FindIndex(instruction => instruction is { Keyword: "COPY", Arguments: "licenses/ /app/licenses/" });
        var modes = build.ToList().FindIndex(instruction => instruction is { Keyword: "RUN", Arguments: "chmod -R u=rwX,go=rX /app" });

        Assert.True(notices >= 0 && licences >= 0, "the build stage copies LICENSE, THIRD-PARTY-NOTICES.md and licenses/ into /app");
        Assert.True(modes > Math.Max(notices, licences), "the modes are set after everything is in /app, whatever the build context gave");
        Assert.Equal(build.Count - 1, modes);
    }

    [Fact]
    public void The_runtime_stage_runs_nothing_and_holds_the_application_Syft_and_the_writable_folders()
    {
        Assert.Empty(Runtime.All("RUN"));
        Assert.Equal(
            [
                "--from=build /app/ /app/",
                "--from=syft /syft /app/bin/syft",
                "--from=build --chown=1654:1654 /rootfs/var/lib/airlock/ /var/lib/airlock/",
            ],
            Runtime.All("COPY").Select(copy => copy.Arguments));
        Assert.Equal("/app", Assert.Single(Runtime.All("WORKDIR")).Arguments);
    }

    [Fact]
    public void It_runs_as_the_non_root_app_user_on_port_8080()
    {
        Assert.Equal("1654", Assert.Single(Runtime.All("USER")).Arguments);
        Assert.Equal("8080", Assert.Single(Runtime.All("EXPOSE")).Arguments);
    }

    [Fact]
    public void It_listens_on_every_interface_and_keeps_its_staging_root_and_keys_on_the_volume()
    {
        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["URLS"] = "http://+:8080",
                ["ASPNETCORE_HTTP_PORTS"] = string.Empty,
                ["Airlock__Staging__Root"] = "/var/lib/airlock/staging",
                ["Airlock__DataProtection__KeysPath"] = "/var/lib/airlock/keys",
            },
            RuntimeEnvironment());
    }

    [Fact]
    public void Its_entry_point_and_health_check_are_executables_with_arguments_needing_no_shell()
    {
        Assert.Equal(["/usr/bin/dotnet", "/app/Airlock.Web.dll"], Exec(Assert.Single(Runtime.All("ENTRYPOINT")).Arguments));

        var check = Assert.Single(Runtime.All("HEALTHCHECK")).Arguments;
        var command = check.IndexOf("CMD ", StringComparison.Ordinal);
        Assert.Equal("--interval=30s --timeout=5s --start-period=30s --start-interval=2s --retries=3", check[..command].Trim());
        Assert.Equal(["/usr/bin/dotnet", "/app/Airlock.Web.dll", "--health-probe"], Exec(check[(command + 4)..]));
    }

    [Fact]
    public void It_carries_the_OCI_labels()
    {
        var labels = string.Join(' ', Runtime.All("LABEL").Select(label => label.Arguments));

        foreach (var label in (string[])
            [
                "org.opencontainers.image.source=\"https://github.com/JJWren/Airlock-DotNet\"",
                "org.opencontainers.image.licenses=\"MIT\"",
                "org.opencontainers.image.title=\"Airlock\"",
                "org.opencontainers.image.description=",
                "org.opencontainers.image.version=\"${VERSION}\"",
                "org.opencontainers.image.revision=\"${REVISION}\"",
                "org.opencontainers.image.created=\"${CREATED}\"",
            ])
        {
            Assert.Contains(label, labels, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void No_build_argument_or_setting_names_a_key_and_no_frontend_is_pulled()
    {
        var names = DockerfileModel.Instructions("Dockerfile")
            .Where(instruction => instruction.Keyword is "ARG" or "ENV")
            .SelectMany(instruction => instruction.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(word => word.Split('=')[0]);

        Assert.All(names, name => Assert.DoesNotMatch("(?i)key|token|secret|password", name.Replace("KeysPath", string.Empty, StringComparison.Ordinal)));
        Assert.DoesNotContain(Repository.Lines("Dockerfile"), line => line.StartsWith("# syntax=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_build_context_is_an_allowlist_with_no_env_file()
    {
        var rules = Repository.Lines(".dockerignore").Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')).ToList();

        Assert.Equal(
            [
                "*", "!global.json", "!Directory.Build.props", "!Directory.Packages.props", "!.editorconfig", "!src/", "!LICENSE",
                "!THIRD-PARTY-NOTICES.md", "!licenses/", "**/bin/", "**/obj/", "**/.env*",
            ],
            rules);
    }

    /// <summary>The runtime stage's environment, each ENV's name=value pairs.</summary>
    internal static Dictionary<string, string> RuntimeEnvironment() =>
        Runtime.All("ENV")
            .SelectMany(env => env.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair.Length > 1 ? pair[1] : string.Empty, StringComparer.Ordinal);

    private static string[] Exec(string arguments) => JsonSerializer.Deserialize<string[]>(arguments.Trim()) ?? [];
}
