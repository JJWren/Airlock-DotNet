using System.Globalization;

namespace Airlock.Domain.Scanning;

/// <summary>The identifier of a scan; time-ordered (GUID version 7).</summary>
public readonly record struct ScanId
{
    public ScanId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Must not be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static ScanId New() => new(Guid.CreateVersion7());

    /// <summary>Parses the standard "D" format used in URLs; the empty GUID is rejected.</summary>
    public static bool TryParse(string? text, out ScanId id)
    {
        if (Guid.TryParseExact(text, "D", out var value) && value != Guid.Empty)
        {
            id = new ScanId(value);
            return true;
        }

        id = default;
        return false;
    }

    public override string ToString() => Value.ToString("D", CultureInfo.InvariantCulture);
}
