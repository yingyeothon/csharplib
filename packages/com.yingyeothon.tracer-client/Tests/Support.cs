using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Yingyeothon.Codec;
using Yingyeothon.Logger;

namespace Yingyeothon.Tracer.Tests
{
    /// <summary>The fixture values the service repository's golden envelopes use; never a real key.</summary>
    internal static class Fixture
    {
        internal const string WriteKey = "fixturewritekeyfixturewritekey00";

        internal const string Dsn = "https://" + WriteKey + "@trace.invalid/trc_fixture01";
    }

    /// <summary>One recorded request, its body decompressed.</summary>
    internal sealed class SentRequest
    {
        internal SentRequest(TracerHttpRequest request, byte[] body)
        {
            Request = request;
            Body = body;
        }

        internal TracerHttpRequest Request { get; }

        /// <summary>The envelope bytes, gunzipped when the request said gzip.</summary>
        internal byte[] Body { get; }

        internal string? Header(string name)
            => Request.Headers.Where(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Select(h => (string?)h.Value).FirstOrDefault();

        internal ParsedEnvelope Envelope => ParsedEnvelope.Parse(Body);
    }

    /// <summary>
    /// A scripted ingest. Each request takes the next scripted answer; with none left it
    /// answers 200. <see cref="Hang"/> leaves a request pending until the test releases it.
    /// </summary>
    internal sealed class FakeTransport : ITracerTransport
    {
        private readonly Queue<Func<TracerHttpRequest, CancellationToken, Task<TracerHttpResponse>>> _script =
            new Queue<Func<TracerHttpRequest, CancellationToken, Task<TracerHttpResponse>>>();

        internal List<SentRequest> Sent { get; } = new List<SentRequest>();

        internal TaskCompletionSource<TracerHttpResponse>? Pending { get; private set; }

        internal CancellationToken LastToken { get; private set; }

        internal FakeTransport Answer(int status, string? retryAfter = null)
        {
            _script.Enqueue((r, c) => Task.FromResult(new TracerHttpResponse(status, retryAfter)));
            return this;
        }

        internal FakeTransport Throw()
        {
            // The message holds the credential and the URL, so a diagnostic that logged it would show.
            _script.Enqueue((r, c) => throw new IOException("no route to " + Fixture.Dsn));
            return this;
        }

        internal FakeTransport Fault()
        {
            _script.Enqueue((r, c) =>
            {
                var source = new TaskCompletionSource<TracerHttpResponse>();
                source.SetException(new IOException("reset"));
                return source.Task;
            });
            return this;
        }

        internal FakeTransport Hang()
        {
            _script.Enqueue((r, c) =>
            {
                Pending = new TaskCompletionSource<TracerHttpResponse>();
                return Pending.Task;
            });
            return this;
        }

        public Task<TracerHttpResponse> SendAsync(TracerHttpRequest request, CancellationToken cancellationToken)
        {
            LastToken = cancellationToken;
            var gzip = request.Headers.Any(h => h.Key == "Content-Encoding" && h.Value == "gzip");
            Sent.Add(new SentRequest(request, gzip ? Gunzip(request.Body) : request.Body));
            return _script.Count == 0
                ? Task.FromResult(new TracerHttpResponse(200, null))
                : _script.Dequeue()(request, cancellationToken);
        }

        internal static byte[] Gunzip(byte[] body)
        {
            using (var input = new GZipStream(new MemoryStream(body), CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                input.CopyTo(output);
                return output.ToArray();
            }
        }
    }

    /// <summary>An item as tracer's <c>envelope.Reader</c> reads it.</summary>
    internal sealed class ParsedItem
    {
        internal ParsedItem(JsonValue header, byte[] payload)
        {
            Header = header;
            Payload = payload;
        }

        internal JsonValue Header { get; }

        internal byte[] Payload { get; }

        internal string Type => Header.GetString("type") ?? string.Empty;

        internal JsonValue Json => Codec.Json.Parse(Encoding.UTF8.GetString(Payload));
    }

    /// <summary>
    /// An envelope parsed the way tracer reads one: a header line, then per item a header
    /// line, exactly <c>length</c> payload bytes and a newline. Every item here must carry
    /// a <c>length</c>.
    /// </summary>
    internal sealed class ParsedEnvelope
    {
        private ParsedEnvelope(JsonValue header, List<ParsedItem> items)
        {
            Header = header;
            Items = items;
        }

