using Airlock.Application.Ports;
using Airlock.Domain.Scanning;
using Airlock.Domain.Targets;
using Airlock.Infrastructure.Syft;
using Airlock.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Airlock.Infrastructure.Tests.Syft;

// IR-01 to IR-04 — one Syft run per call, with the stand-in program playing Syft.
public sealed class SyftSbomGeneratorTests : IDisposable
{
    private readonly TemporaryFolder folder = new();
    private readonly FakeTimeProvider clock = new(Test.StartTime);
    private readonly FakeLogger logger = new();

    private string StagingRoot => folder.Combine("staging");

    [Fact]
    public async Task A_directory_is_scanned_as_dir_with_JSON_output_quietly()
    {
        var target = folder.Combine("target");
        Directory.CreateDirectory(target);
        File.Copy(Fixtures.SyftOutput("manifest-file"), Path.Combine(target, "stand-in-output.json"));
        var argsFile = folder.Combine("args.txt");

        var outcome = await Generator(("STANDIN_ARGS_FILE", argsFile)).GenerateAsync(new DirectorySource(target), Test.Token).Within();

        Assert.Equal(4, Assert.IsType<SbomProduced>(outcome).Packages.Count);
        Assert.Equal(["scan", "dir:" + target, "-o", "json", "-q"], await File.ReadAllLinesAsync(argsFile, Test.Token));
    }

    [Fact]
    public async Task An_artifact_is_scanned_by_its_path_alone_so_Syft_detects_what_it_is()
    {
        var image = Artifact("app-image.tar", Fixtures.SyftOutput("image-file"));
        var argsFile = folder.Combine("args.txt");

        var outcome = await Generator(("STANDIN_ARGS_FILE", argsFile)).GenerateAsync(image, Test.Token).Within();

        var produced = Assert.IsType<SbomProduced>(outcome);
        Assert.True(produced.IsContainerImage);
        Assert.Equal(["scan", image.Path, "-o", "json", "-q"], await File.ReadAllLinesAsync(argsFile, Test.Token));
    }

    [Fact]
    public async Task A_non_zero_exit_fails_the_run_with_the_end_of_Syfts_errors()
    {
        var outcome = await Generator(("STANDIN_EXIT", "1"), ("STANDIN_STDERR", "could not determine source\n"))
            .GenerateAsync(Artifact("broken.tar", null), Test.Token).Within();

        var failure = Assert.IsType<SbomFailed>(outcome).Failure;
        Assert.Equal(ScanFailureCode.SyftFailed, failure.Code);
        Assert.Equal("Syft exited with code 1: could not determine source", failure.Reason);
        Assert.Equal("broken.tar", failure.Input);
    }

    [Fact]
    public async Task A_directory_runs_failure_leaves_its_input_to_the_Application()
    {
        var outcome = await Generator(("STANDIN_EXIT", "1")).GenerateAsync(new DirectorySource(folder.Path), Test.Token).Within();

        Assert.Null(Assert.IsType<SbomFailed>(outcome).Failure.Input);
    }

