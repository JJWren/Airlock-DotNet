using System.Net;
using Airlock.Web.Components.Layout;
using Airlock.Web.Components.Pages;
using Airlock.Web.Security;
using Airlock.Web.Tests.Support;
using Bunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Airlock.Web.Tests.Ui;

// UR-39, U5-NFR-12: unknown addresses answer 404 with "Page not found"; an unexpected error shows "Something went
// wrong" with Reload, never the exception, and is logged once at Error.
public sealed class ErrorPageTests
{
    [Theory]
    [InlineData("/nowhere")]
    [InlineData("/reports/not-a-guid")]
    public async Task An_unknown_address_answers_404_with_the_page_shell(string address)
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(address, Test.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("_framework/blazor.web", await response.Content.ReadAsStringAsync(Test.Token), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_not_found_page_offers_the_way_back()
    {
        await using var ui = new UiTestContext();

        var page = ui.Render<NotFound>();

        Assert.Equal("Page not found", page.Find("h1").TextContent);
        Assert.Equal("/", page.Find("a").GetAttribute("href"));
    }

    [Fact]
    public async Task A_failing_page_shows_something_went_wrong_and_logs_one_error()
    {
        await using var ui = new UiTestContext();

        var boundary = ui.Render<AirlockErrorBoundary>(parameters => parameters.AddChildContent<FailingPage>());

        Assert.Equal("Something went wrong", boundary.Find("[role='alert'] h1").TextContent);
        Assert.Contains("Reload", boundary.Find("[role='alert'] button").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(FailingPage.Secret, boundary.Markup, StringComparison.Ordinal);
        var error = Assert.Single(ui.Logs.GetSnapshot(), record => record.Level >= LogLevel.Error);
        Assert.Equal(typeof(AirlockErrorBoundary).FullName, error.Category);
        Assert.Equal(3401, error.Id.Id);
    }

    [Fact]
    public async Task An_error_outside_a_page_is_drawn_by_the_server_without_its_details()
    {
        using var staging = new TemporaryFolder();
        await using var app = await ComposeAsync(staging.Path);
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/boom", Test.Token);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var page = await response.Content.ReadAsStringAsync(Test.Token);
        Assert.Contains("Something went wrong", page, StringComparison.Ordinal);
        Assert.Contains("href=\"/boom\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain(FailingPage.Secret, page, StringComparison.Ordinal);
        Assert.DoesNotContain("<!--Blazor:", page, StringComparison.Ordinal);
        Assert.Equal(PageSecurityHeaders.ContentSecurityPolicy, response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Theory]
    [InlineData("/reports/01a1637b-7e00-7c3a-9f12-6b84d0e5a1c7", "/reports/01a1637b-7e00-7c3a-9f12-6b84d0e5a1c7")]
    [InlineData("//evil.example/boom", "/")]
    [InlineData("/\\evil.example/boom", "/")]
    public async Task Reload_asks_for_the_failed_page_only_when_it_is_airlocks_own(string failed, string href)
    {
        await using var ui = new UiTestContext();
        var context = new DefaultHttpContext();
        context.Features.Set<IExceptionHandlerPathFeature>(new ExceptionHandlerFeature { Path = failed, Error = new InvalidOperationException() });

        var page = ui.Render<Error>(parameters => parameters.AddCascadingValue<HttpContext>(context));

        Assert.Equal("Something went wrong", page.Find("h1").TextContent);
        Assert.Equal(href, page.Find("a").GetAttribute("href"));
    }

    // The real composition in Production on a test server, with an address of the test's own that fails outside /api.
    private static async Task<WebApplication> ComposeAsync(string stagingRoot)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = "Production",
            Args = [$"--Airlock:Staging:Root={stagingRoot}"],
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        AirlockHost.ConfigureServices(builder);
        var app = builder.Build();
        AirlockHost.ConfigurePipeline(app);
        app.MapGet("/boom", (HttpContext _) => throw new InvalidOperationException(FailingPage.Secret));
        await app.StartAsync(Test.Token);
        return app;
    }

    private sealed class FailingPage : ComponentBase
    {
        public const string Secret = "secret-looking detail at C:/internal/airlock/staging";

        protected override void OnInitialized() => throw new InvalidOperationException(Secret);
    }
}