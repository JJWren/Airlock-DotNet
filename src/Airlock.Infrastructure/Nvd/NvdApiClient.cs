using System.Text.Json;
using Airlock.Application.Logging;
using Airlock.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Timeout;

namespace Airlock.Infrastructure.Nvd;

/// <summary>
/// Asks NVD for one page through the resilience pipeline (IR-07, IR-08). Each attempt reads and parses the page inside the
/// attempt, as it streams in, so the attempt timeout and the retries cover the whole answer.
/// </summary>
internal sealed class NvdApiClient(
    IHttpClientFactory httpClientFactory, NvdOptions options, ResiliencePipeline<NvdAttempt> pipeline, TimeProvider timeProvider,
    ILogger logger)
{
    /// <summary>The named client <c>AddAirlockInfrastructure</c> configures.</summary>
    public const string ClientName = "Airlock.Nvd";

    /// <summary>NVD's largest page (IR-07).</summary>
    public const int PageSize = 2_000;

    /// <summary>A page, no records, or a final failure; only cancellation throws.</summary>
    public async Task<NvdAttempt> GetPageAsync(NvdQuery query, int startIndex, CancellationToken cancellationToken)
    {
        var attempt = 0;
        try
        {
            var answer = await pipeline.ExecuteAsync(
                token => SendAsync(query, startIndex, ++attempt, token), cancellationToken);
            return answer is NvdRetryable retryable ? new NvdFailed($"NVD returned {retryable.Status}") : answer;
        }
        catch (TimeoutRejectedException)
        {
            return new NvdFailed("NVD did not answer in time");
        }
        catch (HttpRequestException exception)
        {
            return new NvdFailed($"NVD could not be reached: {ExternalText.Clean(exception.Message)}");
        }
        catch (IOException exception)
        {
            return new NvdFailed($"NVD's answer was cut off: {ExternalText.Clean(exception.Message)}");
        }
    }

    private static string Message(HttpResponseMessage response) =>
        response.Headers.TryGetValues("message", out var values) ? ExternalText.Clean(string.Join(' ', values)) : ExternalText.NoDetails;

    private async ValueTask<NvdAttempt> SendAsync(NvdQuery query, int startIndex, int attempt, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetTimestamp();
        var client = httpClientFactory.CreateClient(ClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, query.ToUri(options.BaseUrl, startIndex, PageSize));

        // IR-07, IR-24: the key travels in the apiKey header and nowhere else.
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            request.Headers.TryAddWithoutValidation("apiKey", options.ApiKey);
        }

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var status = (int)response.StatusCode;
        var answer = await AnswerAsync(response, status, query, cancellationToken);
        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.Requested(query.Parameter, startIndex, status, (long)timeProvider.GetElapsedTime(started).TotalMilliseconds, attempt);
        }

        return answer;
    }

    // IR-08
    private async Task<NvdAttempt> AnswerAsync(
        HttpResponseMessage response, int status, NvdQuery query, CancellationToken cancellationToken)
    {
        switch (status)
        {
            case 200:
                await using (var body = await response.Content.ReadAsStreamAsync(cancellationToken))
                {
                    try
                    {
                        var page = await JsonSerializer.DeserializeAsync(body, NvdJsonContext.Default.NvdCveResponse, cancellationToken);
                        return page is null ? new NvdFailed("NVD's answer was empty") : new NvdPage(page);
                    }
                    catch (JsonException exception)
                    {
                        return new NvdFailed($"NVD's answer could not be read: {ExternalText.Clean(exception.Message)}");
                    }
                }

            case 404:
                // S0 N2: a malformed CPE is a candidate without records, never a failed lookup.
                logger.MalformedCpe(Message(response), query.Value);
                return new NvdNotFound();
            case 403 or 429 or (>= 500 and <= 599):
                return new NvdRetryable(status, RetryAfter(response));
            default:
                return new NvdFailed($"NVD returned {status}");
        }
    }

    private TimeSpan? RetryAfter(HttpResponseMessage response) => response.Headers.RetryAfter switch
    {
        { Delta: { } delta } => delta,
        { Date: { } date } => date - timeProvider.GetUtcNow(),
        _ => null,
    };
}
