using Airlock.Application.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Airlock.Infrastructure;

/// <summary>Adds the original's environment variables to the configuration (IR-23); the host (U4) calls it.</summary>
public static class AirlockConfigurationBuilderExtensions
{
    /// <summary>
    /// Maps <c>NVD_API_KEY</c> and <c>SYFT_BINARY_PATH</c> onto the Airlock options, and <c>LOG_LEVEL</c> onto the console's
    /// default level. Add it after the JSON files, so these variables win over them.
    /// </summary>
    public static IConfigurationBuilder AddAirlockEnvironmentVariables(this IConfigurationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Add(new AirlockEnvironmentVariables(Environment.GetEnvironmentVariable));
    }
}

/// <summary>The configuration source for the original's variable names (IR-23).</summary>
internal sealed class AirlockEnvironmentVariables(Func<string, string?> read) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new Provider(read);

    // IR-23: the original's level names (loguru's) as well as .NET's, in any case.
    internal static LogLevel? ParseLevel(string value) => value.Trim().ToUpperInvariant() switch
    {
        "TRACE" => LogLevel.Trace,
        "DEBUG" => LogLevel.Debug,
        "INFO" or "INFORMATION" => LogLevel.Information,
        "WARNING" => LogLevel.Warning,
        "ERROR" => LogLevel.Error,
        "CRITICAL" => LogLevel.Critical,
        "NONE" => LogLevel.None,
        _ => null,
    };

    private sealed class Provider(Func<string, string?> read) : ConfigurationProvider
    {
        public override void Load()
        {
            var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            Map(data, "NVD_API_KEY", "Airlock:Nvd:ApiKey");
            Map(data, "SYFT_BINARY_PATH", "Airlock:Syft:BinaryPath");
            if (read("LOG_LEVEL") is { } level && !string.IsNullOrWhiteSpace(level))
            {
                data["Logging:LogLevel:Default"] = ParseLevel(level)?.ToString() ?? throw new InvalidOperationException(
                    "LOG_LEVEL must be TRACE, DEBUG, INFO, WARNING, ERROR, CRITICAL or NONE, not '"
                    + ExternalText.Clean(level, 32) + "'");
            }

            Data = data;
        }

        private void Map(Dictionary<string, string?> data, string variable, string key)
        {
            if (read(variable) is { } value && !string.IsNullOrWhiteSpace(value))
            {
                data[key] = value;
            }
        }
    }
}
