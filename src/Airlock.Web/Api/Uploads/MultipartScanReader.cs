using System.Buffers;
using System.Text;
using Airlock.Application.Logging;
using Airlock.Application.Options;
using Airlock.Application.Outcomes;
using Airlock.Application.UseCases;
using Airlock.Domain.Scanning;
using Airlock.Web.Api.Problems;
using Airlock.Web.Logging;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Airlock.Web.Api.Uploads;

/// <summary>How reading a submission ended.</summary>
internal abstract record SubmissionResult;

/// <summary>The scan started (WR-09).</summary>
internal sealed record SubmissionStarted(Scan Scan) : SubmissionResult;

/// <summary>The Application refused the submission.</summary>
internal sealed record SubmissionRefused(Refusal Refusal) : SubmissionResult;

/// <summary>The request itself is not a valid submission (WR-07).</summary>
internal sealed record SubmissionInvalid(string Detail) : SubmissionResult;

/// <summary>The body passed the server's own limit (WR-08).</summary>
internal sealed record SubmissionTooLarge : SubmissionResult;

/// <summary>The client went away, or its upload stalled; nobody is waiting for an answer.</summary>
internal sealed record SubmissionAbandoned(bool Stalled) : SubmissionResult;

/// <summary>
/// Reads a submission part by part over the raw body, never buffering an artifact (WR-06 to WR-08, U4-NFR-02): each
/// artifact streams straight into the reservation, which stages it within the limits.
/// </summary>
internal sealed class MultipartScanReader(IOptions<AirlockOptions> options, TimeProvider timeProvider, ILogger<MultipartScanReader> logger)
{
    internal const string MalformedBodyDetail = "The request body is not valid multipart/form-data";
    internal const string BoundaryDetail = "The multipart boundary is missing or too long";
    internal const string TargetPathTwiceDetail = "targetPath may be given only once";
    internal const string NoFileNameDetail = "Each artifact needs a file name";

    private const string TargetPathField = "targetPath";
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>WR-06 step 2, before the slot is claimed: the boundary, or the problem to answer instead.</summary>
    public ProblemHttpResult? CheckContentType(HttpRequest request, out string boundary)
    {
        ArgumentNullException.ThrowIfNull(request);
        boundary = string.Empty;
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            logger.UnsupportedMediaType();
            return ApiProblems.ForUnsupportedMediaType();
        }

        var value = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        if (string.IsNullOrEmpty(value) || value.Length > ApiLimits.MaxBoundaryLength)
        {
            logger.InvalidSubmission(BoundaryDetail);
            return ApiProblems.ForInvalidRequest(BoundaryDetail);
        }

