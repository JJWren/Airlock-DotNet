using Airlock.Domain.Text;

namespace Airlock.Domain.Scanning;

public enum ScanFailureCode
{
    SyftFailed,
    SyftTimedOut,
    SyftOutputUnreadable,
    ScanTimedOut,
    Unexpected,
}

/// <summary>Why a scan failed; the reason is safe to show and holds no internal details.</summary>
public sealed record ScanFailure
{
    public ScanFailure(ScanFailureCode code, string reason, string? input = null)
    {
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        Code = code;
        Reason = DomainText.Require(reason, DomainLimits.Message, nameof(reason));
        Input = DomainText.Optional(input, DomainLimits.Path, nameof(input));
    }

    public ScanFailureCode Code { get; }

    public string Reason { get; }

    /// <summary>The directory or artifact involved, when known.</summary>
    public string? Input { get; }
}
