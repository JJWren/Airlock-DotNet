using System.Globalization;
using Airlock.Infrastructure.Syft;
using Airlock.Infrastructure.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Airlock.Infrastructure.Tests.Syft;

// IR-01, IR-02, U3-NFR-04 — the runner against the stand-in program; defect 3 (the whole tree is killed).
public sealed class SyftProcessRunnerTests
{
    private readonly SyftProcessRunner runner = new(TimeProvider.System);

    [Fact]
    public async Task Output_goes_to_the_reader_and_the_exit_code_and_errors_are_kept()
    {
        using var folder = new TemporaryFolder();
        var output = folder.WriteFile("out.json", "{\"artifacts\":[]}");

        var result = await runner.RunAsync(
            StandIn.Request("scan", null, ("STANDIN_OUTPUT", output), ("STANDIN_EXIT", "3"), ("STANDIN_STDERR", "could not determine source")),
            ReadAllAsync,
            Test.Token).Within();

        Assert.Equal(SyftRunEnd.Exited, result.End);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal("{\"artifacts\":[]}", result.Output);
        Assert.Equal("could not determine source", result.ErrorTail);
    }

    [Fact]
    public async Task Arguments_arrive_as_a_list_without_a_shell()
    {
        string[] arguments = ["args", "dir:/uploads/a folder with spaces", "-o", "json", "&& echo injected", "\"quoted\""];

        var result = await runner.RunAsync(new SyftRunRequest(StandIn.Path, arguments, TimeSpan.FromMinutes(1)), ReadAllAsync, Test.Token).Within();

        Assert.Equal(arguments[1..], Lines(result.Output));
    }

    [Fact]
    public async Task The_environment_turns_the_update_check_off_and_points_temporary_files_at_the_runs_folder()
    {
        using var folder = new TemporaryFolder();
        var request = new SyftEnvironment(null).Request(StandIn.Path, ["env"], TimeSpan.FromMinutes(1), folder.Path);

        var variables = Lines((await runner.RunAsync(request, ReadAllAsync, Test.Token).Within()).Output);

        Assert.Contains("SYFT_CHECK_FOR_APP_UPDATE=false", variables);
        Assert.Contains("TMPDIR=" + folder.Path, variables);
        Assert.Contains("TMP=" + folder.Path, variables);
        Assert.Contains("TEMP=" + folder.Path, variables);
    }

    [Fact]
    public async Task Hidden_variables_are_removed_by_name_and_by_value()
    {
        var name = "AIRLOCK_TEST_" + Guid.NewGuid().ToString("N");
        var secret = "secret-" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name + "_BY_NAME", "hidden by its name");
        Environment.SetEnvironmentVariable(name + "_BY_VALUE", secret);
        try
        {
            var request = new SyftRunRequest(StandIn.Path, ["env"], TimeSpan.FromMinutes(1))
            {
                HiddenNames = [name.ToLowerInvariant() + "_by_name"],
                HiddenValues = [secret],
            };

            var output = (await runner.RunAsync(request, ReadAllAsync, Test.Token).Within()).Output!;

            Assert.DoesNotContain(secret, output, StringComparison.Ordinal);
            Assert.DoesNotContain(name + "_BY_NAME", output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("PATH", output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name + "_BY_NAME", null);
            Environment.SetEnvironmentVariable(name + "_BY_VALUE", null);
        }
    }

    [Fact]
    public async Task Only_the_last_4_KB_of_standard_error_are_kept()
    {
        var result = await runner.RunAsync(
            StandIn.Request("scan", null, ("STANDIN_STDERR_KB", "1024"), ("STANDIN_EXIT", "1")), ReadAllAsync, Test.Token).Within();

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(SyftProcessRunner.ErrorTailLength, result.ErrorTail.Length);
        Assert.EndsWith("END-OF-ERRORS", result.ErrorTail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reader_that_stops_early_never_leaves_Syft_blocked_on_a_full_pipe()
    {
        using var deadline = Test.Deadline(30);

        var result = await runner.RunAsync(
            StandIn.Request("flood", null, ("STANDIN_FLOOD_MB", "64")),
            async (stream, token) =>
            {
                var first = new byte[1];
                await stream.ReadExactlyAsync(first, token);
                return "one byte";
            },
            deadline.Token).Within();

        Assert.Equal(SyftRunEnd.Exited, result.End);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Cancelling_kills_the_whole_process_tree_within_a_second()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Test.Token);
        var ids = new TaskCompletionSource<(int Parent, int Child)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = runner.RunAsync(StandIn.Request("tree"), ReadIdsAsync(ids), cancel.Token);
        var (parent, child) = await ids.Task.WaitAsync(TimeSpan.FromSeconds(30), Test.Token);
        Assert.True(StandIn.IsRunning(child));

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(1), Test.Token));
        Assert.True(await StandIn.IsGoneWithinAsync(parent, TimeSpan.FromSeconds(1)));
        Assert.True(await StandIn.IsGoneWithinAsync(child, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task The_time_limit_kills_the_tree_and_is_reported_as_such()
    {
        var clock = new FakeTimeProvider(Test.StartTime);
        var limited = new SyftProcessRunner(clock);
        var ids = new TaskCompletionSource<(int Parent, int Child)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = limited.RunAsync(StandIn.Request("tree", TimeSpan.FromMinutes(15)), ReadIdsAsync(ids), Test.Token);
        var (parent, child) = await ids.Task.WaitAsync(TimeSpan.FromSeconds(30), Test.Token);

        clock.Advance(TimeSpan.FromMinutes(15));
        var result = await run.WaitAsync(TimeSpan.FromSeconds(1), Test.Token);

        Assert.Equal(SyftRunEnd.TimedOut, result.End);
        Assert.True(await StandIn.IsGoneWithinAsync(parent, TimeSpan.FromSeconds(1)));
        Assert.True(await StandIn.IsGoneWithinAsync(child, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task A_binary_that_cannot_start_is_reported_rather_than_thrown()
    {
        using var folder = new TemporaryFolder();

        var result = await runner.RunAsync(
            new SyftRunRequest(folder.Combine(SyftLocator.FileName), ["version"], TimeSpan.FromMinutes(1)), ReadAllAsync, Test.Token).Within();

        Assert.Equal(SyftRunEnd.NotStarted, result.End);
        Assert.False(string.IsNullOrWhiteSpace(result.StartFailure));
    }

    [Fact]
    public async Task A_run_that_is_already_cancelled_does_not_start()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(StandIn.Request("hang"), ReadAllAsync, cancelled.Token));
    }

    private static async Task<string> ReadAllAsync(Stream output, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(output, leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static string[] Lines(string? text) =>
        [.. (text ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.TrimEnd('\r'))];

    private static Func<Stream, CancellationToken, Task<string>> ReadIdsAsync(TaskCompletionSource<(int Parent, int Child)> ids) =>
        async (output, cancellationToken) =>
        {
            using var reader = new StreamReader(output, leaveOpen: true);
            var line = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;
            var parts = line.Split(' ');
            ids.TrySetResult((int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture)));
            return line;
        };
}
