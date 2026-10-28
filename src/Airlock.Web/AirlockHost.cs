using Airlock.Application;
using Airlock.Application.Options;
using Airlock.Infrastructure;
using Airlock.Web.Api;
using Airlock.Web.Api.Docs;
using Airlock.Web.Components;

namespace Airlock.Web;

/// <summary>
/// The composition root: the only Web code that names Infrastructure types (WR-23, architecture rule 5). Program calls
/// it; the order of everything is <c>logical-components.md</c>'s.
/// </summary>
internal static class AirlockHost
{
    public static void ConfigureServices(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Infrastructure Design: the original's variable names rank above the Airlock__* variables, below the command line.
        builder.Configuration.AddAirlockEnvironmentVariables();
        ConfigurationOrder.MoveLastBeforeCommandLine(builder.Configuration.Sources);

        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Limits.MaxRequestBodySize = ApiLimits.ServerBodyLimit;
        });

        builder.Services.AddOptions<AirlockOptions>().BindConfiguration(AirlockOptions.SectionName);
        builder.Services.AddAirlockApplication();
        builder.Services.AddAirlockInfrastructure(builder.Configuration);
        builder.Services.AddAirlockApi();
        builder.Services.AddAirlockUi(builder.Environment);
    }

    public static void ConfigurePipeline(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseAirlockPages();
        app.UseAirlockApi();
        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapAirlockApi();
        app.MapAirlockDocs();
        app.MapAirlockUi();
    }
}
