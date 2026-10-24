using System.Threading.RateLimiting;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Polly.Timeout;

namespace Airlock.Infrastructure.Nvd;

/// <summary>
/// The resilience around each page request, outermost first: the retry, the pacer, the attempt timeout (U3-NFR-02). Every
/// attempt, retries included, takes its turn, and waiting for a turn never counts against an attempt's 30 seconds.
/// </summary>
internal static class NvdPipeline
{
    public const int MaxAttempts = 5;

    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinRetryAfter = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);

    public static ResiliencePipeline<NvdAttempt> Build(RateLimiter pacer, TimeProvider timeProvider, Func<double> random, ILogger logger) =>
        new ResiliencePipelineBuilder<NvdAttempt> { TimeProvider = timeProvider }
            .AddRetry(new RetryStrategyOptions<NvdAttempt>
            {
                MaxRetryAttempts = MaxAttempts - 1,
                ShouldHandle = arguments => ValueTask.FromResult(IsRetryable(arguments.Outcome)),
                DelayGenerator = arguments => ValueTask.FromResult<TimeSpan?>(
                    Delay(arguments.AttemptNumber, (arguments.Outcome.Result as NvdRetryable)?.RetryAfter, random)),
                OnRetry = arguments =>
                {
                    logger.Retrying(Describe(arguments.Outcome), arguments.RetryDelay.TotalSeconds, arguments.AttemptNumber + 2, MaxAttempts);
                    return default;
                },
            })
            .AddRateLimiter(pacer)
            .AddTimeout(AttemptTimeout)
            .Build();

    // U3-NFR-02: a usable Retry-After of 1 to 60 seconds when NVD sends one; otherwise 2 seconds doubling up to 30, ±20 %
    // jitter — so a Retry-After of 0, which is no usable delay (S0 N9), waits at least 1.6 seconds.
    internal static TimeSpan Delay(int retry, TimeSpan? retryAfter, Func<double> random)
    {
        if (retryAfter is { } after && after >= MinRetryAfter)
        {
            return after > MaxRetryAfter ? MaxRetryAfter : after;
        }

        var backoff = FirstDelay * Math.Pow(2, Math.Min(retry, 8));
        return (backoff > MaxDelay ? MaxDelay : backoff) * (0.8 + (0.4 * random()));
    }

    // IR-08: 403, 429, 5xx, a network error or the attempt timeout; a body that cannot be read is never retried.
    private static bool IsRetryable(Outcome<NvdAttempt> outcome) =>
        outcome.Result is NvdRetryable
        || outcome.Exception is HttpRequestException or IOException or TimeoutRejectedException;

    private static string Describe(Outcome<NvdAttempt> outcome) => outcome switch
    {
        { Result: NvdRetryable retryable } => $"answered {retryable.Status}",
        { Exception: TimeoutRejectedException } => "did not answer in time",
        _ => "could not be reached",
    };
}
