using Airlock.Infrastructure.Syft;
using Airlock.Infrastructure.Tests.Support;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Airlock.Infrastructure.Tests.Syft;

// IR-05 — Syft's status, checked on first use and cached; IR-06 — where Syft is looked for.
public sealed class SyftToolStatusTests
{
    private readonly FakeTimeProvider clock = new(Test.StartTime);
    private readonly FakeLogger logger = new();

    [Fact]
    public async Task Syft_that_answers_its_version_is_available()
    {
        var status = await Probe(StandIn.Path).GetAsync(Test.Token).Within();

        Assert.True(status.Available);
        Assert.Equal("1.55.0", status.Version);
        Assert.Equal("Syft 1.55.0 at " + StandIn.Path, status.Detail);
    }

    [Fact]
    public async Task Syft_that_is_not_there_is_reported_with_where_it_was_looked_for()
    {
        using var folder = new TemporaryFolder();
        var missing = folder.Combine(SyftLocator.FileName);

        var status = await Probe(missing).GetAsync(Test.Token).Within();

        Assert.False(status.Available);
        Assert.Equal("Syft not found at " + missing, status.Detail);
        Assert.Contains(logger.Collector.GetSnapshot(), record => record.Message.Contains("Syft is not available", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Syft_that_fails_its_version_did_not_run()
    {
        var status = await Probe(StandIn.Path, ("STANDIN_EXIT", "2"), ("STANDIN_STDERR", "boom")).GetAsync(Test.Token).Within();

        Assert.False(status.Available);
        Assert.Equal($"Syft at {StandIn.Path} did not run: exit code 2: boom", status.Detail);
    }

    [Fact]
    public async Task A_file_that_is_not_a_program_did_not_run()
    {
        using var folder = new TemporaryFolder();
        var notAProgram = folder.WriteFile(SyftLocator.FileName, "just text");

        var status = await Probe(notAProgram).GetAsync(Test.Token).Within();

        Assert.False(status.Available);
        Assert.StartsWith($"Syft at {notAProgram} did not run: ", status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Syft_that_does_not_answer_within_10_seconds_did_not_run()
    {
        var probe = Probe(StandIn.Path, ("STANDIN_HANG", "1"));

        var status = await clock.DriveAsync(probe.GetAsync(Test.Token), TimeSpan.FromSeconds(1));

        Assert.False(status.Available);
        Assert.EndsWith("did not run: it did not answer within 10 seconds", status.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_status_is_cached_for_its_duration_and_shared_by_concurrent_callers()
    {
        var probes = 0;
        var locator = new SyftLocator(StandIn.Path, AppContext.BaseDirectory, null, path =>
        {
            Interlocked.Increment(ref probes);
            return File.Exists(path);
        });
        var probe = new SyftStatusProbe(
            locator, new SyftProcessRunner(clock), new SyftEnvironment(null), TimeSpan.FromSeconds(30), clock, logger);

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => probe.GetAsync(Test.Token)));
        await probe.GetAsync(Test.Token).Within();
        Assert.Equal(1, probes);

        clock.Advance(TimeSpan.FromSeconds(31));
        await probe.GetAsync(Test.Token).Within();
        Assert.Equal(2, probes);
    }

    // U6-NFR-11: the image's health check re-checks Syft every 30 seconds, and only a change of status is logged.
    [Fact]
    public async Task A_recheck_logs_only_a_change_of_status()
    {
        var present = true;
        var locator = new SyftLocator(StandIn.Path, AppContext.BaseDirectory, null, path => present && File.Exists(path));
        var probe = new SyftStatusProbe(
            locator, new SyftProcessRunner(clock), new SyftEnvironment(null), TimeSpan.FromSeconds(30), clock, logger);

        await probe.GetAsync(Test.Token).Within();
        clock.Advance(TimeSpan.FromSeconds(31));
        await probe.GetAsync(Test.Token).Within();
        Assert.Equal(1, logger.Collector.Count);

        present = false;
        clock.Advance(TimeSpan.FromSeconds(31));
        await probe.GetAsync(Test.Token).Within();
        clock.Advance(TimeSpan.FromSeconds(31));
        await probe.GetAsync(Test.Token).Within();
        Assert.Equal(2, logger.Collector.Count);
        Assert.Contains("Syft is not available", logger.Collector.LatestRecord.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_callers_cancellation_ends_only_its_own_wait()
    {
        var probe = Probe(StandIn.Path, ("STANDIN_HANG", "1"));
        using var impatient = new CancellationTokenSource();
        var waiting = probe.GetAsync(impatient.Token);

        await impatient.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        var status = await clock.DriveAsync(probe.GetAsync(Test.Token), TimeSpan.FromSeconds(1));
        Assert.False(status.Available);
    }

    [Theory]
    [InlineData("Application:   syft\nVersion:       1.55.0\nPlatform: windows/amd64", "1.55.0")]
    [InlineData("version: 1.2.3\r\n", "1.2.3")]
    [InlineData("Version:\n", null)]
    [InlineData("nothing useful", null)]
    [InlineData(null, null)]
    public void The_version_is_read_from_the_Version_line(string? output, string? version)
    {
        Assert.Equal(version, SyftStatusProbe.VersionFrom(output));
    }

    [Fact]
    public void A_configured_path_is_taken_from_the_applications_folder_when_relative()
    {
        using var folder = new TemporaryFolder();
        var binary = folder.WriteFile(Path.Combine("tools", SyftLocator.FileName), "x");

        var location = new SyftLocator(Path.Combine("tools", SyftLocator.FileName), folder.Path, null, File.Exists).Locate();

        Assert.Equal(binary, location.Path);
        Assert.True(location.Exists);
    }

    [Fact]
    public void A_configured_folder_means_the_binary_inside_it()
    {
        using var folder = new TemporaryFolder();
        var binary = folder.WriteFile(SyftLocator.FileName, "x");

        var location = new SyftLocator(folder.Path, AppContext.BaseDirectory, null, File.Exists).Locate();

        Assert.Equal(binary, location.Path);
    }

    [Fact]
    public void A_configured_path_that_does_not_exist_is_reported_as_it_is()
    {
        using var folder = new TemporaryFolder();
        var missing = folder.Combine("nowhere", SyftLocator.FileName);

        var location = new SyftLocator(missing, AppContext.BaseDirectory, Path.GetDirectoryName(StandIn.Path), File.Exists).Locate();

        Assert.Equal(missing, location.Path);
        Assert.False(location.Exists);
    }

    [Fact]
    public void Without_a_configured_path_the_first_full_folder_on_the_path_wins_and_relative_ones_are_skipped()
    {
        using var first = new TemporaryFolder();
        using var second = new TemporaryFolder();
        var expected = second.WriteFile(SyftLocator.FileName, "x");
        first.WriteFile(Path.Combine("relative", SyftLocator.FileName), "x");
        var searchPath = string.Join(Path.PathSeparator, ".", "relative", first.Path, "\"" + second.Path + "\"");

        var location = new SyftLocator(null, first.Path, searchPath, File.Exists).Locate();

        Assert.Equal(expected, location.Path);
    }

    [Fact]
    public void Without_Syft_on_the_path_it_is_looked_for_in_bin_next_to_the_application()
    {
        using var folder = new TemporaryFolder();
        var bundled = folder.WriteFile(Path.Combine("bin", SyftLocator.FileName), "x");

        var location = new SyftLocator(null, folder.Path, string.Empty, File.Exists).Locate();

        Assert.Equal(bundled, location.Path);
        Assert.True(location.Exists);
    }

    private SyftStatusProbe Probe(string binary, params (string Name, string Value)[] variables) =>
        new(
            new SyftLocator(binary, AppContext.BaseDirectory, null, File.Exists),
            new SyftProcessRunner(clock),
            new SyftEnvironment(null, variables.ToDictionary(variable => variable.Name, variable => variable.Value, StringComparer.Ordinal)),
            TimeSpan.FromSeconds(30),
            clock,
            logger);
}
