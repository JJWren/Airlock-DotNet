using Airlock.Domain.Text;

namespace Airlock.Domain.Scanning;

public enum ScanWarningCode
{
    NoPackagesIdentified,
    DistributionBackportsNotConsidered,
    CvesWithoutSeverity,
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
