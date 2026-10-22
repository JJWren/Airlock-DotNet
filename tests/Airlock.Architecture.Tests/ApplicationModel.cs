using Airlock.Application.UseCases;
using ArchUnitNET.Fluent.Syntax.Elements.Types;
using ArchUnitNET.Loader;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using ArchitectureModel = ArchUnitNET.Domain.Architecture;
using MethodMember = ArchUnitNET.Domain.MethodMember;

namespace Airlock.Architecture.Tests;

/// <summary>The Application assembly as ArchUnitNET reads it, loaded once for every rule.</summary>
internal static class ApplicationModel
{
    public const string UseCasesNamespace = "Airlock.Application.UseCases";
    public const string PortsNamespace = "Airlock.Application.Ports";

    public static readonly System.Reflection.Assembly Assembly = typeof(StartScan).Assembly;

    public static readonly ArchitectureModel Architecture = new ArchLoader().LoadAssemblies(Assembly).Build();

    /// <summary>The types Airlock declares, leaving out the attribute types the compiler emits.</summary>
    public static GivenTypesConjunction ApplicationTypes() =>
        Types().That().ResideInNamespaceMatching("^Airlock[.]Application([.].+)?$");

    public static IEnumerable<MethodMember> CalledMethods(string fullNameContaining) =>
        ArchitectureQueries.CalledMethods(Architecture, fullNameContaining);
}
