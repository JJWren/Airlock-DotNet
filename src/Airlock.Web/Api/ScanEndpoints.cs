using System.Runtime.CompilerServices;
using Airlock.Application.Outcomes;
using Airlock.Application.UseCases;
using Airlock.Domain.Scanning;
using Airlock.Web.Api.Contracts;
using Airlock.Web.Api.Mapping;
using Airlock.Web.Api.Problems;
using Airlock.Web.Api.Streams;
using Airlock.Web.Api.Uploads;
using Airlock.Web.Logging;

namespace Airlock.Web.Api;

/// <summary>The scan resources (WR-06 to WR-15).</summary>
internal static class ScanEndpoints
{
    public static RouteGroupBuilder MapScanEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/scans", CreateScanAsync)
            .WithName("CreateScan")
            .WithSummary("Submit a scan")
            .WithDescription("A multipart form with either targetPath, a directory on the Airlock host, or one or more artifacts. The scan runs after the answer; follow it with GET /api/v2/scans/{id} or its events.")
            .Produces<ScanAcceptedDto>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .AsStateChanging();

        api.MapGet("/scans/{id}", GetScan)
            .WithName("GetScan")
            .WithSummary("Read a scan")
            .WithDescription("The scan as it stands: its status and phase, and once finished its summary, vulnerable and unverified packages, warnings, failure or cancellation reason. This is also the JSON export.")
            .Produces<ScanDto>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet("/scans/{id}/events", WatchScan)
            .WithName("WatchScan")
            .WithSummary("Follow a scan's events")
            .WithDescription("Server-sent events: snapshot (the scan) first, then phase, progress and log, and finished last, after which the stream ends. A quiet stream receives keep-alive comments.")
            .Produces<Stream>(StatusCodes.Status200OK, "text/event-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet("/scans/{id}/report.pdf", GetReportAsync)
            .WithName("GetScanReport")
            .WithSummary("Download a scan's PDF report")
            .Produces<Stream>(StatusCodes.Status200OK, "application/pdf")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        api.MapPost("/scans/{id}/cancel", CancelAsync)
            .WithName("CancelScan")
            .WithSummary("Cancel a scan")
            .WithDescription("200 with the scan once it has ended, 202 while the cancellation is still under way.")
            .Produces<ScanDto>()
            .Produces<ScanDto>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .AsStateChanging();

        return api;
    }

    // WR-06 to WR-09
    private static async Task<IResult> CreateScanAsync(
        HttpContext context, StartScan startScan, MultipartScanReader reader, ILogger<MultipartScanReader> logger)
    {
        if (reader.CheckContentType(context.Request, out var boundary) is { } notMultipart)
        {
            return notMultipart;
        }

        var reserved = await startScan.ReserveAsync(context.RequestAborted);
        if (!reserved.IsSuccess)
        {
            return ApiProblems.ForRefusal(reserved.Refusal!);
        }

        await using var reservation = reserved.Value!;
        switch (await reader.ReadAndStartAsync(context, boundary, reservation))
        {
            case SubmissionStarted started:
                return TypedResults.Accepted(
                    Location(started.Scan.Id), new ScanAcceptedDto(started.Scan.Id.ToString(), ScanDtoMapper.Status(started.Scan.Status)));
            case SubmissionRefused refused:
                return ApiProblems.ForRefusal(refused.Refusal);
            case SubmissionInvalid invalid:
                logger.InvalidSubmission(invalid.Detail);
                return ApiProblems.ForInvalidRequest(invalid.Detail);
            case SubmissionTooLarge:
                return ApiProblems.ForBodyTooLarge();
            case SubmissionAbandoned { Stalled: true }:
                // U4-NFR-04: nobody is waiting for an answer; disposing the reservation frees the slot.
                logger.UploadStalled((int)ApiLimits.UploadIdleTimeout.TotalSeconds);
                context.Abort();
                return TypedResults.Empty;
            default:
                logger.UploadAbandoned();
                context.Abort();
                return TypedResults.Empty;
        }
    }

    // WR-10
    private static IResult GetScan(string id, GetScan getScan) =>
        ScanId.TryParse(id, out var scanId) && getScan.Execute(scanId) is { } scan
            ? TypedResults.Ok(ScanDtoMapper.ToDto(scan))
            : ApiProblems.ForRefusal(Refusals.ScanNotFound);

    // WR-12, WR-13
    private static IResult WatchScan(string id, GetScan getScan, WatchScan watchScan)
    {
        if (!ScanId.TryParse(id, out var scanId) || getScan.Execute(scanId) is null)
        {
            return ApiProblems.ForRefusal(Refusals.ScanNotFound);
        }

        return new EventStreamResult(token => ScanEventsAsync(watchScan, scanId, token));
    }

    // WR-14
    private static async Task<IResult> GetReportAsync(string id, RenderScanReport renderScanReport, CancellationToken cancellationToken)
    {
        if (!ScanId.TryParse(id, out var scanId))
        {
            return ApiProblems.ForRefusal(Refusals.ScanNotFound);
        }

        var rendered = await renderScanReport.ExecuteAsync(scanId, cancellationToken);
        return rendered.IsSuccess
            ? TypedResults.Bytes(rendered.Value!.Content, rendered.Value.ContentType, rendered.Value.FileName)
            : ApiProblems.ForRefusal(rendered.Refusal!);
    }

    // WR-15: 200 once the scan has ended, 202 while the cancellation is under way.
    private static async Task<IResult> CancelAsync(string id, CancelScan cancelScan, CancellationToken cancellationToken)
    {
        if (!ScanId.TryParse(id, out var scanId))
        {
            return ApiProblems.ForRefusal(Refusals.ScanNotFound);
        }

        var cancelled = await cancelScan.ExecuteAsync(scanId, cancellationToken);
        if (!cancelled.IsSuccess)
        {
            return ApiProblems.ForRefusal(cancelled.Refusal!);
        }

        var dto = ScanDtoMapper.ToDto(cancelled.Value!);
        return cancelled.Value!.IsFinished ? TypedResults.Ok(dto) : TypedResults.Accepted(Location(scanId), dto);
    }

    private static string Location(ScanId id) => $"{ApiEndpoints.GroupPrefix}/scans/{id}";

    private static async IAsyncEnumerable<EventStreamItem> ScanEventsAsync(
        WatchScan watchScan, ScanId id, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var scanEvent in watchScan.SubscribeAsync(id, cancellationToken))
        {
            yield return EventMapper.ForScan(scanEvent);
        }
    }
}
