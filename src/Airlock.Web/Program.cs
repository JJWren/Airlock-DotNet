using Airlock.Web;

var builder = WebApplication.CreateBuilder(args);
AirlockHost.ConfigureServices(builder);

var app = builder.Build();
AirlockHost.ConfigurePipeline(app);
app.Run();