        boundary = value;
        return null;
    }

    /// <summary>Reads the parts in order and starts the scan, stopping at the first refusal (WR-07).</summary>
    public async Task<SubmissionResult> ReadAndStartAsync(HttpContext context, string boundary, ScanReservation reservation)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(reservation);
        var cancellationToken = context.RequestAborted;

        // U4-NFR-03: the larger limit only now that the slot is held; a request refused earlier keeps the small one.
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } bodySize)
        {
            bodySize.MaxRequestBodySize = options.Value.Limits.MaxBytesPerScan + ApiLimits.FramingAllowance;
        }

        // U4-NFR-04, and MultipartReader's defaults: at most 16 headers and 16 KiB of headers per part.
        await using var body = new IdleTimeoutStream(context.Request.Body, ApiLimits.UploadIdleTimeout, timeProvider);
        var multipart = new MultipartReader(boundary, body);
        string? targetPath = null;
        var artifacts = 0;
        RequestBodyStream? artifactBody = null;
        try
        {
            while (await multipart.ReadNextSectionAsync(cancellationToken) is { } section)
            {
                // Every part is form-data; ASP.NET Core's IsFormDisposition() would also demand that it carries no file.
                var disposition = section.GetContentDispositionHeader();
                if (disposition is null || !disposition.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase))
                {
                    return new SubmissionInvalid(MalformedBodyDetail);
                }

                var name = HeaderUtilities.RemoveQuotes(disposition.Name).Value ?? string.Empty;
                if (string.Equals(name, TargetPathField, StringComparison.OrdinalIgnoreCase))
                {
                    if (targetPath is not null)
                    {
                        return new SubmissionInvalid(TargetPathTwiceDetail);
                    }

                    targetPath = await ReadTextAsync(section.Body, cancellationToken);
                    if (targetPath is null)
                    {
                        return new SubmissionRefused(reservation.RefuseUnreadableTargetPath());
                    }

                    if (artifacts > 0 && !string.IsNullOrWhiteSpace(targetPath))
                    {
                        return new SubmissionRefused(reservation.RefuseBothTargets());
                    }
                }
                else if (IsArtifactsField(name))
                {
                    var fileName = FileNameOf(disposition);
                    if (string.IsNullOrEmpty(fileName))
                    {
                        // A browser sends an empty part with an empty file name for a file input left empty.
                        if (fileName is not null && await IsEmptyAsync(section.Body, cancellationToken))
                        {
                            continue;
                        }

                        return new SubmissionInvalid(NoFileNameDetail);
                    }

                    if (!string.IsNullOrWhiteSpace(targetPath))
                    {
                        return new SubmissionRefused(reservation.RefuseBothTargets());
                    }

                    artifactBody = new RequestBodyStream(section.Body);
                    var added = await reservation.AddArtifactAsync(fileName, artifactBody, cancellationToken);
                    artifactBody = null;
                    if (!added.IsSuccess)
                    {
                        return new SubmissionRefused(added.Refusal!);
                    }

                    artifacts++;
                }
                else
                {
                    return new SubmissionInvalid(
                        $"Unknown form field '{ExternalText.Clean(name, ApiLimits.FieldNameShown)}'; expected 'targetPath' or 'artifacts'");
                }
            }
        }
        catch (Exception exception) when (IsRequestFailure(exception, artifactBody))
        {
            return Classify(exception, body, cancellationToken);
        }

        var started = await reservation.StartAsync(targetPath, cancellationToken);
        return started.IsSuccess ? new SubmissionStarted(started.Value!) : new SubmissionRefused(started.Refusal!);
    }

    private static bool IsArtifactsField(string name) =>
        string.Equals(name, "artifacts", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "artifacts[]", StringComparison.OrdinalIgnoreCase);

    // The decoded filename* when there is one, as .NET's HttpClient sends it, else filename; null when the part has neither.
    private static string? FileNameOf(ContentDispositionHeaderValue disposition)
    {
        if (disposition.FileNameStar.HasValue)
        {
            return HeaderUtilities.RemoveQuotes(disposition.FileNameStar).Value;
        }

        return disposition.FileName.HasValue ? HeaderUtilities.RemoveQuotes(disposition.FileName).Value : null;
    }

    // A cancellation is always the request's — a client gone or an upload stalled. Another failure inside an artifact counts
    // only when reading the request threw; a disk that failed while staging is an unexpected error (WR-05).
    private static bool IsRequestFailure(Exception exception, RequestBodyStream? artifactBody) =>
        exception is OperationCanceledException
        || (exception is IOException or InvalidDataException && (artifactBody is null || artifactBody.ReadFailed));

    private static SubmissionResult Classify(Exception exception, IdleTimeoutStream body, CancellationToken cancellationToken)
    {
        if (body.TimedOut)
        {
            return new SubmissionAbandoned(Stalled: true);
        }

        if (cancellationToken.IsCancellationRequested || exception is OperationCanceledException)
        {
            return new SubmissionAbandoned(Stalled: false);
        }

        return exception is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge }
            ? new SubmissionTooLarge()
            : new SubmissionInvalid(MalformedBodyDetail);
    }

    // WR-07: strict UTF-8, at most 16 KiB; null when the value is longer or not UTF-8.
    private static async Task<string?> ReadTextAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(ApiLimits.TargetPathReadLimit + 1);
        try
        {
            var total = 0;
            while (total <= ApiLimits.TargetPathReadLimit)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(total, ApiLimits.TargetPathReadLimit + 1 - total), cancellationToken);
                if (read == 0)
                {
                    return StrictUtf8.GetString(buffer, 0, total);
                }

                total += read;
            }

            return null;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<bool> IsEmptyAsync(Stream stream, CancellationToken cancellationToken)
    {
        var probe = new byte[1];
        return await stream.ReadAsync(probe, cancellationToken) == 0;
    }
}
