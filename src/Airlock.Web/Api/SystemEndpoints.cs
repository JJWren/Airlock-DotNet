using Airlock.Application.UseCases;
using Airlock.Web.Api.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Airlock.Web.Api;

/// <summary>Emergency Stop and health (WR-16, WR-17).</summary>
internal static class SystemEndpoints
{
    /// <summary>Airlock's version, as the original's health answer gave its own (FD decision 17).</summary>
    public static string Version { get; } = typeof(SystemEndpoints).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static RouteGroupBuilder MapSystemEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/system/stop", StopAsync)
            .WithName("EmergencyStop")
            .WithSummary("Emergency Stop")
            .WithDescription("Cancels whatever holds the active slot, whoever started it: TERMINATED with its id, or IDLE.")
            .AsStateChanging();

        api.MapGet("/system/health", GetHealthAsync)
            .WithName("GetHealth")
            .WithSummary("Readiness")
            .WithDescription("Always 200 while Airlock runs; the status is ONLINE when Syft is available and an NVD key is configured, DEGRADED otherwise. NVD is never called.");

        return api;
    }

    // WR-16
    private static async Task<Ok<EmergencyStopDto>> StopAsync(EmergencyStop emergencyStop, CancellationToken cancellationToken)
    {
        var stopped = await emergencyStop.ExecuteAsync(cancellationToken);
        return TypedResults.Ok(new EmergencyStopDto(
            stopped.Stopped ? StopStatusValue.Terminated : StopStatusValue.Idle, stopped.ScanId?.ToString()));
    }

    // WR-17
    private static async Task<Ok<SystemHealthDto>> GetHealthAsync(GetSystemHealth getSystemHealth, CancellationToken cancellationToken)
    {
        var health = await getSystemHealth.ExecuteAsync(cancellationToken);
        return TypedResults.Ok(new SystemHealthDto(
            health.Status == HealthStatus.Online ? HealthStatusValue.Online : HealthStatusValue.Degraded,
            Version,
            [.. health.Checks.Select(check => new HealthCheckDto(check.Name, check.Passing, check.Detail))]));
    }
}
