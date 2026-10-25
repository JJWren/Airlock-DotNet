using ArchUnitNET.Fluent.Syntax.Elements.Types;
using ArchUnitNET.Loader;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using ArchitectureModel = ArchUnitNET.Domain.Architecture;

namespace Airlock.Architecture.Tests;

/// <summary>The Web assembly as ArchUnitNET reads it, with the Infrastructure it must not reach, loaded once.</summary>
internal static class WebModel
{
    public const string CompositionRoot = "Airlock.Web.AirlockHost";

    public static readonly System.Reflection.Assembly Assembly = typeof(Program).Assembly;

    public static readonly ArchitectureModel Architecture = new ArchLoader().LoadAssemblies(Assembly, InfrastructureModel.Assembly).Build();

    /// <summary>The API: every type in <c>Airlock.Web.Api</c> and its namespaces.</summary>
    public static GivenTypesConjunction ApiTypes() => Types().That().ResideInNamespaceMatching("^Airlock[.]Web[.]Api([.].+)?$");
}
