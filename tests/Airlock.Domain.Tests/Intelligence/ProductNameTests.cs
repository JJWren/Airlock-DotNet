using Airlock.Domain.Intelligence;

namespace Airlock.Domain.Tests.Intelligence;

// BR-09 — rows of the product-name table.
public sealed class ProductNameTests
{
    [Theory]
    [InlineData("python", "pyyaml", "pyyaml")]
    [InlineData("python", "PyYAML", "pyyaml")]
    [InlineData("npm", "minimist", "minimist")]
    [InlineData("go-module", "github.com/gin-gonic/gin", "gin")]
    [InlineData("dotnet", "System.Text.Json", "system.text.json")]
    [InlineData("apk", "busybox", "busybox")]
    public void A_specific_name_is_product_matched(string type, string name, string product)
    {
        Assert.Equal(product, ProductName.ForProductMatch(A.Package(name, "1.0", type)));
    }

    [Theory]
    [InlineData("go-module", "cloud.google.com/go/auth")]
    [InlineData("npm", "@angular/core")]
    [InlineData("go-module", "stdlib")]
    [InlineData("npm", "io")]
    [InlineData("binary", "my tool")]
    [InlineData("go-module", "example.com/")]
    public void A_generic_short_or_spaced_name_is_not_product_matched(string type, string name)
    {
        Assert.Null(ProductName.ForProductMatch(A.Package(name, "1.0", type)));
    }

    [Fact]
    public void Normalizing_lower_cases_with_the_invariant_culture()
    {
        Assert.Equal("pyyaml", ProductName.Normalize("PyYAML"));
    }

    [Fact]
    public void Null_input_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => ProductName.ForProductMatch(null!));
        Assert.Throws<ArgumentNullException>(() => ProductName.Normalize(null!));
    }
}
