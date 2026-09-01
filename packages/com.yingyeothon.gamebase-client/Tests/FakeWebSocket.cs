using System;
using System.Collections.Generic;
using Yingyeothon.Codec;

namespace Yingyeothon.Gamebase.Client.Tests
{
    /// <summary>
    /// A socket the test drives as if it were the gateway. It does no threading at
    /// all: every server action posts synchronously, and nothing is observed until
    /// the client is polled.
    /// </summary>
    public sealed class FakeWebSocket : IWebSocket
    {
        private readonly IWebSocketEventSink _sink;
        private readonly List<JsonValue> _sent = new List<JsonValue>();
        private bool _open;
        private bool _closed;

        internal FakeWebSocket(WebSocketCreateContext context)
        {
            Url = context.Url;
            SubProtocols = context.SubProtocols;
            _sink = context.Sink;
        }

        public string Url { get; }

        public IReadOnlyList<string> SubProtocols { get; }

        /// <summary>Frames the client sent, parsed.</summary>
        public IReadOnlyList<JsonValue> Sent => _sent;

        /// <summary>The close the client itself requested, if any.</summary>
        public (int Code, string Reason)? ClientClose { get; private set; }

        public bool Started { get; private set; }

        public int DisposeCount { get; private set; }

        /// <summary>
        /// Makes <see cref="Close"/> record the request without reporting it, the way
        /// the real transport does: <c>ClientWebSocketTransport.Close</c> starts an
        /// async close and the event arrives on the receive thread later. The test
        /// then calls <see cref="ServerClose(int, string)"/> itself, which is the only
        /// way to reach anything that happens between the two.
        /// </summary>
        public bool DeferClose { get; set; }

        public void Start() => Started = true;

        public void SendText(string text)
        {
            if (!_open)
            {
                throw new InvalidOperationException("fake socket is not open");
            }

            _sent.Add(Json.Parse(text));
        }

        public void Close(int code, string reason)
        {
            // A client may only send 1000 or 3000-4999; sending anything else is a
            // bug the browser would refuse, so the fake refuses it too.
            if (code != 1000 && (code < 3000 || code > 4999))
            {
                throw new InvalidOperationException("InvalidAccessError: close code " + code);
            }

            if (_closed)
            {
                return;
            }

            ClientClose = (code, reason);
            if (!DeferClose)
            {
                ServerClose(code, reason);
            }
        }

        public void Dispose() => DisposeCount++;

        // ---- the server side, driven by the test ---------------------------

        public void ServerOpen() => ServerOpen("bearer");

        public void ServerOpen(string protocol)
        {
            _open = true;
            _sink.Post(SocketEvent.Opened(this, protocol));
        }

        public void ServerSend(JsonValue frame) => _sink.Post(SocketEvent.Message(this, Json.Stringify(frame)));

        public void ServerSendRaw(string text) => _sink.Post(SocketEvent.Message(this, text));

        public void ServerSendBinary() => _sink.Post(SocketEvent.BinaryMessage(this));

        public void ServerClose(int code) => ServerClose(code, string.Empty);

        public void ServerClose(int code, string reason)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            _open = false;
            _sink.Post(SocketEvent.Closed(this, code, reason));
        }

        /// <summary>A transport failure: the browser's close 1006.</summary>
        public void ServerError() => ServerClose(1006, "abnormal");
    }

    /// <summary>Hands out <see cref="FakeWebSocket"/>s and remembers them all.</summary>
    public sealed class FakeWebSocketFactory : IWebSocketFactory
    {
        private readonly List<FakeWebSocket> _sockets = new List<FakeWebSocket>();

        public IReadOnlyList<FakeWebSocket> Sockets => _sockets;

        /// <summary>Set to make <see cref="Create"/> fail, the way a bad URL would.</summary>
        public Func<WebSocketCreateContext, IWebSocket>? CreateOverride { get; set; }

        public FakeWebSocket Latest => _sockets.Count == 0
            ? throw new InvalidOperationException("no socket constructed yet")
            : _sockets[_sockets.Count - 1];

        public IWebSocket Create(WebSocketCreateContext context)
        {
            if (CreateOverride != null)
            {
                return CreateOverride(context);
            }

            var socket = new FakeWebSocket(context);
            _sockets.Add(socket);
            return socket;
        }
    }

    /// <summary>A clock the test advances by hand.</summary>
    public sealed class FakeClock : IClock
    {
        public double NowMillis { get; private set; }

        public void Advance(double millis) => NowMillis += millis;
    }
}
