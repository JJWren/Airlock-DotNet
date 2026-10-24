using ArchUnitNET.xUnitV3;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Airlock.Architecture.Tests;

// Architecture rule 3 (U3-NFR-01, IR-26): the Infrastructure never depends on the Web layer or ASP.NET Core.
public sealed class InfrastructureDependencyRules
{
    [Fact]
    public void Infrastructure_references_no_Web_or_ASP_NET_Core_assembly()
    {
        var references = InfrastructureModel.Assembly.GetReferencedAssemblies().Select(reference => reference.Name!);

        Assert.DoesNotContain(references, name => name == "Airlock.Web" || name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }

    [Fact]
    public void Infrastructure_types_depend_on_no_Web_or_ASP_NET_Core_namespace()
    {
        InfrastructureModel.InfrastructureTypes()
            .Should().NotDependOnAny(Types(true).That().ResideInNamespaceMatching("^(Airlock[.]Web|Microsoft[.]AspNetCore)([.]|$)"))
            .Check(InfrastructureModel.Architecture);
    }

    [Fact]
    public void The_namespace_rule_sees_the_frameworks_the_Infrastructure_uses()
    {
        // The NVD client sends through System.Net.Http; if this control fails, the rule above checks nothing.
        Types().That().HaveFullName("Airlock.Infrastructure.Nvd.NvdApiClient")
            .Should().DependOnAny(Types(true).That().ResideInNamespace("System.Net.Http"))
            .Check(InfrastructureModel.Architecture);
    }
}

// Architecture rule 4, the Infrastructure's half (U3-NFR-01, IR-26): every port has exactly one implementation, here.
public sealed class PortImplementationRules
{
    [Fact]
    public void Each_of_the_eight_ports_has_exactly_one_implementation_in_the_Infrastructure()
    {
        var ports = ApplicationModel.Assembly.GetTypes()
            .Where(type => type.IsInterface && type.Namespace == ApplicationModel.PortsNamespace)
            .ToList();
        var candidates = InfrastructureModel.Assembly.GetTypes().Where(type => type is { IsInterface: false, IsAbstract: false }).ToList();

        Assert.Equal(8, ports.Count);
        Assert.All(ports, port =>
        {
            var implementations = candidates.Where(port.IsAssignableFrom).Select(type => type.FullName).ToList();
            Assert.True(implementations.Count == 1, $"{port.Name} has {implementations.Count} implementations: {string.Join(", ", implementations)}");
        });
    }

    [Fact]
    public void The_implementations_live_in_their_adapters_namespaces()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ISbomGenerator"] = "Airlock.Infrastructure.Syft.SyftSbomGenerator",
            ["IVulnerabilityCatalog"] = "Airlock.Infrastructure.Nvd.NvdVulnerabilityCatalog",
            ["IArtifactStaging"] = "Airlock.Infrastructure.Staging.DiskArtifactStaging",
            ["IStagingArea"] = "Airlock.Infrastructure.Staging.DiskStagingArea",
            ["IScanRegistry"] = "Airlock.Infrastructure.Scans.InMemoryScanRegistry",
            ["IScanReportRenderer"] = "Airlock.Infrastructure.Reports.QuestPdfScanReportRenderer",
            ["ILiveLog"] = "Airlock.Infrastructure.Logging.LiveLogBroadcaster",
            ["IHostFileSystem"] = "Airlock.Infrastructure.FileSystem.HostFileSystem",
        };
        var candidates = InfrastructureModel.Assembly.GetTypes().Where(type => type is { IsInterface: false, IsAbstract: false }).ToList();

        Assert.All(
            ApplicationModel.Assembly.GetTypes().Where(type => type.IsInterface && type.Namespace == ApplicationModel.PortsNamespace),
            port => Assert.Equal(expected[port.Name], candidates.Single(port.IsAssignableFrom).FullName));
    }
}

// U3 code generation refinement 3: Infrastructure code reads time only through the TimeProvider, as the Domain and the
// Application do, so every wait runs on a fake clock in the tests.
public sealed class InfrastructureDeterminismRules
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
    [InlineData("::WaitAsync(System.TimeSpan)")]
    [InlineData("::WaitAsync(System.TimeSpan,System.Threading.CancellationToken)")]
    [InlineData("System.Threading.CancellationTokenSource::CancelAfter(")]
    [InlineData("System.Threading.CancellationTokenSource::.ctor(System.TimeSpan)")]
    [InlineData("System.Threading.CancellationTokenSource::.ctor(System.Int32)")]
    public void Infrastructure_code_reads_time_only_through_the_TimeProvider(string clockUse)
    {
        InfrastructureModel.InfrastructureTypes()
            .Should().NotCallAny(InfrastructureModel.CalledMethods(clockUse))
            .Check(InfrastructureModel.Architecture);
    }

    [Theory]
    [InlineData("System.Diagnostics.Stopwatch")]
    [InlineData("System.Threading.Timer")]
    [InlineData("System.Timers.Timer")]
    public void Infrastructure_code_uses_no_stopwatch_or_timer(string typeName)
    {
        InfrastructureModel.InfrastructureTypes()
            .Should().NotDependOnAny(Types(true).That().HaveFullName(typeName))
            .Check(InfrastructureModel.Architecture);
    }

    [Fact]
    public void The_call_rule_sees_the_TimeProvider_calls()
    {
        // The pacer stamps each start with TimeProvider.GetTimestamp(); if this control fails, the rule above checks nothing.
        Types().That().HaveFullName("Airlock.Infrastructure.Nvd.NvdRequestPacer")
            .Should().CallAny(InfrastructureModel.CalledMethods("System.TimeProvider::GetTimestamp()"))
            .Check(InfrastructureModel.Architecture);
    }
}
