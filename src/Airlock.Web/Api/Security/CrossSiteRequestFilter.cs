using Airlock.Application.Logging;
using Airlock.Web.Api.Problems;
using Airlock.Web.Logging;

namespace Airlock.Web.Api.Security;

/// <summary>
/// WR-19, Q-U4-3: the <c>POST</c> endpoints refuse a request a browser marks as coming from another site, before anything
/// else happens. curl, CI clients and .NET's HttpClient send neither header and are accepted.
/// </summary>
internal sealed class CrossSiteRequestFilter(ILogger<CrossSiteRequestFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var request = context.HttpContext.Request;
        if (IsCrossSite(request))
        {
            var origin = request.Headers.Origin.ToString();
            logger.CrossSiteRequestRefused(ExternalText.Clean(origin.Length > 0 ? origin : request.Headers["Sec-Fetch-Site"].ToString()));
            return ApiProblems.ForCrossSiteRequest();
        }

        return await next(context);
    }

    // Sec-Fetch-Site when the browser sends it — same-site, which another port on the same host gets, is refused too —
    // else the Origin's host and port against the request's Host, Origin: null included.
    internal static bool IsCrossSite(HttpRequest request)
    {
        var fetchSite = request.Headers["Sec-Fetch-Site"].ToString();
        if (fetchSite.Length > 0)
        {
            return !fetchSite.Equals("same-origin", StringComparison.OrdinalIgnoreCase)
                && !fetchSite.Equals("none", StringComparison.OrdinalIgnoreCase);
        }

        var origin = request.Headers.Origin.ToString();
        if (origin.Length == 0)
        {
            return false;
        }

        return !Uri.TryCreate(origin, UriKind.Absolute, out var originUri)
            || !string.Equals(originUri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }
}
