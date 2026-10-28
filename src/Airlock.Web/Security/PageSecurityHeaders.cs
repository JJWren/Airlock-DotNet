using Airlock.Web.Api;

namespace Airlock.Web.Security;

/// <summary>
/// U5-NFR-08: every answer outside <c>/api</c> says that no other site may frame it and that its type is not to be
/// guessed; all but the API documentation's also carry the pages' Content-Security-Policy, since Scalar's page writes
/// inline script. Set as the answer starts, so pages re-executed for an error or a 404 get them too. <c>/api</c> keeps
/// U4's headers.
/// </summary>
internal sealed class PageSecurityHeaders(RequestDelegate next)
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; "
        + "connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var path = context.Request.Path;
        if (path.StartsWithSegments(ApiEndpoints.ApiPrefix))
        {
            return next(context);
        }

        var withPolicy = !path.StartsWithSegments("/docs") && !path.StartsWithSegments("/openapi");
        context.Response.OnStarting(
            static state =>
            {
                var (response, withPolicy) = ((HttpResponse, bool))state;
                response.Headers.XFrameOptions = "DENY";
                response.Headers.XContentTypeOptions = "nosniff";
                if (withPolicy)
                {
                    response.Headers.ContentSecurityPolicy = ContentSecurityPolicy;
                }

                return Task.CompletedTask;
            },
            (context.Response, withPolicy));
        return next(context);
    }
}