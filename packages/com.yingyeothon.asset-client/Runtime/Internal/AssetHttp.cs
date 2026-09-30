using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;
using Yingyeothon.Logger;

namespace Yingyeothon.Assets
{
    /// <summary><c>bytes a-b/total</c> or <c>bytes a-b/*</c>.</summary>
    internal sealed class ContentRange
    {
        private static readonly Regex Shape = new Regex(@"^bytes (\d{1,16})-(\d{1,16})/(\d{1,16}|\*)$", RegexOptions.CultureInvariant);
        private static readonly Regex Unsatisfied = new Regex(@"^bytes \*/(\d{1,16})$", RegexOptions.CultureInvariant);

        private ContentRange(long start, long end, long? total)
        {
            Start = start;
            End = end;
            Total = total;
        }

        internal long Start { get; }

        /// <summary>Inclusive.</summary>
        internal long End { get; }

        /// <summary>Null for <c>bytes a-b/*</c>.</summary>
        internal long? Total { get; }

        internal static ContentRange? Parse(string? raw)
        {
            var match = raw == null ? null : Shape.Match(raw.Trim());
            if (match == null || !match.Success)
            {
                return null;
            }

            var start = long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var end = long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            long? total = match.Groups[3].Value == "*" ? (long?)null : long.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            if (end < start || (total.HasValue && end >= total.Value))
            {
                return null;
            }

            return new ContentRange(start, end, total);
        }

        /// <summary>The length a 416 states, <c>bytes */L</c>.</summary>
        internal static long? ParseUnsatisfied(string? raw)
        {
            var match = raw == null ? null : Unsatisfied.Match(raw.Trim());
            return match != null && match.Success ? long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : (long?)null;
        }
    }

    /// <summary>What one request asked for; the only thing besides the status that is logged.</summary>
    internal enum RequestKind
    {
        Whole,
        Head,
        Header,
        Segments,
        Range,
    }

    internal sealed class RequestSpec
    {
        internal RequestSpec(string method, RequestKind kind, long? from = null, long? to = null, string? ifRange = null, bool noCache = false)
        {
            Method = method;
            Kind = kind;
            From = from;
            To = to;
            IfRange = ifRange;
            NoCache = noCache;
        }

        internal string Method { get; }

        internal RequestKind Kind { get; }

        /// <summary>The first byte of a <c>Range</c>; null sends none.</summary>
        internal long? From { get; }

        /// <summary>Inclusive; null leaves the range open-ended.</summary>
        internal long? To { get; }

        internal string? IfRange { get; }

        internal bool NoCache { get; }
    }

    /// <summary>A response whose headers were read and whose body is still to come.</summary>
    internal sealed class Answer
    {
        internal Answer(int status, string? etag, ContentRange? range, long? unsatisfiedTotal, long? length, bool encoded, BodyReader body)
        {
            Status = status;
            ETag = etag;
            Range = range;
            UnsatisfiedTotal = unsatisfiedTotal;
            Length = length;
            Encoded = encoded;
            Body = body;
        }

        internal int Status { get; }

        /// <summary>A strong or weak ETag as sent, when the host exposed one.</summary>
        internal string? ETag { get; }

        internal ContentRange? Range { get; }

        internal long? UnsatisfiedTotal { get; }

        /// <summary><c>Content-Length</c>, when present and numeric.</summary>
        internal long? Length { get; }

        /// <summary>Whether the host said the body is content-encoded.</summary>
        internal bool Encoded { get; }

        internal BodyReader Body { get; }

