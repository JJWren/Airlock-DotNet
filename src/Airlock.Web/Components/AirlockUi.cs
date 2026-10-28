using Airlock.Web.Interop;
using Airlock.Web.Security;
using Airlock.Web.State;
using MudBlazor;
using MudBlazor.Services;

namespace Airlock.Web.Components;

/// <summary>The Blazor UI's registrations and its place in the host's pipeline, beside the API's (U5 logical components).</summary>
internal static class AirlockUi
{
    /// <summary>How long a success or information snackbar stays; errors stay until closed (U5 NFR Design values).</summary>
    public const int SnackbarMilliseconds = 6_000;

    public static IServiceCollection AddAirlockUi(this IServiceCollection services, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);

        // Blazor's CircuitOptions keep their defaults: 3 minutes and 100 disconnected circuits (U5-NFR-03).
        services.AddRazorComponents()
            .AddInteractiveServerComponents(circuit => circuit.DetailedErrors = environment.IsDevelopment());
        services.AddMudServices(mud =>
        {
            mud.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
            mud.SnackbarConfiguration.VisibleStateDuration = SnackbarMilliseconds;
            mud.SnackbarConfiguration.ShowCloseIcon = true;
        });
        services.AddScoped<AirlockJs>();
        services.AddScoped<ScanSession>();
        return services;
    }

    // U5 logical components, step 1: the pages' headers and error handling, ahead of U4's /api branch rather than in a
    // branch of their own, since re-executing a page needs the application's router, which WebApplication withholds from
    // branches. Under /api the API's problems are written first, inside, so these find nothing left to do.
    public static WebApplication UseAirlockPages(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseMiddleware<PageSecurityHeaders>();
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/error", createScopeForErrors: true);
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        return app;
    }

    // Step 5: mapped last, after the API and its documentation (U4 hand-off).
    public static WebApplication MapAirlockUi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
        return app;
    }
}