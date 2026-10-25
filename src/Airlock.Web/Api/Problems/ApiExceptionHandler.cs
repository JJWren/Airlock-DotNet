using Airlock.Application.Logging;
using Airlock.Web.Logging;
using Microsoft.AspNetCore.Diagnostics;

namespace Airlock.Web.Api.Problems;

/// <summary>
/// WR-05: an exception under <c>/api</c> becomes a problem without any of its details; the exception goes to the console
/// log only, which the Live Log never shows (U3 IR-15).
/// </summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        // A request the server itself refused to read — a body past its limit, a broken request — keeps its status.
        var status = exception is BadHttpRequestException badRequest ? badRequest.StatusCode : StatusCodes.Status500InternalServerError;
        if (status >= StatusCodes.Status500InternalServerError)
        {
            logger.UnexpectedError(exception, ExternalText.Clean(httpContext.Request.Method), ExternalText.Clean(httpContext.Request.Path));
        }

        httpContext.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = { Status = status },
            Exception = exception,
        });
    }
}
