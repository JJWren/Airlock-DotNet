using System.Globalization;

namespace Airlock.Domain.Text;

/// <summary>The single validation path for text held by Domain objects (U1-NFR-04).</summary>
internal static class DomainText
{
    // U+200E and U+200F are the directional marks; U+202A to U+202E the embeddings, overrides and their pop;
    // U+2066 to U+2069 the isolates and their pop.
    private const char LeftToRightMark = (char)0x200E;
    private const char RightToLeftMark = (char)0x200F;
    private const char FirstEmbeddingOrOverride = (char)0x202A;
    private const char LastEmbeddingOrOverride = (char)0x202E;
    private const char FirstIsolate = (char)0x2066;
    private const char LastIsolate = (char)0x2069;

    public static string Require(string? value, int maxLength, string paramName, bool allowLineBreaks = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return Check(value, maxLength, paramName, allowLineBreaks);
    }

    public static string AllowEmpty(string? value, int maxLength, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        return Check(value, maxLength, paramName, allowLineBreaks: false);
    }

    public static string? Optional(string? value, int maxLength, string paramName) =>
        value is null ? null : Require(value, maxLength, paramName);

    /// <summary>
    /// Control characters (C0, DEL, C1) and the bidirectional formatting characters that can make text display
    /// differently from what it contains. Line feeds and tabs are allowed only where <paramref name="allowLineBreaks"/>.
    /// </summary>
    public static bool IsForbidden(char c, bool allowLineBreaks) =>
        (char.IsControl(c) && !(allowLineBreaks && (c is '\n' or '\t')))
        || c is LeftToRightMark or RightToLeftMark
        || c is (>= FirstEmbeddingOrOverride and <= LastEmbeddingOrOverride) or (>= FirstIsolate and <= LastIsolate);

    private static string Check(string value, int maxLength, string paramName, bool allowLineBreaks)
    {
        if (value.Length > maxLength)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"Must be at most {maxLength} characters long."),
                paramName);
        }

        foreach (var c in value)
        {
            if (IsForbidden(c, allowLineBreaks))
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"Contains the disallowed character U+{(int)c:X4}."),
                    paramName);
            }
        }

        return value;
    }
}
