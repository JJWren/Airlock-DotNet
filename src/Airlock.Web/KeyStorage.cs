using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Options;

namespace Airlock.Web;

/// <summary>
/// Where ASP.NET Core keeps its data-protection keys, bound from <c>Airlock:DataProtection</c> (U6-NFR-09). The image
/// points it at the volume, so the keys survive a restart; unset, they stay where ASP.NET Core puts them (U4).
/// </summary>
internal sealed class KeyStorageOptions
{
    public const string SectionName = "Airlock:DataProtection";

    /// <summary>The application name the key ring is kept under once it has a folder of its own.</summary>
    public const string ApplicationName = "Airlock";

    public string? KeysPath { get; set; }
}

/// <summary>Checks the keys' folder: a full path or nothing; anything else stops startup and names the setting (E13).</summary>
internal sealed class KeyStorageOptionsValidator : IValidateOptions<KeyStorageOptions>
{
    public static bool IsFullPath(string path) => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path);

    public ValidateOptionsResult Validate(string? name, KeyStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.KeysPath is null || IsFullPath(options.KeysPath)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(KeyStorageOptions.SectionName + ":KeysPath must be a full path");
    }
}

/// <summary>
/// Keeps the key ring in the configured folder, as <c>PersistKeysToFileSystem</c> would, under Airlock's name. It reads
/// the setting itself, unvalidated: antiforgery reads data protection's options while the pipeline is built, before
/// startup validates the setting (E13), so a folder that is not a full path is ignored here and refused there.
/// </summary>
internal sealed class KeyStorageSetup(IConfiguration configuration, ILoggerFactory loggerFactory)
    : IConfigureOptions<KeyManagementOptions>, IConfigureOptions<DataProtectionOptions>
{
    private string? Folder =>
        configuration[KeyStorageOptions.SectionName + ":" + nameof(KeyStorageOptions.KeysPath)] is { } path
        && KeyStorageOptionsValidator.IsFullPath(path)
            ? path
            : null;

    public void Configure(KeyManagementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (Folder is { } folder)
        {
            options.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(folder), loggerFactory);
        }
    }

    public void Configure(DataProtectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (Folder is not null)
        {
            options.ApplicationDiscriminator = KeyStorageOptions.ApplicationName;
        }
    }
}

internal static class KeyStorage
{
    public static IServiceCollection AddAirlockKeyStorage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<KeyStorageOptions>().BindConfiguration(KeyStorageOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IValidateOptions<KeyStorageOptions>, KeyStorageOptionsValidator>();

        // After data protection's own setup, which names the application by its content root.
        services.AddDataProtection();
        services.AddSingleton<IConfigureOptions<KeyManagementOptions>, KeyStorageSetup>();
        services.AddSingleton<IConfigureOptions<DataProtectionOptions>, KeyStorageSetup>();
        return services;
    }
}
