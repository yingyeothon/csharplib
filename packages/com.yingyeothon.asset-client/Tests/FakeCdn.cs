using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Yingyeothon.Assets.Tests
{
    /// <summary>One request as the fake CDN saw it.</summary>
    internal sealed class RecordedRequest
    {
        internal RecordedRequest(string url, string method, IReadOnlyList<KeyValuePair<string, string>> headers)
        {
            Url = url;
            Method = method;
            Headers = headers;
        }

        internal string Url { get; }

        internal string Method { get; }

        internal IReadOnlyList<KeyValuePair<string, string>> Headers { get; }

        internal string? Header(string name)
            => Headers.Where(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Select(h => h.Value).FirstOrDefault();
    }

    /// <summary>
    /// A scripted CDN: objects by URL, Range / If-Range / HEAD answered as CloudFront answers
    /// them, a missing object as 403, and every request recorded so a test asserts exactly
    /// what went on the wire. Every answer settles synchronously, so the blocking NUnit
    /// assertions are safe over it (rules/testing.md).
    /// </summary>
    internal sealed class FakeCdn : IAssetTransport
    {
        private static readonly Regex RangeShape = new Regex(@"^bytes=(\d+)-(\d*)$");
        private readonly Dictionary<string, (byte[] Bytes, string ETag)> _objects = new Dictionary<string, (byte[], string)>();
        private int _generation;

        /// <summary>Answer every GET with the whole object, as a host without Range does.</summary>
        internal bool IgnoreRange { get; set; }

        /// <summary>Serve the range even when If-Range names another ETag.</summary>
        internal bool IgnoreIfRange { get; set; }

        /// <summary>What a cross-origin script sees on the yyt CDN: only ETag and Content-Length.</summary>
        internal bool CrossOrigin { get; set; }

        /// <summary>Send no ETag at all.</summary>
        internal bool NoEtag { get; set; }

        /// <summary>A request carrying Range throws, as a refused preflight does.</summary>
        internal bool RefuseRange { get; set; }

        /// <summary>How the body is cut into reads.</summary>
        internal int ChunkSize { get; set; } = 7000;

        /// <summary>Runs before each answer, with the request's index: mutate the CDN here.</summary>
        internal Action<int, RecordedRequest>? BeforeAnswer { get; set; }

        /// <summary>The status to answer instead, for one call, before any object lookup.</summary>
        internal Func<RecordedRequest, int?>? StatusOverride { get; set; }

        internal List<RecordedRequest> Requests { get; } = new List<RecordedRequest>();

        /// <summary>Responses handed out and not yet disposed; every test should end at 0.</summary>
        internal int OpenBodies { get; private set; }

        internal string Put(string url, byte[] bytes)
        {
            _generation++;
            var etag = "\"etag-" + _generation + "\"";
            _objects[url] = (bytes, etag);
            return etag;
        }

        internal void Remove(string url) => _objects.Remove(url);

        public Task<IAssetResponse> SendAsync(AssetHttpRequest request, CancellationToken cancellationToken)
        {
            var recorded = new RecordedRequest(request.Url.AbsoluteUri, request.Method, request.Headers.ToList());
            Requests.Add(recorded);
            BeforeAnswer?.Invoke(Requests.Count - 1, recorded);
            var range = recorded.Header("Range");
            if (RefuseRange && range != null)
            {
                return Task.FromException<IAssetResponse>(new IOException("preflight refused"));
            }

            var overridden = StatusOverride?.Invoke(recorded);
            if (overridden.HasValue)
            {
                return Task.FromResult<IAssetResponse>(Answer(overridden.Value, new Dictionary<string, string>(), new byte[0]));
            }

            if (!_objects.TryGetValue(recorded.Url, out var found))
            {
                return Task.FromResult<IAssetResponse>(Answer(403, new Dictionary<string, string>(), new byte[0]));
            }

            var (bytes, etag) = found;
            var size = bytes.Length;
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ETag"] = etag,
                ["Content-Type"] = "application/octet-stream",
            };
            if (request.Method == "HEAD")
            {
                headers["Content-Length"] = size.ToString(CultureInfo.InvariantCulture);
                return Task.FromResult<IAssetResponse>(Answer(200, headers, new byte[0]));
            }

            var ifRange = recorded.Header("If-Range");
            var honour = range != null && !IgnoreRange && (ifRange == null || ifRange == etag || IgnoreIfRange);
            if (!honour)
            {
                headers["Content-Length"] = size.ToString(CultureInfo.InvariantCulture);
                return Task.FromResult<IAssetResponse>(Answer(200, headers, bytes));
            }

            var match = RangeShape.Match(range!);
            if (!match.Success)
            {
                throw new InvalidOperationException("fake cdn: bad range " + range);
            }

            var from = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var to = match.Groups[2].Value.Length == 0 ? size - 1 : Math.Min(long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), size - 1);
            if (from >= size)
            {
                headers["Content-Range"] = "bytes */" + size;
                return Task.FromResult<IAssetResponse>(Answer(416, headers, new byte[0]));
            }

            var body = bytes.AsSpan((int)from, (int)(to - from + 1)).ToArray();
            headers["Content-Length"] = body.Length.ToString(CultureInfo.InvariantCulture);
            headers["Content-Range"] = "bytes " + from + "-" + to + "/" + size;
            return Task.FromResult<IAssetResponse>(Answer(206, headers, body));
        }

        private Response Answer(int status, Dictionary<string, string> headers, byte[] body)
        {
            var visible = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in headers)
            {
                if (CrossOrigin && !string.Equals(header.Key, "ETag", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (NoEtag && string.Equals(header.Key, "ETag", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                visible[header.Key] = header.Value;
            }

            OpenBodies++;
            return new Response(this, status, visible, body);
        }

        private sealed class Response : IAssetResponse
        {
            private readonly FakeCdn _cdn;
            private readonly Dictionary<string, string> _headers;
            private readonly byte[] _body;
            private int _at;
            private bool _disposed;

            internal Response(FakeCdn cdn, int status, Dictionary<string, string> headers, byte[] body)
            {
                _cdn = cdn;
                Status = status;
                _headers = headers;
                _body = body;
            }

            public int Status { get; }

            public string? GetHeader(string name) => _headers.TryGetValue(name, out var value) ? value : null;

            public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                var n = Math.Min(Math.Min(count, _cdn.ChunkSize), _body.Length - _at);
                Buffer.BlockCopy(_body, _at, buffer, offset, n);
                _at += n;
                return Task.FromResult(n);
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _cdn.OpenBodies--;
                }
            }
        }
    }

    /// <summary>A capturing sink: what a download wrote, in order, and every reset.</summary>
    internal sealed class MemorySink : IAssetSink
    {
        private readonly MemoryStream _bytes = new MemoryStream();

        internal List<string> Events { get; } = new List<string>();

        internal byte[] Bytes => _bytes.ToArray();

        /// <summary>Pre-fills the sink as an earlier, interrupted download left it.</summary>
        internal void Seed(byte[] bytes) => _bytes.Write(bytes, 0, bytes.Length);

        public Task WriteAsync(ArraySegment<byte> chunk, CancellationToken cancellationToken)
        {
            _bytes.Write(chunk.Array!, chunk.Offset, chunk.Count);
            Events.Add("write:" + chunk.Count);
            return Task.CompletedTask;
        }

        public Task ResetAsync(CancellationToken cancellationToken)
        {
            _bytes.SetLength(0);
            Events.Add("reset");
            return Task.CompletedTask;
        }
    }
}