        /// <summary>A strong ETag may go into <c>If-Range</c>; a weak one never matches there.</summary>
        internal static bool IsStrong(string? etag) => !string.IsNullOrEmpty(etag) && !etag!.StartsWith("W/", StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads a body in exact-sized pieces without holding more of it than one piece: a
    /// download of 256 MiB keeps one 64 KiB segment in memory. Each wait for the next piece
    /// is bounded by the idle timeout, so a connection that went quiet ends as
    /// <c>network</c> rather than hanging.
    /// </summary>
    internal sealed class BodyReader : IDisposable
    {
        private const int ChunkSize = 64 * 1024;

        private readonly IAssetResponse _response;
        private readonly int _status;
        private readonly TimeSpan _idle;
        private readonly Func<bool> _disposed;
        private readonly CancellationToken _cancellationToken;
        private byte[]? _pending;
        private int _pendingOffset;
        private int _pendingCount;
        private bool _finished;

        internal BodyReader(IAssetResponse response, int status, TimeSpan idle, Func<bool> disposed, CancellationToken cancellationToken)
        {
            _response = response;
            _status = status;
            _idle = idle;
            _disposed = disposed;
            _cancellationToken = cancellationToken;
        }

        /// <summary>The next piece as the transport delivered it, or null at the end.</summary>
        internal async Task<ArraySegment<byte>?> NextAsync()
        {
            if (_pending != null)
            {
                var piece = new ArraySegment<byte>(_pending, _pendingOffset, _pendingCount);
                _pending = null;
                return piece;
            }

            if (_finished)
            {
                return null;
            }

            if (_disposed())
            {
                throw new ObjectDisposedException(nameof(IAssetBundleClient));
            }

            var buffer = new byte[ChunkSize];
            int read;
            using (var bound = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken))
            {
                bound.CancelAfter(_idle);
                try
                {
                    // Raced as well as cancelled: a stream that only checks its token on
                    // entry would otherwise leave the idle bound inert.
                    read = await Race(_response.ReadAsync(buffer, 0, buffer.Length, bound.Token), bound.Token);
                }
                catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
                {
                    _finished = true;
                    throw;
                }
                catch (Exception error)
                {
                    _finished = true;
                    if (error is OperationCanceledException)
                    {
                        throw AssetClientException.NetworkError(_status, "the body stalled");
                    }

                    // The transport's message is not the client's to repeat; it is attached.
                    throw AssetClientException.NetworkError(_status, "the body failed mid-transfer", error);
                }
            }

            if (read <= 0)
            {
                _finished = true;
                return null;
            }

            return new ArraySegment<byte>(buffer, 0, read);
        }

        /// <summary>
        /// <paramref name="work"/>, or an <see cref="OperationCanceledException"/> once
        /// <paramref name="cancellationToken"/> fires — the caller's cancellation or the
        /// timeout already set on it; with a context, at its next turn — even if the work
        /// ignores the token. An abandoned
        /// result is handed to <paramref name="late"/> (to release it) and a late failure is
        /// observed, so neither leaks. No timer of its own: the token's is the bound.
        /// </summary>
        /// <remarks>
        /// Never <c>Task.WhenAny</c>: a WebGL player has no thread pool, and a transport that
        /// completes with <c>RunContinuationsAsynchronously</c> — the Unity one does — sends
        /// WhenAny's continuation there, so the read never finishes. Both continuations run on
        /// the caller's synchronization context when it has one, which is Unity's main loop.
        /// </remarks>
        internal static async Task<T> Race<T>(Task<T> work, CancellationToken cancellationToken, Action<T>? late = null)
        {
            if (work.IsCompleted || !cancellationToken.CanBeCanceled)
            {
                return await work;
            }

            // With a context both sides decide there, in arrival order, so a main-loop hitch
            // cannot hand an answer that already arrived to a timer that fired later; and the
            // await then resumes inline, one frame per wait rather than two. Without one, a
            // caller's synchronous Cancel() must not run the rest of the read inside itself.
            var context = SynchronizationContext.Current;
            var scheduler = context == null ? TaskScheduler.Default : TaskScheduler.FromCurrentSynchronizationContext();
            var decided = new TaskCompletionSource<bool>(
                context == null ? TaskCreationOptions.RunContinuationsAsynchronously : TaskCreationOptions.None);
            bool finished;
            using (cancellationToken.Register(() =>
            {
                if (context == null)
                {
                    decided.TrySetResult(false);
                }
                else
                {
                    context.Post(_ => decided.TrySetResult(false), null);
                }
            }))
            {
                _ = work.ContinueWith(
                    _ => decided.TrySetResult(true),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    scheduler);
                finished = await decided.Task;
            }

            if (!finished)
            {
                _ = work.ContinueWith(
                    t =>
                    {
                        if (t.Status == TaskStatus.RanToCompletion)
                        {
                            late?.Invoke(t.Result);
                        }
                        else
                        {
                            _ = t.Exception;
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    scheduler);
                throw new OperationCanceledException(cancellationToken);
            }

            return await work;
        }

        /// <summary>Exactly <paramref name="n"/> bytes; a body that ends first is <c>network</c>.</summary>
        internal async Task<byte[]> ReadExactlyAsync(long n)
        {
            var output = new byte[n];
            long at = 0;
            while (at < n)
            {
                var next = await NextAsync();
                if (!next.HasValue)
                {
                    throw AssetClientException.NetworkError(_status, "the body ended early");
                }

                var piece = next.Value;
                var take = (int)Math.Min(piece.Count, n - at);
                Buffer.BlockCopy(piece.Array!, piece.Offset, output, (int)at, take);
                at += take;
                if (take < piece.Count)
                {
                    _pending = piece.Array;
                    _pendingOffset = piece.Offset + take;
                    _pendingCount = piece.Count - take;
                }
            }

            return output;
        }

        /// <summary>Everything that is left, refusing to hold more than <paramref name="limit"/> bytes.</summary>
        internal async Task<byte[]> RestAsync(long limit, Func<AssetClientException> tooLarge)
        {
            var pieces = new List<ArraySegment<byte>>();
            long size = 0;
            for (; ; )
            {
                var next = await NextAsync();
                if (!next.HasValue)
                {
                    break;
                }

                size += next.Value.Count;
                if (size > limit)
                {
                    throw tooLarge();
                }

                pieces.Add(next.Value);
            }

            var output = new byte[size];
            long at = 0;
            foreach (var piece in pieces)
            {
                Buffer.BlockCopy(piece.Array!, piece.Offset, output, (int)at, piece.Count);
                at += piece.Count;
            }

            return output;
        }

        /// <summary>Stops the transfer; safe to call more than once.</summary>
        public void Dispose()
        {
            _finished = true;
            _pending = null;
            _response.Dispose();
        }
    }

    /// <summary>
    /// The single request choke point: one header assembly, one log line per request.
    /// Logged are the request kind, the path (capped and stripped, since a manifest may have
    /// chosen it), the status and the range — never a URL, a header value or a byte of body.
    /// </summary>
    internal sealed class Requester
    {
        private readonly IAssetTransport _transport;
        private readonly ILogger _logger;
        private readonly TimeSpan _responseTimeout;
        private readonly TimeSpan _bodyIdleTimeout;
        private readonly Func<bool> _disposed;

        internal Requester(IAssetTransport transport, ILogger logger, TimeSpan responseTimeout, TimeSpan bodyIdleTimeout, Func<bool> disposed)
        {
            _transport = transport;
            _logger = logger;
            _responseTimeout = responseTimeout;
            _bodyIdleTimeout = bodyIdleTimeout;
            _disposed = disposed;
        }

        internal async Task<Answer> SendAsync(string url, string path, RequestSpec spec, CancellationToken cancellationToken)
        {
            if (_disposed())
            {
                throw new ObjectDisposedException(nameof(IAssetBundleClient));
            }

            var headers = new List<KeyValuePair<string, string>>(3);
            string? range = null;
            if (spec.From.HasValue)
            {
                range = "bytes=" + spec.From.Value.ToString(CultureInfo.InvariantCulture) + "-"
                    + (spec.To.HasValue ? spec.To.Value.ToString(CultureInfo.InvariantCulture) : string.Empty);
                headers.Add(new KeyValuePair<string, string>("Range", range));
            }

            if (spec.IfRange != null)
            {
                headers.Add(new KeyValuePair<string, string>("If-Range", spec.IfRange));
            }

            if (spec.NoCache)
            {
                headers.Add(new KeyValuePair<string, string>("Cache-Control", "no-cache"));
            }

            var request = new AssetHttpRequest(spec.Method, new Uri(url, UriKind.Absolute), headers, _responseTimeout);
            IAssetResponse response;
            using (var bound = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                bound.CancelAfter(_responseTimeout);
                try
                {
                    response = await BodyReader.Race(_transport.SendAsync(request, bound.Token), bound.Token, r => r.Dispose());
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception error) when (error is OperationCanceledException || error is TimeoutException)
                {
                    // Named, so it is never mistaken for a refused preflight — that shape
                    // (no status, no detail) is what sends a CORS-safe read to the whole file.
                    // A transport with a timer of its own (UnityWebRequest's, the only one
                    // WebGL runs) reports it as a TimeoutException.
                    _logger.Warn("asset request failed", Context(spec.Kind, path).Build());
                    throw AssetClientException.NetworkError(0, "no response in time", error as TimeoutException);
                }
                catch (Exception error)
                {
                    _logger.Warn("asset request failed", Context(spec.Kind, path).Build());
                    throw AssetClientException.NetworkError(0, null, error);
                }
            }

            if (response == null)
            {
                throw AssetClientException.NetworkError(0, null);
            }

            var status = response.Status;
            if (_logger.IsEnabled(LogSeverity.Debug))
            {
                var context = Context(spec.Kind, path).Set("status", (double)status);
                if (range != null)
                {
                    context.Set("range", range);
                }

                _logger.Debug("asset request", context.Build());
            }

            var body = new BodyReader(response, status, _bodyIdleTimeout, _disposed, cancellationToken);
            if (status == 403 || status == 404)
            {
                body.Dispose();
                throw new AssetClientException(AssetErrorCodes.NotFound, status, null, null);
            }

            if (status != 200 && status != 206 && status != 416)
            {
                body.Dispose();
                throw AssetClientException.HttpError(status);
            }

            var encoding = response.GetHeader("Content-Encoding");
            var contentRange = response.GetHeader("Content-Range");
            var length = response.GetHeader("Content-Length");
            long? parsedLength = null;
            if (length != null && Regex.IsMatch(length.Trim(), @"^\d{1,16}$"))
            {
                parsedLength = long.Parse(length.Trim(), CultureInfo.InvariantCulture);
            }

            return new Answer(
                status,
                response.GetHeader("ETag"),
                ContentRange.Parse(contentRange),
                ContentRange.ParseUnsatisfied(contentRange),
                parsedLength,
                encoding != null && !string.Equals(encoding.Trim(), "identity", StringComparison.OrdinalIgnoreCase),
                body);
        }

        internal static JsonObjectBuilder Context(RequestKind kind, string path)
            => Json.Object().Set("kind", KindName(kind)).Set("path", Diagnostic(path));

        private static string KindName(RequestKind kind)
        {
            switch (kind)
            {
                case RequestKind.Whole:
                    return "whole";
                case RequestKind.Head:
                    return "head";
                case RequestKind.Header:
                    return "header";
                case RequestKind.Segments:
                    return "segments";
                default:
                    return "range";
            }
        }

        /// <summary>A path as a log field: capped at 64 characters, controls, format and bidi characters replaced.</summary>
        internal static string Diagnostic(string value)
        {
            const int max = 64;
            var length = Math.Min(value.Length, max);
            var buffer = new char[length];
            for (var i = 0; i < length; i++)
            {
                var c = value[i];
                if (char.IsHighSurrogate(c) && i + 1 < length && char.IsLowSurrogate(value[i + 1]))
                {
                    buffer[i] = c;
                    buffer[i + 1] = value[i + 1];
                    i++;
                    continue;
                }

                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                buffer[i] = category == UnicodeCategory.Control || category == UnicodeCategory.Format
                    || category == UnicodeCategory.LineSeparator || category == UnicodeCategory.ParagraphSeparator
                    || category == UnicodeCategory.Surrogate
                    ? '?'
                    : c;
            }

            return length < value.Length ? new string(buffer) + "…" : new string(buffer);
        }
    }
}
