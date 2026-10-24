using System.Text.Json.Serialization;

namespace Airlock.Infrastructure.Syft;

// The fields of Syft's JSON output (schema 16.1.11) that Airlock reads; every other field is skipped (domain-entities.md).

internal sealed record SyftDocument(SyftSource? Source, IReadOnlyList<SyftArtifact>? Artifacts);

internal sealed record SyftSource(string? Type);

internal sealed record SyftArtifact(string? Type, string? Name, string? Version, IReadOnlyList<SyftCpe>? Cpes);

internal sealed record SyftCpe(string? Cpe, string? Source);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SyftDocument))]
internal sealed partial class SyftJsonContext : JsonSerializerContext;
