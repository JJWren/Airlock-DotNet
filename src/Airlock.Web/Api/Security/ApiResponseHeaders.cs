namespace Airlock.Web.Api.Security;

/// <summary>
/// U4-NFR-06: every answer under <c>/api</c> carries <c>nosniff</c>, because its JSON is written unescaped, and
/// <c>no-store</c>, so no browser or proxy shows a stale status (WR-02). Set as the answer starts, so problems written by
/// the exception handler get them too.
/// </summary>
internal sealed class ApiResponseHeaders(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.OnStarting(
            static state =>
            {
                var headers = ((HttpResponse)state).Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.CacheControl = "no-store";
                return Task.CompletedTask;
            },
            context.Response);
        return next(context);
    }
}
