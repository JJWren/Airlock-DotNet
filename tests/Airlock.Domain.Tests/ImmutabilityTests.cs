using System.Reflection;
using Airlock.Domain.Scanning;

namespace Airlock.Domain.Tests;

// U1-NFR-02: no public setters — `init` included, so `with` cannot bypass validation — and no public fields.
public sealed class ImmutabilityTests
{
    private const BindingFlags Members =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly Type[] ExportedTypes = typeof(Scan).Assembly.GetExportedTypes();

    [Fact]
    public void No_exported_property_has_a_public_setter()
    {
        var offenders = ExportedTypes
            .SelectMany(type => type.GetProperties(Members))
            .Where(property => property.SetMethod is { IsPublic: true })
            .Select(property => $"{property.DeclaringType!.Name}.{property.Name}");

        Assert.Empty(offenders);
    }

    [Fact]
    public void No_exported_type_has_a_public_instance_field()
    {
        var offenders = ExportedTypes
            .Where(type => !type.IsEnum)
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            .Select(field => $"{field.DeclaringType!.Name}.{field.Name}");

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_check_sees_private_init_setters()
    {
        var setter = typeof(Scan).GetProperty(nameof(Scan.Status))!.SetMethod;

        Assert.True(ExportedTypes.Length > 20);
        Assert.NotNull(setter);
        Assert.False(setter.IsPublic);
    }
}
