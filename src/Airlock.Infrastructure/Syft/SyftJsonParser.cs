using System.Text.Json;
using Airlock.Application.Logging;
using Airlock.Application.Ports;
using Airlock.Domain.Intelligence;
using Airlock.Domain.Targets;

namespace Airlock.Infrastructure.Syft;

/// <summary>Reads Syft's JSON output as it streams in and turns it into the Application's packages (IR-03).</summary>
internal static class SyftJsonParser
{
    private const string DictionarySource = "nvd-cpe-dictionary";
    private const string ImageSource = "image";

    /// <summary>The document, or why it could not be read; only cancellation and broken pipes throw.</summary>
    public static async Task<SyftParse> ParseAsync(Stream syftJson, CancellationToken cancellationToken)
    {
        try
        {
            var document = await JsonSerializer.DeserializeAsync(syftJson, SyftJsonContext.Default.SyftDocument, cancellationToken);
            return document?.Artifacts is null
                ? new SyftParse(null, "it holds no list of artifacts")
                : new SyftParse(document, null);
        }
        catch (JsonException exception)
        {
            return new SyftParse(null, ExternalText.Clean(exception.Message));
        }
    }

    // IR-03: each artifact becomes a Package as Syft reports it, with its candidate CPEs in Syft's order. A value the
    // Domain rejects throws ArgumentException, which makes the whole output unreadable (Q-U3-3).
    public static SbomProduced Map(SyftDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var artifacts = document.Artifacts ?? [];
        var packages = new List<SbomPackage>(artifacts.Count);
        foreach (var artifact in artifacts)
        {
            var package = new Package(artifact.Type ?? string.Empty, artifact.Name ?? string.Empty, artifact.Version ?? string.Empty);
            var candidates = (artifact.Cpes ?? [])
                .Select(cpe => new CandidateCpe(
                    cpe.Cpe ?? string.Empty,
                    string.Equals(cpe.Source, DictionarySource, StringComparison.Ordinal) ? CpeSource.Dictionary : CpeSource.Generated))
                .ToList();
            packages.Add(new SbomPackage(package, candidates));
        }

        return new SbomProduced(packages, string.Equals(document.Source?.Type, ImageSource, StringComparison.Ordinal));
    }
}

/// <summary>Syft's document, or the reason it could not be read.</summary>
internal sealed record SyftParse(SyftDocument? Document, string? Problem);
