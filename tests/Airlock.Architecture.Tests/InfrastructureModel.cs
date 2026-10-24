using Airlock.Infrastructure;
using ArchUnitNET.Fluent.Syntax.Elements.Types;
using ArchUnitNET.Loader;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using ArchitectureModel = ArchUnitNET.Domain.Architecture;
using MethodMember = ArchUnitNET.Domain.MethodMember;

namespace Airlock.Architecture.Tests;

/// <summary>The Infrastructure assembly as ArchUnitNET reads it, loaded once for every rule.</summary>
internal static class InfrastructureModel
{
    public static readonly System.Reflection.Assembly Assembly = typeof(InfrastructureServiceCollectionExtensions).Assembly;

    public static readonly ArchitectureModel Architecture = new ArchLoader().LoadAssemblies(Assembly).Build();

    /// <summary>The types Airlock declares, leaving out the attribute and source-generated types the compiler emits.</summary>
    public static GivenTypesConjunction InfrastructureTypes() =>
        Types().That().ResideInNamespaceMatching("^Airlock[.]Infrastructure([.].+)?$");

    public static IEnumerable<MethodMember> CalledMethods(string fullNameContaining) =>
        ArchitectureQueries.CalledMethods(Architecture, fullNameContaining);
}
