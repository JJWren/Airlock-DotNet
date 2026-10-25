using System.Text;
using System.Text.Json;

namespace Airlock.Web.Tests.Support;

/// <summary>One server-sent event as a client receives it.</summary>
internal sealed record SseEvent(string Name, string Data)
{
    public JsonElement Json
    {
        get
        {
            using var document = JsonDocument.Parse(Data);
            return document.RootElement.Clone();
        }
    }
}

/// <summary>Reads a <c>text/event-stream</c> answer line by line, keeping every line for the format checks.</summary>
internal sealed class SseReader(Stream stream) : IDisposable
{
    private readonly StreamReader reader = new(stream, Encoding.UTF8);

    public List<string> Lines { get; } = [];

    public List<string> Comments { get; } = [];

    /// <summary>The next event, skipping comments; null when the server has ended the stream.</summary>
    public async Task<SseEvent?> NextAsync(CancellationToken cancellationToken = default)
    {
        var token = cancellationToken.CanBeCanceled ? cancellationToken : Test.Token;
        string? name = null;
        string? data = null;
        while (true)
        {
            var line = await reader.ReadLineAsync(token);
            if (line is null)
            {
                return null;
            }

            Lines.Add(line);
            if (line.Length == 0)
            {
                if (name is not null || data is not null)
                {
                    return new SseEvent(name ?? "message", data ?? string.Empty);
                }

                continue;
            }

            if (line.StartsWith(':'))
            {
                Comments.Add(line);
            }
            else if (line.StartsWith("event: ", StringComparison.Ordinal))
            {
                name = line["event: ".Length..];
            }
            else if (line.StartsWith("data: ", StringComparison.Ordinal))
            {
                data = data is null ? line["data: ".Length..] : data + "\n" + line["data: ".Length..];
            }
            else
            {
                throw new InvalidDataException("Not a line of the event stream format: " + line);
            }
        }
    }

    /// <summary>Every event until the server ends the stream.</summary>
    public async Task<List<SseEvent>> ToEndAsync()
    {
        List<SseEvent> events = [];
        while (await NextAsync().Within() is { } next)
        {
            events.Add(next);
        }

        return events;
    }

    public void Dispose() => reader.Dispose();
}
