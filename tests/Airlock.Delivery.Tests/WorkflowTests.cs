using Airlock.Delivery.Tests.Support;
using YamlDotNet.RepresentationModel;

namespace Airlock.Delivery.Tests;

// DR-11, DR-19, DR-22, DR-23, U6-NFR-03, U6-NFR-04, U6-NFR-07 — what the linters cannot see: the permissions, runners,
// time limits and concurrency groups the design chose, and the release's guards and order.
public sealed class WorkflowTests
{
    private const string Ref = "${{ github.ref }}";

    public static TheoryData<string> Workflows => [.. Repository.Workflows];

    [Theory]
    [MemberData(nameof(Workflows))]
    public void A_workflow_grants_contents_read_and_nothing_more_at_its_top(string workflow)
    {
        Assert.Equal(["contents: read"], Permissions(Yaml.Load(workflow)));
    }

    [Fact]
    public void Only_codeql_and_the_release_jobs_ask_for_more_and_only_for_what_they_need()
    {
        Dictionary<string, string[]> expected = new(StringComparer.Ordinal)
        {
            ["ci.yml/lint"] = [],
            ["ci.yml/test"] = [],
            ["ci.yml/image"] = [],
            ["codeql.yml/analyze"] = ["actions: read", "contents: read", "security-events: write"],
            ["release.yml/version"] = ["contents: read", "packages: read"],
            ["release.yml/ci"] = ["contents: read"],
            ["release.yml/publish"] = ["attestations: write", "contents: read", "id-token: write", "packages: write"],
        };

        Assert.Equal(expected, Jobs().ToDictionary(job => job.Id, job => Permissions(job.Node).ToArray(), StringComparer.Ordinal));
    }

    [Fact]
    public void Jobs_run_on_runners_named_by_their_image()
    {
        foreach (var job in Jobs().Where(job => job.Node.Find("uses") is null))
        {
            var runsOn = job.Node.Text("runs-on");
            string[] runners = runsOn == "${{ matrix.os }}"
                ? [.. job.Node.Items("strategy", "matrix", "include").Select(entry => entry.Text("os") ?? string.Empty)]
                : [runsOn ?? string.Empty];
            Assert.NotEmpty(runners);
            Assert.All(runners, runner => Assert.Contains(runner, (string[])["ubuntu-24.04", "windows-2025"]));
        }
    }

    [Fact]
    public void Every_job_that_runs_steps_has_its_time_limit()
    {
        Dictionary<string, string?> expected = new(StringComparer.Ordinal)
        {
            ["ci.yml/lint"] = "10",
            ["ci.yml/test"] = "30",
            ["ci.yml/image"] = "20",
            ["codeql.yml/analyze"] = "30",
            ["release.yml/version"] = "5",
            ["release.yml/ci"] = null,
            ["release.yml/publish"] = "30",
        };

        Assert.Equal(expected, Jobs().ToDictionary(job => job.Id, job => job.Node.Text("timeout-minutes"), StringComparer.Ordinal));
        Assert.All(Jobs().Where(job => job.Node.Text("timeout-minutes") is null), job => Assert.NotNull(job.Node.Find("uses")));
    }

    [Theory]
    [InlineData(".github/workflows/ci.yml", "ci-", "true")]
    [InlineData(".github/workflows/codeql.yml", "codeql-", null)]
    [InlineData(".github/workflows/release.yml", "release-", "false")]
    public void Each_workflow_has_a_concurrency_group_of_its_own_and_only_ci_cancels_an_older_run(
        string workflow, string prefix, string? cancelInProgress)
    {
        var root = Yaml.Load(workflow);

        Assert.Equal(prefix + Ref, root.Text("concurrency", "group"));
        Assert.Equal(cancelInProgress, root.Text("concurrency", "cancel-in-progress"));
    }

    [Theory]
    [MemberData(nameof(Workflows))]
    public void Every_checkout_keeps_no_credentials(string workflow)
    {
        var checkouts = Steps(workflow).Where(step => step.Text("uses")?.StartsWith("actions/checkout@", StringComparison.Ordinal) == true).ToList();

        Assert.NotEmpty(checkouts);
        Assert.All(checkouts, checkout => Assert.Equal("false", checkout.Text("with", "persist-credentials")));
    }

    [Theory]
    [MemberData(nameof(Workflows))]
    public void No_workflow_runs_on_pull_request_target_or_workflow_run(string workflow)
    {
        var triggers = Yaml.Load(workflow).Keys("on");

        Assert.NotEmpty(triggers);
        Assert.DoesNotContain("pull_request_target", triggers);
        Assert.DoesNotContain("workflow_run", triggers);
    }

    [Theory]
    [MemberData(nameof(Workflows))]
    public void No_script_holds_an_expression_values_reach_scripts_through_the_environment(string workflow)
    {
        Assert.All(Steps(workflow).Select(step => step.Text("run")).OfType<string>(), script => Assert.DoesNotContain("${{", script, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Workflows))]
    public void Steps_use_GitHubs_own_actions_only_and_no_cache(string workflow)
    {
        var actions = Steps(workflow).Select(step => step.Text("uses")).OfType<string>().ToList();

        Assert.All(actions, action => Assert.True(
            action.StartsWith("actions/", StringComparison.Ordinal) || action.StartsWith("github/codeql-action/", StringComparison.Ordinal),
            action));
        Assert.DoesNotContain(actions, action => action.StartsWith("actions/cache", StringComparison.Ordinal));
        Assert.All(Steps(workflow), step => Assert.Null(step.Find("with", "cache")));
    }

