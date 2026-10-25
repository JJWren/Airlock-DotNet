using ArchUnitNET.xUnitV3;
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
    public void Every_Web_type_but_Program_is_internal()
    {
        var exported = WebModel.Assembly.GetExportedTypes().Select(type => type.FullName).ToList();

        Assert.Equal(["Program"], exported);
    }
}
