using System.Globalization;
using Airlock.Domain.Intelligence;
using Airlock.Domain.Targets;

namespace Airlock.Domain.Tests;

// U1-NFR-03: the Turkish culture lower-cases "I" to a dotless "ı" and upper-cases "i" to a dotted "İ".
public sealed class CultureIndependenceTests
{
    [Fact]
    public void Names_normalize_the_same_way_under_the_Turkish_culture()
    {
        UnderTurkishCulture(() =>
        {
            Assert.Equal("pillow", ProductName.Normalize("PILLOW"));
            Assert.Equal("pillow", ProductName.ForProductMatch(A.Package("PILLOW", "9.0")));
            Assert.Equal(A.Package("IMAGEIO", "2.9"), A.Package("imageio", "2.9"));
            Assert.True(ManifestCatalog.IsManifest("REQUIREMENTS.TXT"));
            Assert.Equal(Severity.Critical, SeverityScale.FromLabel("critical"));
            Assert.False(PackageVersion.IsKnown(A.Package("syft", "unknown", "binary")));
        });
    }

    private static void UnderTurkishCulture(Action assertions)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal("pıllow", "PILLOW".ToLower(CultureInfo.CurrentCulture));
            assertions();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
