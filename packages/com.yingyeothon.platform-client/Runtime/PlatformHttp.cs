using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;
using Yingyeothon.KvStore;
using Yingyeothon.Logger;

namespace Yingyeothon.Platform
{
    /// <summary>A resource driven on its owner's thread. Poll is never reentrant.</summary>
    public interface IPollable : IDisposable
    {
        /// <summary>Applies queued I/O results and settles tasks on this thread.</summary>
        void Poll();
    }

    /// <summary>Safe wire refusal; no body, URL, token or raw transport exception is retained.</summary>
    public sealed class PlatformException : Exception
    {
        /// <summary>Creates a safe refusal.</summary>
        public PlatformException(int status, string code, string? reason = null, double? retryAfterMillis = null)
            : base("platform " + code + " (" + status + ")")
        { Status = status; Code = code; Reason = reason; RetryAfterMillis = retryAfterMillis; }
        /// <summary>HTTP status, or zero without a reply.</summary>
        public int Status { get; }
        /// <summary>Bounded machine code.</summary>
        public string Code { get; }
        /// <summary>Bounded machine reason, when present.</summary>
        public string? Reason { get; }
        /// <summary>Cooldown in milliseconds.</summary>
        public double? RetryAfterMillis { get; }
    }

    /// <summary>Transport and origin configuration copied at creation.</summary>
    public sealed class PlatformClientOptions
    {
        /// <summary>HTTP origin or base path, without credentials, query or fragment.</summary>
        public string? BaseUrl { get; set; }
        /// <summary>Player JWT; required only for authenticated requests. Never log it.</summary>
        public string? Token { get; set; }
        /// <summary>HTTP transport. Use the KV Unity transport on WebGL; it receives credentials.</summary>
        public IHttpTransport? Transport { get; set; }
        /// <summary>Safe structured logger, default null logger.</summary>
        public ILogger? Logger { get; set; }
        /// <summary>Per-request deadline, default 15 seconds.</summary>
        public TimeSpan? Timeout { get; set; }
        /// <summary>Injected monotonic milliseconds, default Stopwatch.</summary>
        public Func<double>? NowMillis { get; set; }
    }

    /// <summary>One escaped relative platform request.</summary>
    public sealed class PlatformRequestOptions
    {
        /// <summary>Uppercase HTTP method.</summary>
        public string Method { get; set; } = "GET";
        /// <summary>Absolute-path reference within the configured base; no dot segments or fragment.</summary>
        public string Path { get; set; } = "/time";
        /// <summary>Optional JSON body.</summary>
        public JsonValue? Body { get; set; }
        /// <summary>Whether a missing response is a null result rather than a refusal.</summary>
        public bool MissingAsNull { get; set; }

        /// <summary>Whether to attach the player's credential; default true.</summary>
        public bool Authenticated { get; set; } = true;
    }

    /// <summary>Bounded JSON requests shared by platform, match and push clients.</summary>
    public interface IPlatformHttpClient : IPollable
    {
        /// <summary>Returns parsed JSON or null for 204. Refusals are PlatformException; completion requires Poll.</summary>
        Task<JsonValue?> RequestAsync(PlatformRequestOptions options, CancellationToken cancellationToken = default);

        /// <summary>Transforms a response inside Poll before settling the returned task.</summary>
        Task<T> RequestAsync<T>(PlatformRequestOptions options, Func<JsonValue?, T> read, CancellationToken cancellationToken = default);
    }

    /// <summary>Factory and escaping helpers for the shared wire seam.</summary>
    public static class PlatformHttpClient
    {
        /// <summary>Creates a pumped requester. Calls and Poll belong to one serialized owner.</summary>
        public static IPlatformHttpClient Create(PlatformClientOptions options) => new PlatformHttpImpl(options);
        /// <summary>Escapes one nonempty route segment, refusing controls and path traversal.</summary>
        public static string Segment(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "." || value == ".." || value.Length > 512)
            {
                throw new ArgumentException("Expected a bounded route segment", nameof(value));
            }

            foreach (var c in value)
            {
                if (char.IsControl(c) || char.IsSurrogate(c))
                {
                    throw new ArgumentException("Invalid route segment", nameof(value));
                }
            }

