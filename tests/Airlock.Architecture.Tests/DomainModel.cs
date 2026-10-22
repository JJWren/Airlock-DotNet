using Airlock.Domain.Scanning;
using ArchUnitNET.Fluent.Syntax.Elements.Types;
using ArchUnitNET.Loader;
using static ArchUnitNET.Fluent.ArchRuleDefinition;
using ArchitectureModel = ArchUnitNET.Domain.Architecture;
using MethodCallDependency = ArchUnitNET.Domain.Dependencies.MethodCallDependency;
using MethodMember = ArchUnitNET.Domain.MethodMember;

namespace Airlock.Architecture.Tests;

/// <summary>The Domain assembly as ArchUnitNET reads it, loaded once for every rule.</summary>
internal static class DomainModel
{
    public static readonly System.Reflection.Assembly Assembly = typeof(Scan).Assembly;

    public static readonly ArchitectureModel Architecture = new ArchLoader().LoadAssemblies(Assembly).Build();

    /// <summary>
    /// The types Airlock declares. The compiler also emits attribute types into the assembly, such as
    /// Microsoft.CodeAnalysis.EmbeddedAttribute, and the rules leave those out.
    /// </summary>
    public static GivenTypesConjunction DomainTypes() =>
        Types().That().ResideInNamespaceMatching("^Airlock[.]Domain([.].+)?$");

    public static IEnumerable<MethodMember> CalledMethods(string fullNameContaining) =>
        ArchitectureQueries.CalledMethods(Architecture, fullNameContaining);
}

internal static class ArchitectureQueries
{
    /// <summary>
    /// The methods an architecture's types call whose full name contains the given text. ArchUnitNET records calls into
    /// other assemblies only as dependencies, not as members of the referenced types, so they are read from there.
    /// </summary>
    public static IEnumerable<MethodMember> CalledMethods(ArchitectureModel architecture, string fullNameContaining) =>
        architecture.Types
            .SelectMany(type => type.Dependencies)
            .OfType<MethodCallDependency>()
            .Select(call => call.TargetMember)
            .OfType<MethodMember>()
            .Where(method => method.FullName.Contains(fullNameContaining, StringComparison.Ordinal))
            .Distinct();
}
