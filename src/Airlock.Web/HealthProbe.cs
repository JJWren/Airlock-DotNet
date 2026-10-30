using System.Globalization;
using System.Net;

namespace Airlock.Web;

/// <summary>
/// The image's health check, run as <c>dotnet Airlock.Web.dll --health-probe</c> because the runtime image has no shell
/// (DR-10, U6-NFR-11). It sends one GET to the health endpoint and exits 0 on a 200, DEGRADED included, and 1 on any
/// other answer, an error or the timeout. It builds no host, configuration, logging or services, so it writes nothing to
/// Airlock's logs or the staging root; its one line goes to Docker's health record.
/// </summary>
internal static class HealthProbe
{
    public const string Argument = "--health-probe";

    /// <summary>Where Airlock listens in the container; <c>localhost</c> is among the default <c>AllowedHosts</c>.</summary>
    public static readonly Uri HealthUri = new("http://localhost:8080/api/v2/system/health");

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    /// <summary>Only the argument alone asks for the probe, so no command line meant for the host can.</summary>
    public static bool IsRequested(string[] args) => args is [Argument];

    public static Task<int> RunAsync(TextWriter output) => RunAsync(HealthUri, Timeout, output);

    public static async Task<int> RunAsync(Uri healthUri, TimeSpan timeout, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(healthUri);
        ArgumentNullException.ThrowIfNull(output);

        string? problem;
        try
        {
            // Loopback only: no proxy a container's environment names, and no redirect to follow.
            using var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = timeout };
            using var response = await client.GetAsync(healthUri, HttpCompletionOption.ResponseHeadersRead);
            problem = response.StatusCode == HttpStatusCode.OK
                ? null
                : $"the health endpoint answered {(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd();
        }
        catch (TaskCanceledException)
        {
            problem = string.Create(CultureInfo.InvariantCulture, $"no answer within {timeout.TotalSeconds:0.###} seconds");
        }
        catch (HttpRequestException exception)
        {
            problem = exception.Message.ReplaceLineEndings(" ");
        }

        await output.WriteLineAsync(problem is null ? "healthy" : "unhealthy: " + problem);
        return problem is null ? 0 : 1;
    }
}