    [Fact]
    public async Task Output_that_is_not_Syft_JSON_is_unreadable()
    {
        var garbage = folder.WriteFile("garbage.json", "this is not JSON");

        var outcome = await Generator(("STANDIN_OUTPUT", garbage)).GenerateAsync(Artifact("thing.bin", null), Test.Token).Within();

        var failure = Assert.IsType<SbomFailed>(outcome).Failure;
        Assert.Equal(ScanFailureCode.SyftOutputUnreadable, failure.Code);
        Assert.StartsWith("Syft's output could not be read: ", failure.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_package_Airlock_cannot_represent_makes_the_output_unreadable()
    {
        var hostile = folder.WriteFile("hostile.json", "{\"artifacts\":[{\"type\":\"python\",\"name\":\"evil\\u001b[2J\",\"version\":\"1.0\"}]}");

        var outcome = await Generator(("STANDIN_OUTPUT", hostile)).GenerateAsync(Artifact("thing.bin", null), Test.Token).Within();

        var failure = Assert.IsType<SbomFailed>(outcome).Failure;
        Assert.Equal(ScanFailureCode.SyftOutputUnreadable, failure.Code);
        Assert.StartsWith("Syft's output holds a package Airlock cannot represent: ", failure.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_run_past_its_limit_is_killed_and_fails_naming_the_limit()
    {
        var run = Generator(("STANDIN_HANG", "1")).GenerateAsync(Artifact("slow.tar", null), Test.Token).AsTask();

        var outcome = await clock.DriveAsync(run, TimeSpan.FromMinutes(1));

        var failure = Assert.IsType<SbomFailed>(outcome).Failure;
        Assert.Equal(ScanFailureCode.SyftTimedOut, failure.Code);
        Assert.Equal("Syft did not finish within 15 minutes", failure.Reason);
        Assert.Contains(logger.Collector.GetSnapshot(), record => record.Message.Contains("the time limit passed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_cancelled_scan_kills_the_run_and_throws()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Test.Token);
        var run = Generator(("STANDIN_HANG", "1")).GenerateAsync(Artifact("slow.tar", null), cancel.Token).AsTask();
        await Task.Delay(500, Test.Token);

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5), Test.Token));
    }

    [Fact]
    public async Task A_missing_binary_fails_the_run()
    {
        var generator = new SyftSbomGenerator(
            new SyftLocator(folder.Combine("missing", SyftLocator.FileName), AppContext.BaseDirectory, null, File.Exists),
            new SyftEnvironment(null), TimeSpan.FromSeconds(30), StagingRoot, TimeSpan.FromMinutes(15), clock, logger);

        var outcome = await generator.GenerateAsync(new DirectorySource(folder.Path), Test.Token).Within();

        Assert.StartsWith("Syft could not be started: ", Assert.IsType<SbomFailed>(outcome).Failure.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Syfts_temporary_files_go_to_a_marked_folder_of_the_runs_own_that_is_deleted_afterwards()
    {
        var variablesFile = folder.Combine("args.txt");

        await Generator(("STANDIN_ARGS_FILE", variablesFile), ("STANDIN_OUTPUT", Fixtures.SyftOutput("jar-file")))
            .GenerateAsync(Artifact("log4j.jar", null), Test.Token).Within();

        Assert.True(Directory.Exists(StagingRoot));
        Assert.Empty(Directory.EnumerateFileSystemEntries(StagingRoot));
    }

    [Fact]
    public async Task A_NuGet_package_adds_the_package_its_nuspec_names_alongside_the_assemblies()
    {
        var nupkg = Artifact("Newtonsoft.Json.12.0.1.nupkg", Fixtures.SyftOutput("nupkg-dll-as-zip"), A.Zip(("Newtonsoft.Json.nuspec", A.Nuspec())));

        var produced = Assert.IsType<SbomProduced>(await Generator().GenerateAsync(nupkg, Test.Token).Within());

        Assert.Equal(["dotnet:System.Text.Json 10.0.11", "nuget:Newtonsoft.Json 12.0.1"], produced.Packages.Select(Describe));
        Assert.Empty(produced.Packages.Single(package => package.Package.Type == "nuget").CandidateCpes);
    }

    [Fact]
    public async Task A_NuGet_package_without_a_readable_nuspec_adds_nothing_and_is_logged()
    {
        var nupkg = Artifact("Broken.1.0.0.nupkg", Fixtures.SyftOutput("nupkg-dll-as-zip"), [1, 2, 3]);

        var produced = Assert.IsType<SbomProduced>(await Generator().GenerateAsync(nupkg, Test.Token).Within());

        Assert.Equal(["dotnet:System.Text.Json 10.0.11"], produced.Packages.Select(Describe));
        Assert.Contains(logger.Collector.GetSnapshot(), record => record.Message.Contains("Broken.1.0.0.nupkg", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_status_comes_from_the_probe()
    {
        var status = await Generator().GetStatusAsync(Test.Token).Within();

        Assert.True(status.Available);
        Assert.Equal("1.55.0", status.Version);
    }

    public void Dispose() => folder.Dispose();

    private static string Describe(SbomPackage package) => $"{package.Package.Type}:{package.Package}";

    private SyftSbomGenerator Generator(params (string Name, string Value)[] variables) =>
        new(
            new SyftLocator(StandIn.Path, AppContext.BaseDirectory, null, File.Exists),
            new SyftEnvironment(null, variables.ToDictionary(variable => variable.Name, variable => variable.Value, StringComparer.Ordinal)),
            TimeSpan.FromSeconds(30),
            StagingRoot,
            TimeSpan.FromMinutes(15),
            clock,
            logger);

    /// <summary>A staged artifact whose stand-in output is <paramref name="output"/>, if any.</summary>
    private FileSource Artifact(string name, string? output, byte[]? content = null)
    {
        var path = folder.Combine("staged", name + (name.EndsWith(".nupkg", StringComparison.Ordinal) ? ".zip" : string.Empty));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content ?? [0]);
        if (output is not null)
        {
            File.Copy(output, path + ".stand-in-output.json");
        }

        return new FileSource(path, new Artifact(name, content?.Length ?? 1));
    }
}
