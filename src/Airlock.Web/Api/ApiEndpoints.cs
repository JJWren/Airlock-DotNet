using Airlock.Web.Api.Docs;
using Airlock.Web.Api.Problems;
using Airlock.Web.Api.Security;
using Airlock.Web.Api.Uploads;
using Airlock.Web.Api.Contracts;

namespace Airlock.Web.Api;

/// <summary>The <c>/api/v2</c> surface (WR-01) and what it needs from the host.</summary>
internal static class ApiEndpoints
{
    public const string ApiPrefix = "/api";
    public const string GroupPrefix = "/api/v2";

    public static IServiceCollection AddAirlockApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.ConfigureHttpJsonOptions(options => ApiJson.Configure(options.SerializerOptions));
        services.AddProblemDetails(options => options.CustomizeProblemDetails = ApiProblems.Customize);
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddSingleton<MultipartScanReader>();
        services.AddAirlockOpenApi();
        return services;
    }

    // logical-components.md: a branch for /api with the response headers, the exception handler and the status-code
    // pages, so API errors are problems in every environment while the UI keeps its own error handling (U5).
    public static WebApplication UseAirlockApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseWhen(
            context => context.Request.Path.StartsWithSegments(ApiPrefix),
            api =>
            {
                api.UseMiddleware<ApiResponseHeaders>();
                api.UseExceptionHandler();
                api.UseStatusCodePages();
            });
        return app;
    }

    public static IEndpointRouteBuilder MapAirlockApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var api = endpoints.MapGroup(GroupPrefix);
        api.MapScanEndpoints();
        api.MapSystemEndpoints();
        api.MapLogEndpoints();
        return endpoints;
    }

    /// <summary>WR-19, WR-21: the cross-site check, and no antiforgery token, on every <c>POST</c>.</summary>
    public static RouteHandlerBuilder AsStateChanging(this RouteHandlerBuilder endpoint) =>
        endpoint.AddEndpointFilter<CrossSiteRequestFilter>().DisableAntiforgery().ProducesProblem(StatusCodes.Status403Forbidden);
}
