using Airlock.Application.UseCases;
using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Airlock.Architecture.Tests;

// Architecture rule 2 (U2-NFR-01): the Application depends on the Domain and the Microsoft.Extensions abstractions only.
public sealed class ApplicationDependencyRules
{
    [Fact]
    public void Application_references_only_the_base_class_library_the_Domain_and_Microsoft_Extensions()
    {
        var others = ApplicationModel.Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name != "System"
                && !name.StartsWith("System.", StringComparison.Ordinal)
                && name != "Airlock.Domain"
                && !name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal));

        Assert.Empty(others);
    }

    [Fact]
    public void Application_types_depend_on_no_Infrastructure_Web_or_ASP_NET_Core_namespace()
    {
        ApplicationModel.ApplicationTypes()
            .Should().NotDependOnAny(
                Types(true).That().ResideInNamespaceMatching("^(Airlock[.](Infrastructure|Web)|Microsoft[.]AspNetCore)([.]|$)"))
            .Check(ApplicationModel.Architecture);
    }

    [Fact]
    public void The_namespace_rule_sees_the_abstractions_the_Application_uses()
    {
        // PackageCorrelator logs through Microsoft.Extensions.Logging; if this control fails, the rule above checks nothing.
        Types().That().HaveFullName("Airlock.Application.Correlation.PackageCorrelator")
            .Should().DependOnAny(Types(true).That().ResideInNamespace("Microsoft.Extensions.Logging"))
            .Check(ApplicationModel.Architecture);
    }
}

// Architecture rule 4, the Application's half: the ports are interfaces that no Application type implements.
public sealed class PortRules
{
    [Fact]
    public void No_Application_type_implements_a_port()
    {
        var types = ApplicationModel.Assembly.GetTypes();
        var ports = types.Where(type => type.IsInterface && type.Namespace == ApplicationModel.PortsNamespace).ToList();
        var implementations = types
            .Where(type => !type.IsInterface && type.GetInterfaces().Any(ports.Contains))
            .Select(type => type.FullName);

        Assert.Equal(8, ports.Count);
        Assert.Empty(implementations);
    }
}

// Architecture rule 6: use cases are sealed classes in Airlock.Application.UseCases.
public sealed class UseCaseRules
{
    [Fact]
    public void Every_class_in_the_use_case_namespace_is_sealed()
    {
        Classes().That().ResideInNamespace(ApplicationModel.UseCasesNamespace)
            .Should().BeSealed()
            .Check(ApplicationModel.Architecture);
    }

    [Fact]
    public void The_use_cases_live_in_the_use_case_namespace()
    {
        Type[] useCases =
        [
            typeof(StartScan), typeof(ScanReservation), typeof(CancelScan), typeof(EmergencyStop), typeof(GetScan),
            typeof(WatchScan), typeof(WatchActiveScan), typeof(RenderScanReport), typeof(GetSystemHealth), typeof(WatchLiveLog),
        ];

        Assert.All(useCases, useCase =>
        {
            Assert.Equal(ApplicationModel.UseCasesNamespace, useCase.Namespace);
            Assert.True(useCase.IsSealed, useCase.Name);
        });
    }
}

// U2-NFR-08: Application code reads time only through the TimeProvider.
public sealed class ApplicationDeterminismRules
{
    [Theory]
    [InlineData("System.DateTime::get_Now()")]
    [InlineData("System.DateTime::get_UtcNow()")]
    [InlineData("System.DateTime::get_Today()")]
    [InlineData("System.DateTimeOffset::get_Now()")]
    [InlineData("System.DateTimeOffset::get_UtcNow()")]
    [InlineData("System.Threading.Tasks.Task::Delay(System.TimeSpan)")]
    [InlineData("System.Threading.Tasks.Task::Delay(System.Int32)")]
    [InlineData("System.Threading.Tasks.Task::Delay(System.TimeSpan,System.Threading.CancellationToken)")]
    [InlineData("System.Threading.Tasks.Task::Delay(System.Int32,System.Threading.CancellationToken)")]
    [InlineData("System.Threading.CancellationTokenSource::CancelAfter(")]
    [InlineData("System.Threading.CancellationTokenSource::.ctor(System.TimeSpan)")]
    [InlineData("System.Threading.CancellationTokenSource::.ctor(System.Int32)")]
    public void Application_code_reads_time_only_through_the_TimeProvider(string clockUse)
    {
        ApplicationModel.ApplicationTypes()
            .Should().NotCallAny(ApplicationModel.CalledMethods(clockUse))
            .Check(ApplicationModel.Architecture);
    }

    [Theory]
    [InlineData("System.Diagnostics.Stopwatch")]
    [InlineData("System.Threading.Timer")]
    [InlineData("System.Timers.Timer")]
    public void Application_code_uses_no_stopwatch_or_timer(string typeName)
    {
        ApplicationModel.ApplicationTypes()
            .Should().NotDependOnAny(Types(true).That().HaveFullName(typeName))
            .Check(ApplicationModel.Architecture);
    }

    [Fact]
    public void The_call_rule_sees_the_TimeProvider_calls()
    {
        // The pipeline stamps times with TimeProvider.GetUtcNow(); if this control fails, the rule above checks nothing.
        Types().That().HaveFullName("Airlock.Application.Scanning.ScanPipeline")
            .Should().CallAny(ApplicationModel.CalledMethods("System.TimeProvider::GetUtcNow()"))
            .Check(ApplicationModel.Architecture);
    }
}
