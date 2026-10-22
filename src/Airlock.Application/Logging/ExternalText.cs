using System.Globalization;
using System.Text;

namespace Airlock.Application.Logging;

/// <summary>
/// Cleans text that comes from outside Airlock — Syft's error output, NVD's error responses — before it appears in a
/// log line or a reason (AR-25): one line, no control or formatting characters, no terminal escape sequences, cut to
/// a maximum length.
/// </summary>
public static class ExternalText
{
    public const int DefaultMaxLength = 512;

    public const string NoDetails = "no details given";

    private const char Escape = (char)0x1B;
    private const char Ellipsis = '…';

    public static string Clean(string? text, int maxLength = DefaultMaxLength)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, 2);
        if (string.IsNullOrWhiteSpace(text))
        {
            return NoDetails;
        }

        var builder = new StringBuilder(Math.Min(text.Length, maxLength + 1));
        var escape = EscapeState.None;
        var pendingSpace = false;
        foreach (var c in text)
        {
            if (escape == EscapeState.Started)
            {
                escape = c == '[' ? EscapeState.Sequence : EscapeState.None;
                continue;
            }

            if (escape == EscapeState.Sequence)
            {
                // A control sequence ends with its final byte, '@' to '~'.
                escape = c is >= '@' and <= '~' ? EscapeState.None : EscapeState.Sequence;
                continue;
            }

            if (c == Escape)
            {
                escape = EscapeState.Started;
            }
            else if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
            }
            else if (!char.IsControl(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format)
            {
                if (pendingSpace)
                {
                    builder.Append(' ');
                    pendingSpace = false;
                }

                builder.Append(c);
                if (builder.Length > maxLength)
                {
                    break;
                }
            }
        }

        if (builder.Length == 0)
        {
            return NoDetails;
        }

        if (builder.Length > maxLength)
        {
            var cut = maxLength - 1;
            if (char.IsHighSurrogate(builder[cut - 1]))
            {
                cut--;
            }

            builder.Length = cut;
            builder.Append(Ellipsis);
        }

        return builder.ToString();
    }

    private enum EscapeState
    {
        None,
        Started,
        Sequence,
    }
}