        internal JsonValue Header { get; }

        internal List<ParsedItem> Items { get; }

        internal static ParsedEnvelope Parse(byte[] body)
        {
            var at = 0;
            string Line()
            {
                var end = Array.IndexOf(body, (byte)'\n', at);
                Assert.That(end, Is.GreaterThanOrEqualTo(0), "every line ends with a newline");
                var text = Encoding.UTF8.GetString(body, at, end - at);
                at = end + 1;
                return text;
            }

            var header = Json.Parse(Line());
            var items = new List<ParsedItem>();
            while (at < body.Length)
            {
                var itemHeader = Json.Parse(Line());
                var length = itemHeader.GetNumber("length");
                Assert.That(length.HasValue, Is.True, "every item has an explicit length");
                var payload = new byte[(int)length!.Value];
                Array.Copy(body, at, payload, 0, payload.Length);
                at += payload.Length;
                Assert.That(body[at], Is.EqualTo((byte)'\n'), "a payload is followed by a newline");
                at++;
                items.Add(new ParsedItem(itemHeader, payload));
            }

            return new ParsedEnvelope(header, items);
        }
    }

    /// <summary>Byte values that count up from a start byte (ids are exact), or one fixed value (jitter is exact).</summary>
    internal sealed class SequenceRandom
    {
        private int _next;
        private readonly double? _fixed;

        internal SequenceRandom(int start = 0x30, double? fixedValue = null)
        {
            _next = start;
            _fixed = fixedValue;
        }

        internal double Next() => _fixed ?? (_next++ % 256) / 256.0 + 1e-9;
    }

    /// <summary>A monotonic clock the test advances, and a wall clock that steps 1 ms per read.</summary>
    internal sealed class Clocks
    {
        private long _wallTicks = new DateTimeOffset(2026, 11, 8, 6, 42, 16, 4, TimeSpan.Zero).UtcTicks;

        internal double Now { get; set; } = 1000;

        internal DateTimeOffset Wall()
        {
            var now = new DateTimeOffset(_wallTicks, TimeSpan.Zero);
            _wallTicks += TimeSpan.TicksPerMillisecond;
            return now;
        }
    }

    /// <summary>Records every record in full.</summary>
    internal sealed class CapturingLogWriter : ILogger
    {
        internal List<string> Lines { get; } = new List<string>();

        internal string Text => string.Join("\n", Lines);

        public LogSeverity Severity { get; set; } = LogSeverity.Debug;

        public bool IsEnabled(LogSeverity severity) => severity >= Severity;

        public Action? OnWrite { get; set; }

        public void Debug(string message, JsonValue? context = null) => Add(LogSeverity.Debug, message, context);

        public void Info(string message, JsonValue? context = null) => Add(LogSeverity.Info, message, context);

        public void Warn(string message, JsonValue? context = null) => Add(LogSeverity.Warn, message, context);

        public void Error(string message, JsonValue? context = null) => Add(LogSeverity.Error, message, context);

        private void Add(LogSeverity severity, string message, JsonValue? context)
        {
            Lines.Add(LogWriters.Format(severity, message, context));
            OnWrite?.Invoke();
        }
    }

    /// <summary>A client over a fake transport with fixed ids and clocks.</summary>
    internal sealed class Harness
    {
        internal Harness(Action<TracerOptions>? configure = null, double? jitter = null)
        {
            Random = new SequenceRandom(0x30, jitter);
            var options = new TracerOptions
            {
                Dsn = Fixture.Dsn,
                Release = "playground@0.1.0+1",
                Dist = "abc1234",
                Environment = "debug",
                UserId = "00000000000000000000000000000000",
                Transport = Transport,
                Logger = Log,
                NowMillis = () => Clocks.Now,
                UtcNow = Clocks.Wall,
                Random = Random.Next,
            };
            configure?.Invoke(options);
            Client = TracerClient.Create(options);
        }

        internal FakeTransport Transport { get; } = new FakeTransport();

        internal CapturingLogWriter Log { get; } = new CapturingLogWriter();

        internal Clocks Clocks { get; } = new Clocks();

        internal SequenceRandom Random { get; }

        internal ITracerClient Client { get; }

        /// <summary>Advances the monotonic clock, then polls.</summary>
        internal void Advance(double ms)
        {
            Clocks.Now += ms;
            Client.Poll();
        }
    }
}