            return Uri.EscapeDataString(value);
        }
        /// <summary>Escapes a query value.</summary>
        public static string Query(string value) => Uri.EscapeDataString(value ?? throw new ArgumentNullException(nameof(value)));
    }

    internal sealed class PlatformHttpImpl : IPlatformHttpClient
    {
        private readonly OwnerGate _gate = new OwnerGate();
        private readonly string _base;
        private readonly string? _token;
        private readonly IHttpTransport _transport;
        private readonly ILogger _logger;
        private readonly TimeSpan _timeout;
        private readonly Func<double> _now;
        private readonly ConcurrentQueue<Action> _answers = new ConcurrentQueue<Action>();
        private readonly List<Pending> _pending = new List<Pending>();
        private bool _disposed;
        private int _poll;
        private sealed class Pending
        {
            internal Action<JsonValue?> Complete = null!;
            internal Action<Exception> Fail = null!;
            internal Action CancelTask = null!;
            internal bool MissingAsNull;
            internal readonly CancellationTokenSource Cancel = new CancellationTokenSource();
            internal CancellationTokenRegistration Registration;
            internal double Deadline;
            internal bool Done;
        }
        internal PlatformHttpImpl(PlatformClientOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var url) || (url.Scheme != "http" && url.Scheme != "https")
                || url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0)
            {
                throw new ArgumentException("Expected an HTTP base without credentials, query or fragment", nameof(options));
            }

            _base = url.AbsoluteUri.TrimEnd('/');
            _token = options.Token;
            if (_token != null)
            {
                foreach (var c in _token)
                {
                    if (c < 33 || c > 126)
                    {
                        throw new ArgumentException("Invalid credential", nameof(options));
                    }
                }
            }

            _transport = options.Transport ?? HttpClientTransport.Default;
            _logger = options.Logger ?? NullLogger.Instance;
            _timeout = options.Timeout ?? TimeSpan.FromSeconds(15);
            if (_timeout <= TimeSpan.Zero || _timeout > KvStoreClient.MaxTimeout)
            {
                throw new ArgumentException("Invalid timeout", nameof(options));
            }

            var stopwatch = Stopwatch.StartNew();
            _now = options.NowMillis ?? (() => stopwatch.Elapsed.TotalMilliseconds);
        }
        public Task<JsonValue?> RequestAsync(PlatformRequestOptions options, CancellationToken cancellationToken = default)
            => RequestAsync(options, value => value, cancellationToken);

        public Task<T> RequestAsync<T>(PlatformRequestOptions options, Func<JsonValue?, T> read, CancellationToken cancellationToken = default)
        {
            using var lease = _gate.Enter();
            if (read == null)
            {
                throw new ArgumentNullException(nameof(read));
            }

            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(IPlatformHttpClient));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (_pending.Count >= 256)
            {
                throw new InvalidOperationException("Too many pending requests");
            }

            if (options.Method != "GET" && options.Method != "POST" && options.Method != "PUT" && options.Method != "PATCH" && options.Method != "DELETE")
            {
                throw new ArgumentException("Invalid HTTP method", nameof(options));
            }

            var path = options.Path;
            if (path == null || !path.StartsWith("/", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal) || path.IndexOf('#') >= 0 || path.IndexOf('\\') >= 0)
            {
                throw new ArgumentException("Expected a relative platform path", nameof(options));
            }

            var rawPath = Uri.UnescapeDataString(path.Split('?')[0]);
            foreach (var part in rawPath.Split('/'))
            {
                if (part == "." || part == "..")
                {
                    throw new ArgumentException("Path traversal is forbidden", nameof(options));
                }
            }

            var headers = new List<KeyValuePair<string, string>>();
            if (options.Authenticated)
            {
                if (string.IsNullOrEmpty(_token))
                {
                    throw new InvalidOperationException("A player token is required");
                }

                headers.Add(new KeyValuePair<string, string>("Authorization", "Bearer " + _token));
            }
            var body = options.Body == null ? null : Json.Stringify(options.Body);
            if (body != null)
            {
                headers.Add(new KeyValuePair<string, string>("Content-Type", "application/json"));
            }

            var call = new HttpCall(options.Method, new Uri(_base + path), headers, body, _timeout);
            var source = new TaskCompletionSource<T>();
            var pending = new Pending
            {
                Deadline = _now() + _timeout.TotalMilliseconds,
                MissingAsNull = options.MissingAsNull,
                Complete = value =>
                {
                    T result;
                    try { result = read(value); } catch (Exception error) { source.TrySetException(error); return; }
                    source.TrySetResult(result);
                },
                Fail = error => source.TrySetException(error),
                CancelTask = () => source.TrySetCanceled(),
            };
            _pending.Add(pending);
            pending.Registration = cancellationToken.Register(() => _answers.Enqueue(() => Finish(pending, null, null, true)));
            if (!cancellationToken.IsCancellationRequested)
            {
                _ = Observe(call, pending);
            }

            return source.Task;
        }
        private async Task Observe(HttpCall call, Pending pending)
        {
            try
            {
                var reply = await _transport.SendAsync(call, pending.Cancel.Token);
                _answers.Enqueue(() =>
                {
                    if (pending.Done)
                    {
                        return;
                    }

                    if (_logger.IsEnabled(LogSeverity.Debug))
                    {
                        _logger.Debug("platform request", Json.Object().Set("status", reply.Status).Set("bytes", System.Text.Encoding.UTF8.GetByteCount(reply.Body)).Build());
                    }

                    JsonValue? value = null;
                    PlatformException? error = null;
                    if (pending.MissingAsNull && reply.Status == 404) { }
                    else if (reply.Status < 200 || reply.Status >= 300)
                    {
                        error = Refusal(reply);
                    }
                    else if (reply.Status != 204 && (reply.Body.Length > Json.MaxLength || !Json.TryParse(reply.Body, out value)))
                    {
                        error = new PlatformException(reply.Status, "bad_body");
                    }

                    Finish(pending, value, error, false);
                });
            }
            catch (Exception) { _answers.Enqueue(() => Finish(pending, null, new PlatformException(0, "network"), false)); }
        }
        private static PlatformException Refusal(HttpReply reply)
        {
            var code = "http"; string? reason = null; double? retry = null;
            if (reply.Body.Length <= Json.MaxLength && Json.TryParse(reply.Body, out var body) && body.TryGetMember("error", out var error))
            {
                code = SafeCode(error.GetString("code")) ?? code;
                if (error.TryGetMember("details", out var details))
                {
                    reason = SafeCode(details.GetString("reason"));
                    var seconds = details.GetNumber("retryAfter");
                    if (seconds >= 0 && seconds <= 86400)
                    {
                        retry = seconds * 1000;
                    }
                }
            }
            return new PlatformException(reply.Status, code, reason, retry);
        }
        private static string? SafeCode(string? code)
        {
            if (string.IsNullOrEmpty(code) || code!.Length > 64)
            {
                return null;
            }

            foreach (var c in code)
            {
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_')
                {
                    return null;
                }
            }

            return code;
        }
        private void Finish(Pending pending, JsonValue? result, Exception? error, bool cancelled)
        {
            if (pending.Done)
            {
                return;
            }

            pending.Done = true; _pending.Remove(pending);
            pending.Registration.Dispose();
            pending.Cancel.Cancel(); pending.Cancel.Dispose();
            if (cancelled)
            {
                pending.CancelTask();
            }
            else if (error != null)
            {
                pending.Fail(error);
            }
            else
            {
                pending.Complete(result);
            }
        }
        public void Poll()
        {
            using var lease = _gate.Enter();
            if (Interlocked.Exchange(ref _poll, 1) != 0)
            {
                throw new InvalidOperationException("Poll is not reentrant");
            }

            try
            {
                var count = Math.Min(512, _answers.Count);
                while (count-- > 0 && _answers.TryDequeue(out var answer))
                {
                    answer();
                }

                foreach (var p in _pending.ToArray())
                {
                    if (_now() >= p.Deadline)
                    {
                        Finish(p, null, new PlatformException(0, "timeout"), false);
                    }
                }
            }
            finally { Volatile.Write(ref _poll, 0); }
        }
        public void Dispose()
        {
            using var lease = _gate.Enter();
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            // Settlement is queued: even disposal never runs an awaiter outside Poll.
            foreach (var p in _pending.ToArray())
            {
                _answers.Enqueue(() => Finish(p, null, null, true));
            }
        }
    }
}
