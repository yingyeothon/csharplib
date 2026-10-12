using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;
using Yingyeothon.Gamebase.Client;
using Yingyeothon.Platform;

namespace Yingyeothon.Match
{
    /// <summary>Deferred client factory.</summary>
    public static class DeferredMatchClient
    {
        /// <summary>Creates a client without automatic retry or polling timers.</summary>
        public static IDeferredMatchClient Create(DeferredMatchClientOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            return new DeferredImpl(PlatformHttpClient.Create(options.Http), "/m/" + PlatformHttpClient.Segment(options.ChannelId));
        }
    }
    internal sealed class DeferredImpl : IDeferredMatchClient
    {
        private readonly IPlatformHttpClient _http; private readonly string _path;
        internal DeferredImpl(IPlatformHttpClient http, string path) { _http = http; _path = path; }
        private Task<MatchTicket> Ticket(string suffix, string method, CancellationToken ct)
            => _http.RequestAsync(new PlatformRequestOptions { Path = _path + suffix, Method = method }, v => new MatchTicket(v ?? throw new PlatformException(200, "bad_body")), ct);
        public Task<MatchTicket> SubmitAsync(CancellationToken ct = default) => Ticket("/ticket", "POST", ct);
        public Task<MatchTicket?> StatusAsync(CancellationToken ct = default)
            => _http.RequestAsync(new PlatformRequestOptions { Path = _path + "/ticket", MissingAsNull = true }, v => v == null ? null : new MatchTicket(v), ct);
        public Task CancelAsync(CancellationToken ct = default) => _http.RequestAsync(new PlatformRequestOptions { Path = _path + "/ticket", Method = "DELETE" }, ct);
        public Task<MatchTicket> AcceptAsync(CancellationToken ct = default) => Ticket("/accept", "POST", ct);
        public Task<MatchTicket> DeclineAsync(CancellationToken ct = default) => Ticket("/decline", "POST", ct);
        public void Poll() => _http.Poll();
        public void Dispose() => _http.Dispose();
    }
    /// <summary>Live match socket factory.</summary>
    public static class LiveMatchClient
    {
        /// <summary>Creates a client; no socket starts until FindAsync.</summary>
        public static ILiveMatchClient Create(LiveMatchClientOptions options) => new LiveImpl(options);
    }
    internal sealed class LiveImpl : ILiveMatchClient, IWebSocketEventSink
    {
        private readonly OwnerGate _gate = new OwnerGate();
        private readonly string _url; private readonly string _token; private readonly IWebSocketFactory _factory; private readonly IClock _clock; private readonly double _timeout;
        private readonly ConcurrentQueue<SocketEvent> _events = new ConcurrentQueue<SocketEvent>();
        private IWebSocket? _socket; private TaskCompletionSource<MatchResult>? _find; private CancellationTokenRegistration _registration;
        private double _deadline; private bool _disposed; private int _cancel; private int _poll;
        internal LiveImpl(LiveMatchClientOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var url) || (url.Scheme != "ws" && url.Scheme != "wss") || url.UserInfo.Length > 0 || url.Query.Length > 0 || url.Fragment.Length > 0)
            {
                throw new ArgumentException("Invalid socket origin", nameof(options));
            }

            if (string.IsNullOrEmpty(options.Token))
            {
                throw new ArgumentException("Player token required", nameof(options));
            }

            foreach (var c in options.Token)
            {
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && "!#$%&'*+-.^_`|~".IndexOf(c) < 0)
                {
                    throw new ArgumentException("Invalid credential", nameof(options));
                }
            }

            PlatformHttpClient.Segment(options.ChannelId);
            _url = url.AbsoluteUri + "?channel=" + PlatformHttpClient.Query(options.ChannelId);
            _token = options.Token; _factory = options.WebSocketFactory ?? WebSocketTransport.Default; _clock = options.Clock ?? SystemClock.Instance;
            _timeout = options.TimeoutMillis;
            if (double.IsNaN(_timeout) || double.IsInfinity(_timeout) || _timeout <= 0)
            {
                throw new ArgumentException("Invalid timeout", nameof(options));
            }
        }
        public Task<MatchResult> FindAsync(CancellationToken cancellationToken = default)
        {
            using var lease = _gate.Enter();
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ILiveMatchClient));
            }

            if (_find != null)
            {
                return _find.Task;
            }

            var source = new TaskCompletionSource<MatchResult>(); _find = source; _deadline = _clock.NowMillis + _timeout;
            _registration = cancellationToken.Register(Cancel);
            if (!cancellationToken.IsCancellationRequested)
            {
                try { _socket = _factory.Create(new WebSocketCreateContext(_url, new[] { "bearer", _token }, this)); _socket.Start(); }
                catch (Exception) { Cancel(); }
            }
            return source.Task;
        }
        public void Post(SocketEvent socketEvent)
        {
            if (_events.Count < 512)
            {
                _events.Enqueue(socketEvent);
            }
            else
            {
                Interlocked.Exchange(ref _cancel, 1);
            }
        }
        public void Cancel() => Interlocked.Exchange(ref _cancel, 1);
        private void Finish(MatchResult? result, PlatformException? error)
        {
            var source = _find; _find = null; var socket = _socket; _socket = null; _registration.Dispose();
            try { socket?.Close(1000, ""); }
            catch (Exception) { }
            finally { try { socket?.Dispose(); } catch (Exception) { } }
            if (error != null)
            {
                source?.TrySetException(error);
            }
            else
            {
                source?.TrySetResult(result!);
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
                if (Interlocked.Exchange(ref _cancel, 0) != 0)
                {
                    Finish(null, new PlatformException(0, "cancelled"));
                }

                for (var i = 0; i < 512 && _events.TryDequeue(out var e); i++)
                {
                    if (_socket != e.Source)
                    {
                        continue;
                    }

                    if (e.Kind == SocketEventKind.Opened)
                    {
                        if (e.Protocol != "bearer")
                        {
                            Finish(null, new PlatformException(0, "unauthorized"));
                        }
                    }
                    else if (e.Kind == SocketEventKind.Closed)
                    {
                        Finish(null, new PlatformException(0, "network"));
                    }
                    else if (!e.IsText || e.Text == null || !Json.TryParse(e.Text, out var frame))
                    {
                        Finish(null, new PlatformException(0, "bad_body"));
                    }
                    else if (frame.GetString("type") == "matched")
                    {
                        MatchResult? result = null; PlatformException? error = null;
                        try { result = new MatchResult(frame); } catch (PlatformException ex) { error = ex; }
                        Finish(result, error);
                    }
                    else if (frame.GetString("type") == "failed")
                    {
                        Finish(null, new PlatformException(0, "unavailable"));
                    }
                    else if (frame.GetString("type") == "replaced")
                    {
                        Finish(null, new PlatformException(0, "cancelled"));
                    }
                }
                if (_find != null && _clock.NowMillis >= _deadline)
                {
                    Finish(null, new PlatformException(0, "timeout"));
                }
            }
            finally { Volatile.Write(ref _poll, 0); }
        }
        public void Dispose() { using var lease = _gate.Enter(); _disposed = true; Cancel(); }
    }
}