    [Fact]
    public void The_scripts_import_no_build_cache()
    {
        Assert.All(Repository.FilesUnder("scripts/ci", "*.sh"), script =>
            Assert.DoesNotContain("--cache-from", Repository.Read(script), StringComparison.Ordinal));
    }

    [Fact]
    public void Ci_runs_on_pushes_and_pull_requests_to_main_by_hand_and_when_called_and_skips_its_image_job_only_when_told()
    {
        var ci = Yaml.Load(".github/workflows/ci.yml");

        Assert.Equal(["push", "pull_request", "workflow_dispatch", "workflow_call"], ci.Keys("on"));
        Assert.Equal(["main"], ci.Texts("on", "push", "branches"));
        Assert.Equal(["main"], ci.Texts("on", "pull_request", "branches"));
        Assert.Equal(["image"], ci.Keys("on", "workflow_call", "inputs"));
        Assert.Equal("boolean", ci.Text("on", "workflow_call", "inputs", "image", "type"));
        Assert.Equal("true", ci.Text("on", "workflow_call", "inputs", "image", "default"));

        // A push or a pull request has no input; toJSON tells that empty input from false, which loose equality cannot.
        Assert.Equal("toJSON(inputs.image) != 'false'", ci.Text("jobs", "image", "if"));
        Assert.Null(ci.Text("jobs", "lint", "if"));
        Assert.Null(ci.Text("jobs", "test", "if"));
    }

    [Fact]
    public void The_release_runs_only_on_a_pushed_version_tag_in_this_repository()
    {
        var release = Yaml.Load(".github/workflows/release.yml");

        Assert.Equal(["push"], release.Keys("on"));
        Assert.Equal(["tags"], release.Keys("on", "push"));
        Assert.Equal(["v[0-9]+.[0-9]+.[0-9]+", "v[0-9]+.[0-9]+.[0-9]+-*"], release.Texts("on", "push", "tags"));
        Assert.All(release.Entries("jobs"), job => Assert.Equal("github.repository == 'JJWren/Airlock-DotNet'", job.Value.Text("if")));
    }

    [Fact]
    public void The_release_checks_the_version_runs_ci_without_its_image_job_then_publishes()
    {
        var release = Yaml.Load(".github/workflows/release.yml");

        Assert.Equal(["version", "ci", "publish"], release.Keys("jobs"));
        Assert.Contains("bash scripts/ci/check-release.sh", Runs(release.Mapping("jobs", "version")));
        Assert.Equal("./.github/workflows/ci.yml", release.Text("jobs", "ci", "uses"));
        Assert.Equal("false", release.Text("jobs", "ci", "with", "image"));
        Assert.Equal(["version"], release.Texts("jobs", "ci", "needs"));
        Assert.Equal(["version", "ci"], release.Texts("jobs", "publish", "needs"));
    }

    [Fact]
    public void The_release_tags_last_after_it_tests_scans_pushes_attests_and_verifies_the_one_image()
    {
        var publish = Yaml.Load(".github/workflows/release.yml").Mapping("jobs", "publish");
        var steps = publish.Items("steps").Select(step => step.Text("run") ?? step.Text("uses") ?? string.Empty).ToList();
        int At(string fragment) => steps.FindIndex(step => step.Contains(fragment, StringComparison.Ordinal));

        int[] order =
        [
            At("build-image.sh load"), At("smoke-test.cs"), At("scan-image.sh"), At("docker login ghcr.io"),
            At("build-image.sh push"), At("actions/attest@"), steps.FindLastIndex(step => step.StartsWith("actions/attest@", StringComparison.Ordinal)),
            At("promote.sh verify"), At("promote.sh tag"),
        ];
        Assert.DoesNotContain(-1, order);
        Assert.Equal(order.Order(), order);

        var attestations = publish.Items("steps").Where(step => step.Text("uses")?.StartsWith("actions/attest@", StringComparison.Ordinal) == true).ToList();
        Assert.Equal(2, attestations.Count);
        Assert.All(attestations, attestation =>
        {
            Assert.Equal("ghcr.io/jjwren/airlock-dotnet", attestation.Text("with", "subject-name"));
            Assert.Equal("${{ steps.push.outputs.digest }}", attestation.Text("with", "subject-digest"));
            Assert.Equal("true", attestation.Text("with", "push-to-registry"));
            Assert.Equal("false", attestation.Text("with", "create-storage-record"));
        });
        Assert.Null(attestations[0].Find("with", "sbom-path"));
        Assert.Equal("${{ runner.temp }}/airlock-ci/sbom.spdx.json", attestations[1].Text("with", "sbom-path"));
    }

    private static IEnumerable<(string Id, YamlNode Node)> Jobs() =>
        Repository.Workflows.SelectMany(workflow => Yaml.Load(workflow).Entries("jobs")
            .Select(job => (Path.GetFileName(workflow) + "/" + job.Key, job.Value)));

    private static IEnumerable<YamlNode> Steps(string workflow) =>
        Yaml.Load(workflow).Entries("jobs").SelectMany(job => job.Value.Items("steps"));

    private static IEnumerable<string> Runs(YamlNode job) => job.Items("steps").Select(step => step.Text("run")).OfType<string>();

    private static List<string> Permissions(YamlNode node) =>
        [.. node.Entries("permissions").Select(permission => $"{permission.Key}: {(permission.Value as YamlScalarNode)?.Value}").Order(StringComparer.Ordinal)];
}
