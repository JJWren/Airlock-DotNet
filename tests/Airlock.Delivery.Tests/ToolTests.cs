using Airlock.Delivery.Tests.Support;

namespace Airlock.Delivery.Tests;

// U6-NFR-15 — each CI tool's version is written in one place: Grype, actionlint, zizmor and BuildKit in
// .github/ci-tools/Dockerfile, Syft in the Dockerfile (DR-04 checks its other places). The workflows and scripts name no
// tool image themselves; they reach the tools through scripts/ci/run-tool.sh.
public sealed class ToolTests
{
    private static readonly string[] Images = ["anchore/grype", "rhysd/actionlint", "zizmorcore/zizmor", "moby/buildkit", "anchore/syft"];

    [Fact]
    public void The_CI_tools_file_pins_the_four_tools_each_in_a_stage_of_its_own()
    {
        var stages = DockerfileModel.Stages(".github/ci-tools/Dockerfile");

        Assert.Equal(["grype", "actionlint", "zizmor", "buildkit"], stages.Select(stage => stage.Name));
        Assert.Equal(["anchore/grype", "rhysd/actionlint", "zizmorcore/zizmor", "moby/buildkit"], stages.Select(stage => Repo(stage.Image)));
        Assert.All(stages, stage => Assert.Empty(stage.Instructions));
    }

    [Fact]
    public void No_workflow_or_script_names_a_tool_image()
    {
        var files = Repository.Workflows.Concat(Repository.FilesUnder("scripts", "*.*"));

        // An image is named by its repository and a tag or a digest; the install scripts' release URLs are not images.
        Assert.All(files, file =>
        {
            var text = Repository.Read(file);
            Assert.All(Images, image =>
            {
                Assert.DoesNotContain(image + ":", text, StringComparison.Ordinal);
                Assert.DoesNotContain(image + "@", text, StringComparison.Ordinal);
            });
        });
    }

    [Fact]
    public void The_workflows_run_the_linters_and_scanners_through_the_tool_runner()
    {
        var runs = Repository.Workflows.SelectMany(workflow => Yaml.Load(workflow).Entries("jobs"))
            .SelectMany(job => job.Value.Items("steps"))
            .Select(step => step.Text("run"))
            .OfType<string>()
            .ToList();

        Assert.Contains(runs, run => run.StartsWith("bash scripts/ci/run-tool.sh actionlint", StringComparison.Ordinal));
        Assert.Contains(runs, run => run.StartsWith("bash scripts/ci/run-tool.sh shellcheck", StringComparison.Ordinal));
        Assert.Contains(runs, run => run.StartsWith("bash scripts/ci/run-tool.sh zizmor", StringComparison.Ordinal));
        Assert.DoesNotContain(runs, run => run.Contains("docker run", StringComparison.Ordinal));
    }

    // The image's repository: its name without the registry, the tag and the digest.
    private static string Repo(string image)
    {
        var name = image.Split('@')[0];
        name = name[..name.LastIndexOf(':')];
        return name.Count(character => character == '/') > 1 || name.Split('/')[0].Contains('.', StringComparison.Ordinal)
            ? name[(name.IndexOf('/', StringComparison.Ordinal) + 1)..]
            : name;
    }
}
