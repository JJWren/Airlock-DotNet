using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Airlock.Web.Tests.Support;

namespace Airlock.Web.Tests.Host;

// DR-10, U6-NFR-11 — the image's health check: one GET of the health endpoint, 0 on a 200 and 1 otherwise, in one line.
public sealed class HealthProbeTests
{
    [Fact]
    public async Task A_200_answer_is_healthy()
    {
        using var stub = StubServer.Answering("200 OK");
        using var output = new StringWriter();

        var exitCode = await HealthProbe.RunAsync(stub.HealthUri, HealthProbe.Timeout, output).Within();

        Assert.Equal(0, exitCode);
        Assert.Equal("healthy" + Environment.NewLine, output.ToString());
        Assert.Equal("GET /api/v2/system/health HTTP/1.1", stub.RequestLine);
    }

    [Theory]
    [InlineData("503 Service Unavailable")]
    [InlineData("400 Bad Request")]
    [InlineData("302 Found")]
    public async Task Any_other_answer_is_unhealthy_and_named(string status)
    {
        using var stub = StubServer.Answering(status);
        using var output = new StringWriter();

        var exitCode = await HealthProbe.RunAsync(stub.HealthUri, HealthProbe.Timeout, output).Within();

        Assert.Equal(1, exitCode);
        Assert.Equal($"unhealthy: the health endpoint answered {status}" + Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task A_refused_connection_is_unhealthy()
    {
        var closed = new Uri($"http://127.0.0.1:{UnusedPort()}/api/v2/system/health");
        using var output = new StringWriter();

        // Windows answers a closed loopback port only after retrying, so this test waits longer than the image does.
        var exitCode = await HealthProbe.RunAsync(closed, TimeSpan.FromSeconds(20), output).Within();

        Assert.Equal(1, exitCode);
        var line = Assert.Single(Lines(output.ToString()));
        Assert.StartsWith("unhealthy: ", line, StringComparison.Ordinal);
        Assert.DoesNotContain("no answer within", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_answer_within_the_timeout_is_unhealthy()
    {
        using var stub = StubServer.Silent();
        using var output = new StringWriter();

        var exitCode = await HealthProbe.RunAsync(stub.HealthUri, TimeSpan.FromMilliseconds(300), output).Within();

        Assert.Equal(1, exitCode);
        Assert.Equal("unhealthy: no answer within 0.3 seconds" + Environment.NewLine, output.ToString());
    }

    [Theory]
    [InlineData(true, "--health-probe")]
    [InlineData(false)]
    [InlineData(false, "--health-probe", "--urls=http://127.0.0.1:0")]
    [InlineData(false, "--urls=http://127.0.0.1:0", "--health-probe")]
    [InlineData(false, "--Health-Probe")]
    public void Only_the_argument_alone_asks_for_the_probe(bool requested, params string[] args)
    {
        Assert.Equal(requested, HealthProbe.IsRequested(args));
    }

    [Fact]
    public void The_image_asks_localhost_8080_and_gives_up_after_3_seconds()
    {
        Assert.Equal("http://localhost:8080/api/v2/system/health", HealthProbe.HealthUri.ToString());
        Assert.Equal(TimeSpan.FromSeconds(3), HealthProbe.Timeout);
    }

    // B1: Program hands the argument over before it builds a host. Whatever answers on this machine's port 8080, if
    // anything does, the probe prints one line and exits with its verdict; a host would also have logged its start.
    [Fact]
    public async Task Airlock_Web_run_with_the_argument_prints_one_line_and_builds_no_host()
    {
        var start = new ProcessStartInfo(
            Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            [Path.Combine(AppContext.BaseDirectory, "Airlock.Web.dll"), HealthProbe.Argument])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // As the image runs it: without the profiler a coverage run attaches to this test process.
        foreach (var name in start.Environment.Keys.Where(IsProfilerVariable).ToList())
        {
            start.Environment.Remove(name);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start");
        var standardOutput = process.StandardOutput.ReadToEndAsync(Test.Token);
        var standardError = process.StandardError.ReadToEndAsync(Test.Token);
        await process.WaitForExitAsync(Test.Token).Within(30);

        var line = Assert.Single(Lines(await standardOutput));
        Assert.Matches("^(healthy|unhealthy: .+)$", line);
        Assert.Equal(line == "healthy" ? 0 : 1, process.ExitCode);
        Assert.Equal(string.Empty, await standardError);
    }

    private static bool IsProfilerVariable(string name) =>
        name.StartsWith("CORECLR_", StringComparison.OrdinalIgnoreCase) || name.StartsWith("COR_", StringComparison.OrdinalIgnoreCase);

    private static string[] Lines(string text) => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    private static int UnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>A one-request HTTP server on a loopback port of its own, answering with a fixed status, or never.</summary>
    private sealed class StubServer : IDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stopping = new();
        private readonly Task serving;

        private StubServer(string? status)
        {
            listener.Start();
            HealthUri = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/api/v2/system/health");
            serving = ServeAsync(status, stopping.Token);
        }

        public Uri HealthUri { get; }

        public string? RequestLine { get; private set; }

        public static StubServer Answering(string status) => new(status);

        public static StubServer Silent() => new(null);

        public void Dispose()
        {
            stopping.Cancel();
            listener.Stop();
            try
            {
                serving.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // The listener was stopped under a pending accept.
            }

            stopping.Dispose();
        }

        private async Task ServeAsync(string? status, CancellationToken cancellationToken)
        {
            try
            {
                using var client = await listener.AcceptTcpClientAsync(cancellationToken);
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                RequestLine = await reader.ReadLineAsync(cancellationToken);
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken)))
                {
                }

                if (status is null)
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken);
                }

                var answer = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(answer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
