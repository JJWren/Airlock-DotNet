using Airlock.Web;

// DR-10, U6-NFR-11: the image's health check runs this assembly too, and must not build a host.
if (HealthProbe.IsRequested(args))
{
    return await HealthProbe.RunAsync(Console.Out);
}

var builder = WebApplication.CreateBuilder(args);
AirlockHost.ConfigureServices(builder);
var app = builder.Build();
AirlockHost.ConfigurePipeline(app);
await app.RunAsync();
return 0;
