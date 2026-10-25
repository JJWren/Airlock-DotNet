using Airlock.Web.Api.Problems;
using Airlock.Web.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace Airlock.Web.Tests.Contract;

// WR-04, WR-05 — problems ASP.NET Core writes itself get their kind from the status and keep five members.
public sealed class ProblemCustomizationTests
{
    [Theory]
    [InlineData(400, "invalid-request", "The request is not valid")]
    [InlineData(404, "not-found", "No API endpoint at this address")]
    [InlineData(413, "too-large", "The request body is larger than this server accepts")]
    [InlineData(415, "unsupported-media-type", "Scans are submitted as multipart/form-data")]
    [InlineData(500, "unexpected-error", "Unexpected error — see the server log")]
    [InlineData(502, "unexpected-error", "Unexpected error — see the server log")]
    public void A_framework_problem_gets_its_kind_from_its_status(int status, string slug, string detail)
    {
        var context = Context(new ProblemDetails { Status = status, Title = "Framework title", Detail = "framework detail with a stack trace", Instance = "/api/v2/x" });
        context.ProblemDetails.Extensions["exception"] = "System.InvalidOperationException: secret";

        ApiProblems.Customize(context);

        Assert.Equal(ApiProblems.TypeBase + slug, context.ProblemDetails.Type);
        Assert.Equal(detail, context.ProblemDetails.Detail);
        Assert.Null(context.ProblemDetails.Instance);
        Assert.Equal(["traceId"], context.ProblemDetails.Extensions.Keys);
    }

    [Fact]
    public void A_problem_of_the_catalogue_keeps_its_words()
    {
        var context = Context(new ProblemDetails
        {
            Status = 409,
            Type = ApiProblems.ScanInProgress.Type,
            Title = ApiProblems.ScanInProgress.Title,
            Detail = "A scan is already in progress",
        });

        ApiProblems.Customize(context);

        Assert.Equal("A scan is already in progress", context.ProblemDetails.Detail);
        Assert.Equal("Scan in progress", context.ProblemDetails.Title);
    }

    [Fact]
    public void A_status_without_a_problem_status_is_read_from_the_response()
    {
        var context = Context(new ProblemDetails());
        context.HttpContext.Response.StatusCode = 405;
        context.HttpContext.Request.Method = "DELETE";

        ApiProblems.Customize(context);

        Assert.Equal(ApiProblems.MethodNotAllowed.Type, context.ProblemDetails.Type);
        Assert.Equal("This endpoint does not accept DELETE", context.ProblemDetails.Detail);
    }

    [Fact]
    public async Task A_body_the_server_refused_to_read_keeps_its_status_and_is_not_logged_as_unexpected()
    {
        var writer = new RecordingProblemDetailsService();
        var handler = new ApiExceptionHandler(writer, NullLogger<ApiExceptionHandler>.Instance);
        var context = new DefaultHttpContext();

        var handled = await handler.TryHandleAsync(context, new BadHttpRequestException("Request body too large.", 413), Test.Token);

        Assert.True(handled);
        Assert.Equal(413, context.Response.StatusCode);
        Assert.Equal(413, writer.Written!.ProblemDetails.Status);
    }

    private static ProblemDetailsContext Context(ProblemDetails problem) =>
        new() { HttpContext = new DefaultHttpContext(), ProblemDetails = problem };

    private sealed class RecordingProblemDetailsService : IProblemDetailsService
    {
        public ProblemDetailsContext? Written { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            Written = context;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
        {
            Written = context;
            return ValueTask.FromResult(true);
        }
    }
}
