using ArchUnitNET.xUnitV3;
using Microsoft.AspNetCore.Components;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Airlock.Architecture.Tests;

// Architecture rule 5, the API's half (U4-NFR-01, WR-23): the Web calls Application use cases and never Infrastructure
// types; only the composition root, AirlockHost, wires the Infrastructure in. (Program, the compiler's entry point for
// the top-level statements, only calls AirlockHost; ArchUnitNET does not read compiler-generated methods.)
public sealed class WebApiRules
{
    [Fact]
    public void Web_types_but_the_composition_root_depend_on_no_Infrastructure_type()
    {
        Types().That().ResideInNamespaceMatching("^Airlock[.]Web([.].+)?$").And().DoNotHaveFullName(WebModel.CompositionRoot)
            .Should().NotDependOnAny(Types(true).That().ResideInNamespaceMatching("^Airlock[.]Infrastructure([.]|$)"))
            .Check(WebModel.Architecture);
    }

    [Fact]
    public void The_api_types_are_among_those_the_rule_checks()
    {
        Assert.NotEmpty(WebModel.ApiTypes().GetObjects(WebModel.Architecture));
    }

    [Fact]
    public void The_composition_root_does_reach_the_Infrastructure()
    {
        // Positive control: AirlockHost registers the adapters, so if this fails the rule above checks nothing.
        Types().That().HaveFullName(WebModel.CompositionRoot)
            .Should().DependOnAny(Types(true).That().ResideInNamespace("Airlock.Infrastructure"))
            .Check(WebModel.Architecture);
    }

    [Fact]
    public void Api_types_do_reach_the_Application_use_cases()
    {
        Types().That().HaveFullName("Airlock.Web.Api.ScanEndpoints")
            .Should().DependOnAny(Types(true).That().HaveFullName("Airlock.Application.UseCases.StartScan"))
            .Check(WebModel.Architecture);
    }

    [Fact]
    public void Every_Web_type_but_Program_and_the_components_is_internal()
    {
        // U5 refinement 1: Razor makes every component a public class, and so their base, and the class of its _Imports
        // file; nothing else is exported.
        var exported = WebModel.Assembly.GetExportedTypes();

        Assert.Equal(
            ["Airlock.Web.Components._Imports", "Program"],
            exported.Where(type => !typeof(IComponent).IsAssignableFrom(type)).Select(type => type.FullName).Order(StringComparer.Ordinal));
        Assert.All(
            exported.Where(type => typeof(IComponent).IsAssignableFrom(type)),
            component => Assert.StartsWith("Airlock.Web.Components.", component.FullName, StringComparison.Ordinal));
    }
}

// U5-NFR-01, UR-41: the UI calls the Application in-process; no UI type uses an HttpClient, so the server never calls
// its own API and only the browser posts to it (ADR-0001). Rule 5 above already keeps the UI from the Infrastructure.
public sealed class WebUiRules
{
    [Fact]
    public void Ui_types_depend_on_no_HttpClient()
    {
        WebModel.UiTypes()
            .Should().NotDependOnAny(Types(true).That().HaveFullName("System.Net.Http.HttpClient"))
            .Check(WebModel.Architecture);
    }

    [Fact]
    public void The_ui_types_are_among_those_the_rule_checks()
    {
        Assert.NotEmpty(WebModel.UiTypes().GetObjects(WebModel.Architecture));
    }

    [Fact]
    public void A_page_does_reach_an_Application_use_case()
    {
        // Positive control: Reports reads a scan through GetScan, in-process.
        Types().That().HaveFullName("Airlock.Web.Components.Pages.Reports")
            .Should().DependOnAny(Types(true).That().HaveFullName("Airlock.Application.UseCases.GetScan"))
            .Check(WebModel.Architecture);
    }
}
