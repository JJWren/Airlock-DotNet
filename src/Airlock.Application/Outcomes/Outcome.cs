namespace Airlock.Application.Outcomes;

/// <summary>Why a request was refused. The message is written for the person who sees it.</summary>
public sealed record Refusal(RefusalCode Code, string Message);

/// <summary>The kinds of refusal; each maps to one HTTP status in the API.</summary>
public enum RefusalCode
{
    /// <summary>409: another scan holds the active slot.</summary>
    ScanInProgress,

    /// <summary>503: the NVD API key is missing or Syft is unavailable.</summary>
    NotReady,

    /// <summary>400: nothing to scan, both kinds of target, or a directory that cannot be scanned.</summary>
    InvalidTarget,

    /// <summary>400: more files than a scan accepts.</summary>
    TooManyArtifacts,

    /// <summary>413: more bytes than a scan accepts.</summary>
    TooLarge,

    /// <summary>409: Emergency Stop ended the upload before the scan started.</summary>
    ReservationStopped,

    /// <summary>404: no such scan, or it is no longer retained.</summary>
    ScanNotFound,

    /// <summary>409: the scan is still running.</summary>
    ScanNotFinished,

    /// <summary>409: the scan has already finished.</summary>
    ScanAlreadyFinished,

    /// <summary>409: the scan failed or was cancelled, so it has no results.</summary>
    ScanHasNoResults,
}

/// <summary>
/// A use case's answer: a value, or the refusal that took its place. Created through <see cref="Outcome"/>; a default
/// instance is not valid.
/// </summary>
public readonly record struct Outcome<T>
{
    internal Outcome(T? value, Refusal? refusal)
    {
        Value = value;
        Refusal = refusal;
        IsSuccess = refusal is null;
    }

    public T? Value { get; }

    public Refusal? Refusal { get; }

    public bool IsSuccess { get; }
}

/// <summary>Creates <see cref="Outcome{T}"/> values.</summary>
public static class Outcome
{
    public static Outcome<T> Success<T>(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Outcome<T>(value, null);
    }

    public static Outcome<T> Refused<T>(Refusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return new Outcome<T>(default, refusal);
    }

    public static Outcome<T> Refused<T>(RefusalCode code, string message) => Refused<T>(new Refusal(code, message));
}
