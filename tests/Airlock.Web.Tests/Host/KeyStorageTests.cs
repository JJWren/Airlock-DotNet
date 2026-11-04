using Airlock.Web.Tests.Support;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Airlock.Web.Tests.Host;

// U6-NFR-09 — the data-protection keys' folder, Airlock:DataProtection:KeysPath, which the image puts on the volume.
public sealed class KeyStorageTests
{
    [Fact]
    public async Task With_a_folder_the_keys_are_written_there_under_the_name_Airlock()
    {
        using var keys = new TemporaryFolder();
        await using var factory = Factory(keys.Path);

        var secret = Protector(factory).Protect("antiforgery");

        Assert.Equal("antiforgery", Protector(factory).Unprotect(secret));
        Assert.Single(Directory.EnumerateFiles(keys.Path, "key-*.xml"));
        Assert.Equal(
            KeyStorageOptions.ApplicationName,
            factory.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
    }

    [Fact]
    public async Task A_restarted_airlock_reads_what_it_protected_before()
    {
        using var keys = new TemporaryFolder();
        string secret;
        await using (var first = Factory(keys.Path))
        {
            secret = Protector(first).Protect("antiforgery");
        }

        await using var second = Factory(keys.Path);

        Assert.Equal("antiforgery", Protector(second).Unprotect(secret));
        Assert.Single(Directory.EnumerateFiles(keys.Path, "key-*.xml"));
    }

    [Fact]
    public async Task Without_a_folder_the_keys_stay_where_ASP_NET_Core_puts_them()
    {
        await using var factory = new AirlockWebFactory();

        Assert.Null(factory.Services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository);
        Assert.NotEqual(
            KeyStorageOptions.ApplicationName,
            factory.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
    }

    [Theory]
    [InlineData("relative/keys")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_folder_that_is_not_a_full_path_is_refused_by_name(string path)
    {
        var result = new KeyStorageOptionsValidator().Validate(null, new KeyStorageOptions { KeysPath = path });

        Assert.True(result.Failed);
        Assert.Equal("Airlock:DataProtection:KeysPath must be a full path", result.FailureMessage);
    }

    [Fact]
    public void A_full_path_or_none_is_accepted()
    {
        var validator = new KeyStorageOptionsValidator();

        Assert.True(validator.Validate(null, new KeyStorageOptions { KeysPath = Path.GetFullPath("keys") }).Succeeded);
        Assert.True(validator.Validate(null, new KeyStorageOptions()).Succeeded);
    }

    private static AirlockWebFactory Factory(string keysPath) =>
        new(settings => settings["Airlock:DataProtection:KeysPath"] = keysPath);

    private static IDataProtector Protector(AirlockWebFactory factory) =>
        factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Airlock.Web.Tests");
}
