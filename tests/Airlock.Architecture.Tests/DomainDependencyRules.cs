using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Airlock.Architecture.Tests;

// Architecture rule 1 (U1-NFR-01): the Domain depends on the .NET base class library only.
public sealed class DomainDependencyRules
{
    [Fact]
    public void Domain_references_only_base_class_library_assemblies()
    {
        var others = DomainModel.Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name != "System" && !name.StartsWith("System.", StringComparison.Ordinal));

        Assert.Empty(others);
    }

    [Fact]
    public void Domain_types_depend_on_no_Microsoft_or_other_Airlock_namespace()
    {
        DomainModel.DomainTypes()
            .Should().NotDependOnAny(
                Types(true).That().ResideInNamespaceMatching("^(Microsoft([.]|$)|Airlock[.](?!Domain([.]|$)))"))
            .Check(DomainModel.Architecture);
    }

    [Fact]
    public void The_namespace_rule_sees_base_class_library_types()
    {
        // Cve holds a generated Regex; if this control fails, the rule above checks nothing.
        Types().That().HaveFullName("Airlock.Domain.Intelligence.Cve")
            .Should().DependOnAny(Types(true).That().ResideInNamespace("System.Text.RegularExpressions"))
            .Check(DomainModel.Architecture);
    }
}
