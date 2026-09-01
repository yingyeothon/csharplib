using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;
using Yingyeothon.Logger;

namespace Yingyeothon.Gamebase.Client
{
    internal sealed class GatewaySocketOptions
    {
        internal string Url { get; set; } = string.Empty;

        internal string ChannelId { get; set; } = string.Empty;

        internal string? GameId { get; set; }

        internal string Token { get; set; } = string.Empty;

        internal GatewayChannelKind Kind { get; set; }

        internal IWebSocketFactory? WebSocketFactory { get; set; }

        internal BackoffOptions? Backoff { get; set; }

        internal double HelloTimeoutMillis { get; set; } = 10000;

        internal int MaxHandshakeFailures { get; set; } = 5;

        internal ILogger Logger { get; set; } = NullLogger.Instance;

        internal IClock Clock { get; set; } = SystemClock.Instance;
    }

    /// <summary>
    /// The connection state machine both clients share: the bearer-subprotocol
    /// handshake, the hello wait, the close-code policy, and reconnect with backoff.
    /// </summary>
    /// <remarks>
    /// Every field here is touched only from the pump thread. The receive side does
    /// exactly one thing: enqueue a <see cref="SocketEvent"/>. <see cref="Poll"/>
    /// drains that queue, runs the transitions, raises the events, and only then
    /// settles the pending connect — so a continuation can never observe a client
    /// halfway through a transition.
    /// </remarks>
    internal sealed class GatewaySocket : IGatewayPollable, IDisposable, IWebSocketEventSink
    {
        private const string BearerSubprotocol = "bearer";

        /// <summary>
        /// A local close makes the fake (and a real adapter) report back immediately,
        /// so one Poll must be able to drain the queue more than once. The bound keeps
        /// a pathological close/open cascade from hanging a frame.
        /// </summary>
        private const int MaxPollPasses = 16;

        private readonly ConcurrentQueue<SocketEvent> _events = new ConcurrentQueue<SocketEvent>();
        private readonly GatewaySocketOptions _options;
        private readonly IWebSocketFactory _factory;
        private readonly IBackoff _backoff;
        private readonly ILogger _logger;
        private readonly IClock _clock;
        private readonly string _url;
        private readonly IReadOnlyList<string> _subProtocols;

        /// <summary>
        /// The thread currently inside <see cref="Poll"/>, or 0. This is the whole
        /// concurrency guard: identity of the pump thread is deliberately not
        /// enforced, because a host without a synchronization context resumes each
        /// <c>await</c> on a different pool thread while still using the client
        /// strictly one call at a time. What must never happen is two threads in it
        /// at once.
        /// </summary>
        private int _pumpingThreadId;

        private IWebSocket? _socket;
        private bool _closedByUser;
        private bool _ready;
        private bool _opened;
        private int _handshakeFailures;
        private CloseDisposition? _closeOverride;
        private double? _helloDeadline;
        private double? _reconnectDeadline;
        private bool _disposed;

        private TaskCompletionSource<bool>? _pending;
        private Exception? _pendingFailure;
        private bool _settleScheduled;
        private bool _settleSuccess;

        internal GatewaySocket(GatewaySocketOptions options)
        {
            _options = options;
            _factory = options.WebSocketFactory ?? WebSocketTransport.Default;
            _backoff = Backoff.Create(options.Backoff ?? new BackoffOptions());
            _logger = options.Logger;
            _clock = options.Clock;
            _url = GatewayUrl.Build(options.Url, options.ChannelId, options.GameId);
            _subProtocols = new[] { BearerSubprotocol, options.Token };
        }

        internal GatewayClientState State { get; private set; } = GatewayClientState.Idle;

        internal event Action<string>? Opened;

        internal event Action<string, JsonValue>? Frame;

        internal event Action<DisconnectedEvent>? Disconnected;

        internal event Action<ReconnectingEvent>? Reconnecting;

        internal event Action<StoppedEvent>? Stopped;

        internal event Action<ProtocolErrorEvent>? ProtocolError;

        // ---- the pump ------------------------------------------------------

        void IWebSocketEventSink.Post(SocketEvent socketEvent) => _events.Enqueue(socketEvent);

