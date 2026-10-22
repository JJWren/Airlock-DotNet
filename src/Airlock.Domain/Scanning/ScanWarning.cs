using Airlock.Domain.Text;

namespace Airlock.Domain.Scanning;

public enum ScanWarningCode
{
    NoPackagesIdentified,
    DistributionBackportsNotConsidered,
    CvesWithoutSeverity,

    /// <summary>Packages without CPE records whose names are too generic to product-match (U2 Q-U2-3).</summary>
    NotProductMatched,
}

/// <summary>A note about a scan that does not change its status.</summary>
public sealed record ScanWarning
{
    public ScanWarning(ScanWarningCode code, string message, string? input = null)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        Code = code;
        Message = DomainText.Require(message, DomainLimits.Message, nameof(message));
        Input = DomainText.Optional(input, DomainLimits.Path, nameof(input));
    }

    public ScanWarningCode Code { get; }

    public string Message { get; }

    /// <summary>The artifact or package the warning is about, when there is one.</summary>
    public string? Input { get; }
}
