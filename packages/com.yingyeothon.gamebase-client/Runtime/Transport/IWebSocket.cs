using System;
using System.Collections.Generic;

namespace Yingyeothon.Gamebase.Client
{
    /// <summary>What a socket reported.</summary>
    public enum SocketEventKind
    {
        Opened,
        Message,
        Closed,
    }

    /// <summary>One thing a socket reported, carried from the receive thread to the pump.</summary>
    public readonly struct SocketEvent
    {
        private SocketEvent(IWebSocket source, SocketEventKind kind, string protocol, string? text, bool isText, int code, string reason)
        {
            Source = source;
            Kind = kind;
            Protocol = protocol;
            Text = text;
            IsText = isText;
            Code = code;
            Reason = reason;
        }

        /// <summary>
        /// The socket that reported this. The state machine compares it against the
        /// socket it currently holds and drops anything from one it has replaced.
        /// </summary>
        public IWebSocket Source { get; }

        /// <summary>What happened.</summary>
        public SocketEventKind Kind { get; }

        /// <summary>The subprotocol the server selected. Empty when it selected none.</summary>
        public string Protocol { get; }

        /// <summary>The message, for a text frame.</summary>
        public string? Text { get; }

        /// <summary>Whether the message was a text frame. A binary one is a protocol error.</summary>
        public bool IsText { get; }

        /// <summary>The close code, on a close.</summary>
        public int Code { get; }

        /// <summary>The close reason, on a close. Never log it: the peer may have chosen it.</summary>
        public string Reason { get; }

        /// <summary>The handshake completed; <paramref name="protocol"/> is the subprotocol the server selected.</summary>
        public static SocketEvent Opened(IWebSocket source, string protocol)
            => new SocketEvent(source, SocketEventKind.Opened, protocol ?? string.Empty, null, false, 0, string.Empty);

        /// <summary>A text frame arrived.</summary>
        public static SocketEvent Message(IWebSocket source, string text)
            => new SocketEvent(source, SocketEventKind.Message, string.Empty, text, true, 0, string.Empty);

        /// <summary>A binary frame arrived, which is a protocol error on this gateway.</summary>
        public static SocketEvent BinaryMessage(IWebSocket source)
            => new SocketEvent(source, SocketEventKind.Message, string.Empty, null, false, 0, string.Empty);

        /// <summary>The socket closed. Report this exactly once, with the locally requested code when the close was local.</summary>
        public static SocketEvent Closed(IWebSocket source, int code, string? reason)
            => new SocketEvent(source, SocketEventKind.Closed, string.Empty, null, false, code, reason ?? string.Empty);
    }

    /// <summary>Where a socket posts what it observed.</summary>
    /// <remarks>
    /// A sink rather than events on the socket makes the thread hand-off structural:
    /// there is no callback an adapter could invoke on the wrong thread, because the
    /// only thing it can do is enqueue.
    /// </remarks>
    public interface IWebSocketEventSink
    {
        /// <summary>Enqueues one observation. The only thing a transport may do from its own thread.</summary>
        void Post(SocketEvent socketEvent);
    }

    /// <summary>A WebSocket, reduced to what this SDK needs.</summary>
    public interface IWebSocket : IDisposable
    {
        /// <summary>Begins connecting. The outcome arrives on the sink, never as a throw.</summary>
        void Start();

        /// <summary>Sends one text frame.</summary>
        void SendText(string text);

        /// <summary>Requests a close. Valid client codes are 1000 and 3000-4999.</summary>
        void Close(int code, string reason);
    }

    /// <summary>Everything a factory needs to build a socket.</summary>
    public sealed class WebSocketCreateContext
    {
        public WebSocketCreateContext(string url, IReadOnlyList<string> subProtocols, IWebSocketEventSink sink)
        {
            Url = url;
            SubProtocols = subProtocols;
            Sink = sink;
        }

        /// <summary>The full handshake URL, query string included.</summary>
        public string Url { get; }

        /// <summary>Always <c>["bearer", token]</c>. The token never appears anywhere else.</summary>
        public IReadOnlyList<string> SubProtocols { get; }

        /// <summary>Where the socket posts what it observes.</summary>
        public IWebSocketEventSink Sink { get; }
    }

    /// <summary>
    /// Builds sockets. Injectable because Unity WebGL has no usable
    /// <c>ClientWebSocket</c> and a test needs a socket it drives as the server.
    /// </summary>
    /// <remarks>
    /// A factory may throw from <see cref="Create"/> for input it can reject up front
    /// (a malformed URL, a subprotocol with illegal characters); the SDK reports that
    /// as a stop. Everything after that — including a refused handshake — must arrive
    /// as a close event instead, or the handshake-failure policy never sees it.
    /// </remarks>
    public interface IWebSocketFactory
    {
        /// <summary>Builds a socket. May throw for input it can reject up front; everything after that must arrive as a close.</summary>
        IWebSocket Create(WebSocketCreateContext context);
    }
}
