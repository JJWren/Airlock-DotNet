namespace Airlock.Infrastructure.Nvd;

/// <summary>NVD's answer to one page request (IR-08); statuses are values, so the retry decides without exceptions.</summary>
internal abstract record NvdAttempt
{
    private protected NvdAttempt()
    {
    }
}

/// <summary>A page of records (200).</summary>
internal sealed record NvdPage(NvdCveResponse Response) : NvdAttempt;

/// <summary>A malformed CPE (404): no records (S0 N2).</summary>
internal sealed record NvdNotFound : NvdAttempt;

/// <summary>A 403, 429 or 5xx, retried with backoff; it never leaves the client.</summary>
internal sealed record NvdRetryable(int Status, TimeSpan? RetryAfter) : NvdAttempt;

/// <summary>A failure that is final: any other status, a body that cannot be read, or the last of the retries.</summary>
internal sealed record NvdFailed(string Reason) : NvdAttempt;
