using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Airlock.Web.Api.Contracts;

/// <summary>The types the API writes, generated at build time.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ScanAcceptedDto))]
[JsonSerializable(typeof(ScanDto))]
[JsonSerializable(typeof(EmergencyStopDto))]
[JsonSerializable(typeof(SystemHealthDto))]
[JsonSerializable(typeof(PhaseEventDto))]
[JsonSerializable(typeof(ScanProgressDto))]
[JsonSerializable(typeof(ScanLogEntryDto))]
[JsonSerializable(typeof(LogEntryDto))]
[JsonSerializable(typeof(FinishedEventDto))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;

/// <summary>How the API writes JSON (WR-02).</summary>
internal static class ApiJson
{
    /// <summary>The options for one-line event data (WR-13): the HTTP options without indentation.</summary>
    public static JsonSerializerOptions Compact { get; } = CreateCompact();

    /// <summary>
    /// Two-space indentation with <c>\n</c> line ends on every system, like the original's Export JSON, and text written as
    /// it is. The relaxed encoder is safe here: every answer is <c>application/json</c> with <c>nosniff</c>, and none is
    /// embedded in HTML.
    /// </summary>
    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.WriteIndented = true;
        options.NewLine = "\n";
        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        options.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default);
    }

    public static byte[] SerializeCompact<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, (JsonTypeInfo<T>)Compact.GetTypeInfo(typeof(T)));

    private static JsonSerializerOptions CreateCompact()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        options.WriteIndented = false;
        options.MakeReadOnly();
        return options;
    }
}

/// <summary>Times as the API writes them: UTC, RFC 3339, milliseconds and <c>Z</c> (WR-02).</summary>
internal static class ApiTime
{
    public static string Format(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>The value cut to whole milliseconds, as <see cref="Format"/> shows it.</summary>
    public static DateTimeOffset Truncate(DateTimeOffset value) =>
        new(value.UtcTicks - (value.UtcTicks % TimeSpan.TicksPerMillisecond), TimeSpan.Zero);
}
