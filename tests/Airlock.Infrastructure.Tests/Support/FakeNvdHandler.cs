using System.Globalization;
using System.Net;
using System.Text;

namespace Airlock.Infrastructure.Tests.Support;

/// <summary>
/// NVD for the tests (U3-NFR-12): answers from the recorded S0 responses by query value, can answer a sequence of
/// statuses, stall a body or fail, and records every request with its time on the fake clock.
/// </summary>
internal sealed class FakeNvdHandler(TimeProvider timeProvider) : HttpMessageHandler
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, Queue<Func<CancellationToken, Task<HttpResponseMessage>>>> answers = new(StringComparer.Ordinal);
    private readonly List<NvdRequestRecord> requests = [];

    public IReadOnlyList<NvdRequestRecord> Requests
    {
        get
        {
            lock (gate)
            {
                return [.. requests];
            }
        }
    }

    /// <summary>Recorded answer of <c>Fixtures/Nvd/&lt;fixture&gt;</c>: its status, kept headers and body.</summary>
    public static HttpResponseMessage Recorded(string fixture)
    {
        var lines = File.ReadAllLines(Fixtures.NvdHeaders(fixture));
        var status = int.Parse(lines[0].Split(' ')[1], CultureInfo.InvariantCulture);
        var response = new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new ByteArrayContent(File.ReadAllBytes(Fixtures.NvdBody(fixture))),
        };
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            var (name, value) = (line[..colon].Trim(), line[(colon + 1)..].Trim());
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                response.Content.Headers.TryAddWithoutValidation(name, value);
            }
            else
            {
                response.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return response;
    }

    public static HttpResponseMessage Status(int status, string? retryAfter = null)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(string.Empty) };
        if (retryAfter is not null)
        {
            response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
        }

        return response;
    }

    public static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>A 200 whose body sends nothing until the request is cancelled.</summary>
    public static HttpResponseMessage Stalled() =>
        new(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) };

    /// <summary>Answers each request whose cpeName or virtualMatchString is <paramref name="value"/> with the next answer; the last repeats.</summary>
    public FakeNvdHandler On(string value, params Func<CancellationToken, Task<HttpResponseMessage>>[] sequence)
    {
        lock (gate)
        {
            answers[value] = new Queue<Func<CancellationToken, Task<HttpResponseMessage>>>(sequence);
        }

        return this;
    }

    public FakeNvdHandler On(string value, params Func<HttpResponseMessage>[] sequence) =>
        On(value, [.. sequence.Select(answer => (Func<CancellationToken, Task<HttpResponseMessage>>)(_ => Task.FromResult(answer())))]);

    public FakeNvdHandler OnRecorded(string value, string fixture) => On(value, () => Recorded(fixture));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("A request without a URI.");
        var query = Query(uri);
        var parameter = query.ContainsKey("cpeName") ? "cpeName" : "virtualMatchString";
        var value = query.GetValueOrDefault(parameter) ?? string.Empty;
        var startIndex = int.Parse(query.GetValueOrDefault("startIndex") ?? "0", CultureInfo.InvariantCulture);
        var apiKey = request.Headers.TryGetValues("apiKey", out var keys) ? string.Join(",", keys) : null;
        Func<CancellationToken, Task<HttpResponseMessage>> answer;
        lock (gate)
        {
            requests.Add(new NvdRequestRecord(uri, parameter, value, startIndex, apiKey, timeProvider.GetUtcNow()));
            var key = answers.ContainsKey(value + "@" + startIndex) ? value + "@" + startIndex : value;
            if (!answers.TryGetValue(key, out var queue))
            {
                answer = _ => Task.FromResult(Recorded("cpe-unknown"));
            }
            else
            {
                answer = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
            }
        }

        return await answer(cancellationToken);
    }

    private static Dictionary<string, string> Query(Uri uri)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = part.IndexOf('=', StringComparison.Ordinal);
            var name = equals < 0 ? part : part[..equals];
            values[Uri.UnescapeDataString(name)] = equals < 0 ? string.Empty : Uri.UnescapeDataString(part[(equals + 1)..]);
        }

        return values;
    }

    private sealed class StallingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

/// <summary>One request NVD received: its query, the apiKey header, and when it arrived on the clock.</summary>
internal sealed record NvdRequestRecord(Uri Uri, string Parameter, string Value, int StartIndex, string? ApiKey, DateTimeOffset At);

/// <summary>An <see cref="IHttpClientFactory"/> whose clients all go to one handler.</summary>
internal sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
}
