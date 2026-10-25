using System.Buffers;
using System.Text;
using Airlock.Web.Logging;
using Microsoft.AspNetCore.Http.Features;

namespace Airlock.Web.Api.Streams;

/// <summary>One server-sent event: its name and one line of compact JSON.</summary>
internal readonly record struct EventStreamItem(string EventType, byte[] Data);

/// <summary>
/// Writes a stream of server-sent events (WR-12, WR-13, WR-18; U4-NFR-05). <c>TypedResults.ServerSentEvents</c> cannot
/// write the keep-alive comment, so Airlock writes the few lines the format needs itself.
/// </summary>
internal sealed class EventStreamResult(Func<CancellationToken, IAsyncEnumerable<EventStreamItem>> source) : IResult
{
    private static readonly byte[] KeepAlive = ": keep-alive\n\n"u8.ToArray();

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var timeProvider = httpContext.RequestServices.GetRequiredService<TimeProvider>();
        var lifetime = httpContext.RequestServices.GetRequiredService<IHostApplicationLifetime>();
        var logger = httpContext.RequestServices.GetRequiredService<ILogger<EventStreamResult>>();

        var response = httpContext.Response;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-store";
        response.Headers["X-Accel-Buffering"] = "no";
        httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        // U4-NFR-05: a closed connection ends the stream at once, and shutdown a second after it begins, which leaves a
        // scan's stream the time to send the Cancelled status the Application records as the host stops.
        using var shutdown = new CancellationTokenSource(Timeout.InfiniteTimeSpan, timeProvider);
        using var stopping = lifetime.ApplicationStopping.Register(() => shutdown.CancelAfter(ApiLimits.StreamShutdownGrace));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(httpContext.RequestAborted, shutdown.Token);
        var token = linked.Token;

        IAsyncEnumerator<EventStreamItem>? events = null;
        Task<bool>? next = null;
        try
        {
            await response.StartAsync(token);
            events = source(token).GetAsyncEnumerator(token);
            next = events.MoveNextAsync().AsTask();
            while (true)
            {
                using var quiet = CancellationTokenSource.CreateLinkedTokenSource(token);
                var keepAlive = Task.Delay(ApiLimits.KeepAliveInterval, timeProvider, quiet.Token);
                if (await Task.WhenAny(next, keepAlive) == keepAlive)
                {
                    await keepAlive;
                    if (!await WriteAsync(response, KeepAlive, token))
                    {
                        return;
                    }

                    continue;
                }

                await quiet.CancelAsync();
                if (!await next || !await WriteAsync(response, Frame(events.Current), token))
                {
                    return;
                }

                next = events.MoveNextAsync().AsTask();
            }
        }
        catch (Exception exception) when (token.IsCancellationRequested && exception is OperationCanceledException or IOException)
        {
            if (shutdown.IsCancellationRequested)
            {
                logger.StreamEndedByShutdown();
            }
        }
        finally
        {
            // A client that stopped reading may not have aborted the request yet, so the source's token is cancelled here:
            // a pending move-next then ends promptly, and the enumerator is disposed after it.
            await linked.CancelAsync();
            if (next is not null)
            {
                try
                {
                    await next;
                }
                catch (Exception exception) when (exception is OperationCanceledException or IOException)
                {
                    // Ended with the stream.
                }
            }

            if (events is not null)
            {
                await events.DisposeAsync();
            }
        }
    }

    // WR-13: an event: line naming it and one data: line of compact JSON.
    private static byte[] Frame(EventStreamItem item)
    {
        var name = Encoding.UTF8.GetBytes(item.EventType);
        var frame = new byte[7 + name.Length + 7 + item.Data.Length + 2];
        var span = frame.AsSpan();
        "event: "u8.CopyTo(span);
        name.CopyTo(span[7..]);
        "\ndata: "u8.CopyTo(span[(7 + name.Length)..]);
        item.Data.CopyTo(span[(14 + name.Length)..]);
        "\n\n"u8.CopyTo(span[^2..]);
        return frame;
    }

    // False once the client is gone.
    private static async Task<bool> WriteAsync(HttpResponse response, byte[] bytes, CancellationToken token)
    {
        response.BodyWriter.Write(bytes);
        var flushed = await response.BodyWriter.FlushAsync(token);
        return !flushed.IsCanceled && !flushed.IsCompleted;
    }
}
