using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Airlock.Architecture.Tests;

// U1-NFR-03: Domain code reads no clock, environment, file, network, process or console; times are parameters.
public sealed class DomainDeterminismRules
{
    [Theory]
    [InlineData("System.DateTime::get_Now()")]
    [InlineData("System.DateTime::get_UtcNow()")]
    [InlineData("System.DateTime::get_Today()")]
    [InlineData("System.DateTimeOffset::get_Now()")]
    [InlineData("System.DateTimeOffset::get_UtcNow()")]
    public void Domain_code_does_not_read_the_clock(string clockRead)
    {
        DomainModel.DomainTypes()
            .Should().NotCallAny(DomainModel.CalledMethods(clockRead))
            .Check(DomainModel.Architecture);
    }

    // A type the Domain never references is absent from the architecture, so the rule names types by full name.
    [Theory]
    [InlineData("System.Environment")]
    [InlineData("System.Diagnostics.Process")]
    [InlineData("System.Console")]
    public void Domain_code_does_not_use_the_environment_processes_or_the_console(string typeName)
    {
        DomainModel.DomainTypes()
            .Should().NotDependOnAny(Types(true).That().HaveFullName(typeName))
            .Check(DomainModel.Architecture);
    }

    [Theory]
    [InlineData("^System[.]IO([.]|$)")]
    [InlineData("^System[.]Net([.]|$)")]
    public void Domain_code_does_not_touch_files_or_the_network(string namespacePattern)
    {
        DomainModel.DomainTypes()
            .Should().NotDependOnAny(Types(true).That().ResideInNamespaceMatching(namespacePattern))
            .Check(DomainModel.Architecture);
    }

    [Fact]
    public void The_call_rule_sees_calls_into_the_base_class_library()
    {
        // ScanId.New() calls Guid.CreateVersion7(); if this control fails, the clock rule checks nothing.
        Types().That().HaveFullName("Airlock.Domain.Scanning.ScanId")
            .Should().CallAny(DomainModel.CalledMethods("System.Guid::CreateVersion7()"))
            .Check(DomainModel.Architecture);
    }

    [Fact]
    public void The_type_rule_sees_base_class_library_types()
    {
        // Cve uses Regex; if this control fails, the environment, process and console rule checks nothing.
        Types().That().HaveFullName("Airlock.Domain.Intelligence.Cve")
            .Should().DependOnAny(Types(true).That().HaveFullName("System.Text.RegularExpressions.Regex"))
            .Check(DomainModel.Architecture);
    }
}
