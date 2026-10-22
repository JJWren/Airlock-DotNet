using System.Globalization;
using System.Text;
using Airlock.Domain.Text;

namespace Airlock.Application.Scanning;

/// <summary>
/// Turns an uploaded file's name into a plain file name that is safe on Windows and Linux (AR-07): its last path
/// segment, with invalid, control and formatting characters replaced by <c>_</c>, trailing dots and spaces dropped, a
/// Windows device name escaped, and at most 255 characters, keeping the extension.
/// </summary>
public static class ArtifactName
{
    public const string Fallback = "artifact";

    private const char Backslash = (char)92;
    private const char Replacement = '_';

    private static readonly char[] InvalidCharacters = ['<', '>', ':', '"', '/', '|', '?', '*', Backslash];

    private static readonly HashSet<string> DeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static string Sanitize(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return Fallback;
        }

        var lastSeparator = Math.Max(fileName.LastIndexOf('/'), fileName.LastIndexOf(Backslash));
        var segment = fileName[(lastSeparator + 1)..];

        var builder = new StringBuilder(segment.Length);
        foreach (var c in segment)
        {
            var unsafeCharacter = char.IsControl(c)
                || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format
                || InvalidCharacters.Contains(c);
            builder.Append(unsafeCharacter ? Replacement : c);
        }

        var name = builder.ToString().Trim().TrimEnd('.', ' ');
        if (name.Length == 0)
        {
            return Fallback;
        }

        if (DeviceNames.Contains(name.Split('.')[0].TrimEnd()))
        {
            name = Replacement + name;
        }

        return Shorten(name, DomainLimits.FileName);
    }

    private static string Shorten(string name, int maxLength)
    {
        if (name.Length <= maxLength)
        {
            return name;
        }

        var dot = name.LastIndexOf('.');
        var extension = dot > 0 && name.Length - dot <= 16 ? name[dot..] : string.Empty;
        return name[..(maxLength - extension.Length)] + extension;
    }
}
