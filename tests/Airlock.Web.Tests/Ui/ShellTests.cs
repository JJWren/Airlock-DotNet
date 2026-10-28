using Airlock.Web.Components.Layout;
using Airlock.Web.Components.Pages;
using Airlock.Web.Tests.Support;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Airlock.Web.Tests.Ui;

// UR-01, UR-02: the drawer, Execute Audit, and the top bar's placeholders, which are inert (defect 16).
public sealed class ShellTests
{
    [Theory]
    [InlineData("", "Control Center")]
    [InlineData("logs", "Active Logs")]
    [InlineData("reports", "Reports")]
    [InlineData("reports/01a1637b-7e00-7c3a-9f12-6b84d0e5a1c7", "Reports")]
    public async Task The_current_item_is_highlighted_on_each_address(string address, string item)
    {
        await using var ui = new UiTestContext();
        ui.Services.GetRequiredService<NavigationManager>().NavigateTo(address);

        var menu = ui.Render<NavMenu>();

        var active = Assert.Single(menu.FindAll("a.mud-nav-link.active"));
        Assert.Equal(item, active.TextContent.Trim());
        Assert.Equal(["Control Center", "Active Logs", "Reports"], menu.FindAll("a.mud-nav-link").Select(link => link.TextContent.Trim()));
    }

    [Fact]
    public async Task Execute_audit_opens_control_center_and_starts_nothing()
    {
        await using var ui = new UiTestContext();

        var menu = ui.Render<NavMenu>();

        var execute = menu.Find(".airlock-drawer__footer a");
        Assert.Equal("Execute Audit", execute.TextContent.Trim());
        Assert.Equal("/", execute.GetAttribute("href"));
        Assert.Empty(ui.Module.Invocations);
    }

    [Fact]
    public async Task The_placeholders_change_neither_the_address_nor_what_is_shown()
    {
        await using var ui = new UiTestContext();
        ui.PlanStart();
        var navigation = ui.Services.GetRequiredService<BunitNavigationManager>();
        var layout = ui.Render<MainLayout>(parameters => parameters.Add(page => page.Body, (RenderFragment)(body => body.AddContent(0, "The page"))));
        ui.Session.SetDirectoryPath("/uploads/acme-app");
        var before = navigation.Uri;

        foreach (var name in new[] { "Notifications", "Settings", "Profile" })
        {
            var button = layout.Find($"button[aria-label='{name}']");
            Assert.Equal("button", button.GetAttribute("type"));
            button.Click();
        }

        Assert.Equal(before, navigation.Uri);
        Assert.Empty(navigation.History);
        Assert.Contains("The page", layout.Markup, StringComparison.Ordinal);
        Assert.Equal("/uploads/acme-app", ui.Session.State.DirectoryPath);
    }

    [Fact]
    public async Task No_page_loads_anything_from_another_host()
    {
        await using var factory = new AirlockWebFactory();
        using var client = factory.CreateClient();
        var shell = await client.GetStringAsync("/", Test.Token);

        await using var ui = new UiTestContext();
        ui.PlanStart();
        ui.PlanPage();
        var layout = ui.Render<MainLayout>(parameters => parameters.Add(
            page => page.Body,
            (RenderFragment)(body =>
            {
                body.OpenComponent<ControlCenter>(0);
                body.CloseComponent();
            })));

        foreach (var markup in new[] { shell, layout.Markup })
        {
            Assert.DoesNotMatch("(src|href)=\"(https?:)?//", markup);
        }

        Assert.Contains("_framework/blazor.web", shell, StringComparison.Ordinal);
    }
}