        public void Poll()
        {
            var thread = Thread.CurrentThread.ManagedThreadId;
            var prior = Interlocked.CompareExchange(ref _pumpingThreadId, thread, 0);
            if (prior != 0)
            {
                throw new InvalidOperationException(prior == thread
                    ? "Poll() is not re-entrant; do not call it from an event handler."
                    : "Poll() is already running on another thread; pump this client from one thread.");
            }

            try
            {
                for (var pass = 0; pass < MaxPollPasses; pass++)
                {
                    var progressed = false;

                    while (_events.TryDequeue(out var socketEvent))
                    {
                        Handle(socketEvent);
                        progressed = true;
                    }

                    // Events first: a hello that arrived in the same tick as its
                    // deadline clears the deadline instead of racing it.
                    var now = _clock.NowMillis;
                    if (_helloDeadline.HasValue && now >= _helloDeadline.Value)
                    {
                        _helloDeadline = null;
                        LocalClose(new CloseDisposition(CloseDispositionKind.Reconnect, "hello timeout"), "hello timeout");
                        progressed = true;
                    }

                    if (_reconnectDeadline.HasValue && now >= _reconnectDeadline.Value)
                    {
                        _reconnectDeadline = null;
                        Open();
                        progressed = true;
                    }

                    if (!progressed)
                    {
                        break;
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _pumpingThreadId, 0);
                FlushSettlement();
            }
        }

        // ---- public operations ---------------------------------------------

        internal Task ConnectAsync()
        {
            RequireNotConcurrent();
            if (State != GatewayClientState.Idle)
            {
                return FromException(new InvalidOperationException("ConnectAsync() called in state " + State));
            }

            // No RunContinuationsAsynchronously: the continuation is meant to run
            // inline on the pump thread, which is the thread the caller must use for
            // everything else. Handing it to the thread pool would make `await
            // ConnectAsync()` resume somewhere Send() is not allowed.
            var pending = new TaskCompletionSource<bool>();
            _pending = pending;
            Open();

            // Open() can fail outright — a factory that refuses the URL — and that is
            // a decided outcome, not something to wait for the next Poll to report.
            FlushSettlement();
            return pending.Task;
        }

        internal void Close()
        {
            RequireNotConcurrent();
            if (_closedByUser)
            {
                return;
            }

            _closedByUser = true;
            _helloDeadline = null;
            _reconnectDeadline = null;

            var current = _socket;
            _socket = null;
            var wasReady = _ready;
            _ready = false;
            State = GatewayClientState.Closed;

            if (current != null)
            {
                SafeClose(current, 1000, "client closed");
                current.Dispose();
            }

            if (wasReady || current != null)
            {
                Disconnected?.Invoke(new DisconnectedEvent(1000, "client closed", false));
            }

            ScheduleFailure(new GatewayStoppedException("closed before the connection became ready"));
            FlushSettlement();
        }

        internal void Send(JsonValue frame)
        {
            RequireNotConcurrent();
            if (!_ready || _socket == null)
            {
                throw new InvalidOperationException("cannot send in state " + State);
            }

            _socket.SendText(JsonCodec.Encode(frame));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Close();
        }

        // ---- transitions ---------------------------------------------------

        private void Open()
        {
            if (_closedByUser)
            {
                return;
            }

            if (State != GatewayClientState.Reconnecting)
            {
                State = GatewayClientState.Connecting;
            }

            IWebSocket created;
            try
            {
                created = _factory.Create(new WebSocketCreateContext(_url, _subProtocols, this));
            }
            catch (Exception error)
            {
                Stop(0, new CloseDisposition(CloseDispositionKind.Stop, "cannot open WebSocket: " + error.Message));
                return;
            }

            _socket = created;
            _opened = false;
            try
            {
                created.Start();
            }
            catch (Exception error)
            {
                _socket = null;
                created.Dispose();
                Stop(0, new CloseDisposition(CloseDispositionKind.Stop, "cannot open WebSocket: " + error.Message));
            }
        }

        private void Handle(SocketEvent socketEvent)
        {
            // Anything from a socket this state machine has already replaced is a
            // ghost: it must not reopen, close, or feed the current connection.
            if (!ReferenceEquals(socketEvent.Source, _socket))
            {
                return;
            }

            switch (socketEvent.Kind)
            {
                case SocketEventKind.Opened:
                    HandleOpened(socketEvent.Protocol);
                    return;
                case SocketEventKind.Message:
                    HandleMessage(socketEvent);
                    return;
                default:
                    HandleClosed(socketEvent.Code, socketEvent.Reason);
                    return;
            }
        }

        private void HandleOpened(string protocol)
        {
            _opened = true;
            _handshakeFailures = 0;

            if (!string.Equals(protocol, BearerSubprotocol, StringComparison.Ordinal))
            {
                LocalClose(
                    new CloseDisposition(CloseDispositionKind.Stop, "gateway did not select the bearer subprotocol"),
                    "unexpected subprotocol");
                return;
            }

            Opened?.Invoke(protocol);

            if (_options.Kind == GatewayChannelKind.Q)
            {
                MarkReady();
                return;
            }

            _helloDeadline = _clock.NowMillis + _options.HelloTimeoutMillis;
        }

        private void HandleMessage(SocketEvent socketEvent)
        {
            if (!socketEvent.IsText || socketEvent.Text == null)
            {
                ProtocolError?.Invoke(new ProtocolErrorEvent("non-text frame"));
                return;
            }

            if (!Json.TryParse(socketEvent.Text, out var parsed))
            {
                ProtocolError?.Invoke(new ProtocolErrorEvent("frame is not JSON"));
                return;
            }

            if (parsed.Kind != JsonKind.Object)
            {
                ProtocolError?.Invoke(new ProtocolErrorEvent("frame has no string type"));
                return;
            }

            var type = parsed.GetString("type");
            if (type == null)
            {
                ProtocolError?.Invoke(new ProtocolErrorEvent("frame has no string type"));
                return;
            }

            if (_options.Kind == GatewayChannelKind.Lobby && !_ready)
            {
                if (!string.Equals(type, FrameTypes.Hello, StringComparison.Ordinal))
                {
                    // Keep waiting: the hello deadline is what ends this, not one
                    // stray frame.
                    ProtocolError?.Invoke(new ProtocolErrorEvent("expected hello, got " + type));
                    return;
                }

                _helloDeadline = null;
                MarkReady();
                Frame?.Invoke(type, parsed);
                return;
            }

            Frame?.Invoke(type, parsed);
        }

        private void HandleClosed(int code, string reason)
        {
            var closed = _socket;
            _socket = null;
            _ready = false;
            _helloDeadline = null;
            closed?.Dispose();

            if (_closedByUser)
            {
                return;
            }

            var disposition = _closeOverride ?? CloseCodes.Classify(code, _options.Kind);
            _closeOverride = null;

            if (!_opened)
            {
                _handshakeFailures++;
                if (disposition.Kind == CloseDispositionKind.Reconnect
                    && _handshakeFailures >= _options.MaxHandshakeFailures)
                {
                    // A browser cannot see why a handshake was refused, and neither
                    // can this: 401/403/404/410 all arrive as a close before open. So
                    // a run of them ends the session instead of retrying a dead token.
                    disposition = new CloseDisposition(
                        CloseDispositionKind.Stop,
                        "handshake failed " + _handshakeFailures + " times in a row");
                }
            }

            _logger.Debug(
                "gateway socket closed",
                Json.Object()
                    .Set("channelId", _options.ChannelId)
                    .Set("gameId", _options.GameId)
                    .Set("code", (double)code)
                    .Set("reasonLength", (double)reason.Length)
                    .Build());

            if (disposition.Kind == CloseDispositionKind.Reconnect)
            {
                ScheduleReconnect(code, disposition);
            }
            else
            {
                Stop(code, disposition);
            }
        }

        private void MarkReady()
        {
            _ready = true;
            State = GatewayClientState.Connected;
            _backoff.Reset();
            ScheduleSuccess();
        }

        private void ScheduleReconnect(int code, CloseDisposition disposition)
        {
            var delay = _backoff.Next();
            if (!delay.HasValue)
            {
                Stop(code, new CloseDisposition(CloseDispositionKind.Stop, "reconnect attempts exhausted"));
                return;
            }

            State = GatewayClientState.Reconnecting;
            Disconnected?.Invoke(new DisconnectedEvent(code, disposition.Reason, true));
            _logger.Info(
                "gateway reconnecting",
                Json.Object()
                    .Set("channelId", _options.ChannelId)
                    .Set("gameId", _options.GameId)
                    .Set("code", (double)code)
                    .Set("attempt", (double)_backoff.Attempts)
                    .Set("delayMs", delay.Value)
                    .Build());
            Reconnecting?.Invoke(new ReconnectingEvent(_backoff.Attempts, delay.Value));
            _reconnectDeadline = _clock.NowMillis + delay.Value;
        }

        private void Stop(int code, CloseDisposition disposition)
        {
            State = GatewayClientState.Closed;
            _logger.Info(
                "gateway connection stopped",
                Json.Object()
                    .Set("channelId", _options.ChannelId)
                    .Set("gameId", _options.GameId)
                    .Set("code", (double)code)
                    .Set("kind", disposition.Kind.ToString())
                    .Set("reason", disposition.Reason)
                    .Build());
            Disconnected?.Invoke(new DisconnectedEvent(code, disposition.Reason, false));
            Stopped?.Invoke(new StoppedEvent(disposition.Kind, disposition.Reason, code));
            ScheduleFailure(new GatewayStoppedException("gateway connection stopped: " + disposition.Reason));
        }

        private void LocalClose(CloseDisposition disposition, string reason)
        {
            _closeOverride = disposition;
            if (_socket != null)
            {
                SafeClose(_socket, GatewayCloseCode.Local, reason);
            }
        }

        private static void SafeClose(IWebSocket socket, int code, string reason)
        {
            try
            {
                socket.Close(code, reason);
            }
            catch (ObjectDisposedException)
            {
                // The socket already went away; its close event is on the queue.
            }
            catch (InvalidOperationException)
            {
                // Same: a socket that is no longer closeable has already reported.
            }
        }

        // ---- pending connect settlement -------------------------------------

        private void ScheduleSuccess()
        {
            if (_settleScheduled)
            {
                return;
            }

            _settleScheduled = true;
            _settleSuccess = true;
        }

        private void ScheduleFailure(Exception error)
        {
            if (_settleScheduled)
            {
                return;
            }

            _settleScheduled = true;
            _settleSuccess = false;
            _pendingFailure = error;
        }

        /// <summary>
        /// Completes a pending <c>ConnectAsync</c> only after every handler for this
        /// pass has run. tslib gets this free from the microtask queue: its
        /// <c>markReady</c> resolves before <c>emit("hello")</c>, but the continuation
        /// still runs after it. Completing inline here would let an awaiter read the
        /// client before the hello handler had filled it in.
        /// </summary>
        private void FlushSettlement()
        {
            if (!_settleScheduled)
            {
                return;
            }

            _settleScheduled = false;
            var pending = _pending;
            var failure = _pendingFailure;
            _pending = null;
            _pendingFailure = null;

            if (pending == null)
            {
                return;
            }

            if (_settleSuccess)
            {
                pending.TrySetResult(true);
            }
            else
            {
                pending.TrySetException(failure ?? new GatewayStoppedException("gateway connection stopped"));
            }
        }

        /// <summary>
        /// Refuses a call made while another thread is inside <see cref="Poll"/>. A
        /// handler calling back in during its own pump is fine — that is how a game
        /// sends from an event — so only a genuinely concurrent caller is rejected.
        /// </summary>
        private void RequireNotConcurrent()
        {
            var pumping = Volatile.Read(ref _pumpingThreadId);
            if (pumping != 0 && pumping != Thread.CurrentThread.ManagedThreadId)
            {
                throw new InvalidOperationException(
                    "This client is being polled on another thread; use it from one thread at a time.");
            }
        }

        private static Task FromException(Exception error)
        {
            var source = new TaskCompletionSource<bool>();
            source.SetException(error);
            return source.Task;
        }
    }
}
