using System.Runtime.CompilerServices;
using Airlock.Application.UseCases;
using Airlock.Web.Api.Mapping;
using Airlock.Web.Api.Streams;

namespace Airlock.Web.Api;

/// <summary>The Live Log stream (WR-18).</summary>
internal static class LogEndpoints
{
    public static RouteGroupBuilder MapLogEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/logs/stream", Stream)
            .WithName("WatchLiveLog")
            .WithSummary("Follow the Live Log")
            .WithDescription("Server-sent log events: the last 200 lines first, then every line as it is written. The stream ends only when the client closes it or the server stops.")
            .Produces<Stream>(StatusCodes.Status200OK, "text/event-stream");
        return api;
    }

    private static EventStreamResult Stream(WatchLiveLog watchLiveLog) => new(token => LiveLogAsync(watchLiveLog, token));

    private static async IAsyncEnumerable<EventStreamItem> LiveLogAsync(
        WatchLiveLog watchLiveLog, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var entry in watchLiveLog.SubscribeAsync(cancellationToken))
        {
            yield return EventMapper.ForLiveLog(entry);
        }
    }
}
