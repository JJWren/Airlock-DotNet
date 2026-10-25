using System.Diagnostics;
using Airlock.Application.Logging;
using Airlock.Application.Outcomes;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Airlock.Web.Api.Problems;

/// <summary>One kind of problem of the catalogue (domain-entities.md, problems).</summary>
internal sealed record ProblemKind(string Slug, int Status, string Title)
{
    public string Type => ApiProblems.TypeBase + Slug;
}

/// <summary>Every error answer under <c>/api</c> is one of these problems (WR-03 to WR-05, Q-U4-2).</summary>
internal static class ApiProblems
{
    public const string TypeBase = "https://github.com/JJWren/Airlock-DotNet/blob/main/docs/api/problems.md#";

    public static readonly ProblemKind ScanInProgress = new("scan-in-progress", StatusCodes.Status409Conflict, "Scan in progress");
    public static readonly ProblemKind NotReady = new("not-ready", StatusCodes.Status503ServiceUnavailable, "Airlock is not ready");
    public static readonly ProblemKind InvalidTarget = new("invalid-target", StatusCodes.Status400BadRequest, "Invalid scan target");
    public static readonly ProblemKind TooManyArtifacts = new("too-many-artifacts", StatusCodes.Status400BadRequest, "Too many artifacts");
    public static readonly ProblemKind TooLarge = new("too-large", StatusCodes.Status413PayloadTooLarge, "Upload too large");
    public static readonly ProblemKind UploadStopped = new("upload-stopped", StatusCodes.Status409Conflict, "Upload stopped");
    public static readonly ProblemKind ScanNotFound = new("scan-not-found", StatusCodes.Status404NotFound, "Scan not found");
    public static readonly ProblemKind ScanNotFinished = new("scan-not-finished", StatusCodes.Status409Conflict, "Scan still running");
    public static readonly ProblemKind ScanAlreadyFinished = new("scan-already-finished", StatusCodes.Status409Conflict, "Scan already finished");
    public static readonly ProblemKind ScanHasNoResults = new("scan-has-no-results", StatusCodes.Status409Conflict, "Scan has no results");
    public static readonly ProblemKind InvalidRequest = new("invalid-request", StatusCodes.Status400BadRequest, "Invalid request");
    public static readonly ProblemKind UnsupportedMediaType = new("unsupported-media-type", StatusCodes.Status415UnsupportedMediaType, "Unsupported media type");
    public static readonly ProblemKind CrossSiteRequest = new("cross-site-request", StatusCodes.Status403Forbidden, "Cross-site request refused");
    public static readonly ProblemKind NotFound = new("not-found", StatusCodes.Status404NotFound, "Not found");
    public static readonly ProblemKind MethodNotAllowed = new("method-not-allowed", StatusCodes.Status405MethodNotAllowed, "Method not allowed");
    public static readonly ProblemKind UnexpectedError = new("unexpected-error", StatusCodes.Status500InternalServerError, "Unexpected error");

    /// <summary>The whole catalogue, in the order of <c>docs/api/problems.md</c>.</summary>
    public static IReadOnlyList<ProblemKind> All { get; } =
    [
        ScanInProgress, NotReady, InvalidTarget, TooManyArtifacts, TooLarge, UploadStopped, ScanNotFound, ScanNotFinished,
        ScanAlreadyFinished, ScanHasNoResults, InvalidRequest, UnsupportedMediaType, CrossSiteRequest, NotFound,
        MethodNotAllowed, UnexpectedError,
    ];

    public const string UnexpectedErrorDetail = "Unexpected error — see the server log";

    // WR-03
    public static ProblemKind For(RefusalCode code) => code switch
    {
        RefusalCode.ScanInProgress => ScanInProgress,
        RefusalCode.NotReady => NotReady,
        RefusalCode.InvalidTarget => InvalidTarget,
        RefusalCode.TooManyArtifacts => TooManyArtifacts,
        RefusalCode.TooLarge => TooLarge,
        RefusalCode.ReservationStopped => UploadStopped,
        RefusalCode.ScanNotFound => ScanNotFound,
        RefusalCode.ScanNotFinished => ScanNotFinished,
        RefusalCode.ScanAlreadyFinished => ScanAlreadyFinished,
        RefusalCode.ScanHasNoResults => ScanHasNoResults,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, null),
    };

    /// <summary>A refusal as its problem; the detail is the refusal's message, unchanged.</summary>
    public static ProblemHttpResult ForRefusal(Refusal refusal)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        return Problem(For(refusal.Code), refusal.Message);
    }

    public static ProblemHttpResult ForInvalidRequest(string detail) => Problem(InvalidRequest, detail);

    public static ProblemHttpResult ForUnsupportedMediaType() => Problem(UnsupportedMediaType, "Scans are submitted as multipart/form-data");

    public static ProblemHttpResult ForCrossSiteRequest() => Problem(CrossSiteRequest, "Requests from other websites are not accepted");

    public static ProblemHttpResult ForBodyTooLarge() => Problem(TooLarge, "The request body is larger than this server accepts");

    /// <summary>
    /// WR-04: problems ASP.NET Core writes itself — an unknown address, a wrong method, a body cut off, an unexpected
    /// exception — get their kind from the status, and every problem keeps exactly its five members.
    /// </summary>
    public static void Customize(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var problem = context.ProblemDetails;
        if (problem.Type is null || !problem.Type.StartsWith(TypeBase, StringComparison.Ordinal))
        {
            var kind = ForStatus(problem.Status ?? context.HttpContext.Response.StatusCode);
            problem.Type = kind.Type;
            problem.Title = kind.Title;
            problem.Status = kind.Status;
            problem.Detail = DetailFor(kind, context.HttpContext);
        }

        var traceId = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        problem.Instance = null;
        problem.Extensions.Clear();
        problem.Extensions["traceId"] = traceId;
    }

    private static ProblemHttpResult Problem(ProblemKind kind, string detail) =>
        TypedResults.Problem(detail, statusCode: kind.Status, title: kind.Title, type: kind.Type);

    private static ProblemKind ForStatus(int status) => status switch
    {
        StatusCodes.Status400BadRequest => InvalidRequest,
        StatusCodes.Status404NotFound => NotFound,
        StatusCodes.Status405MethodNotAllowed => MethodNotAllowed,
        StatusCodes.Status413PayloadTooLarge => TooLarge,
        StatusCodes.Status415UnsupportedMediaType => UnsupportedMediaType,
        _ => UnexpectedError,
    };

    private static string DetailFor(ProblemKind kind, HttpContext httpContext)
    {
        if (kind == MethodNotAllowed)
        {
            return $"This endpoint does not accept {ExternalText.Clean(httpContext.Request.Method, ApiLimits.FieldNameShown)}";
        }

        if (kind == NotFound)
        {
            return "No API endpoint at this address";
        }

        if (kind == TooLarge)
        {
            return "The request body is larger than this server accepts";
        }

        if (kind == InvalidRequest)
        {
            return "The request is not valid";
        }

        return kind == UnsupportedMediaType ? "Scans are submitted as multipart/form-data" : UnexpectedErrorDetail;
    }
}
