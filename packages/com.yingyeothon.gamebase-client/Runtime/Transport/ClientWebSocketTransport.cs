using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Yingyeothon.Gamebase.Client
{
    /// <summary>The default <see cref="IWebSocketFactory"/>.</summary>
    public static class WebSocketTransport
    {
        /// <summary>
        /// A factory over <see cref="ClientWebSocket"/>.
        /// </summary>
        /// <remarks>
        /// Unity WebGL has no usable <c>ClientWebSocket</c> and no thread to run a
        /// receive loop on, so a WebGL build must pass its own factory through the
        /// client's options. This mirrors tslib, where a runtime without a global
        /// <c>WebSocket</c> has to inject one.
        /// </remarks>
        public static IWebSocketFactory Default { get; } = new ClientWebSocketFactory();

        private sealed class ClientWebSocketFactory : IWebSocketFactory
        {
            public IWebSocket Create(WebSocketCreateContext context)
            {
                if (context == null)
                {
                    throw new ArgumentNullException(nameof(context));
                }

#if UNITY_WEBGL && !UNITY_EDITOR
                throw new PlatformNotSupportedException(
                    "ClientWebSocket does not work on WebGL; set WebSocketFactory on the client options.");
#else
                return new ClientWebSocketTransport(context);
#endif
            }
        }
    }

    /// <summary>
    /// Drives a <see cref="ClientWebSocket"/> and reports everything it observes to
    /// the sink, never by throwing.
    /// </summary>
    /// <remarks>
    /// The one thing this must get right, beyond framing, is that a refused handshake
    /// becomes a close event. .NET raises 401/403/404/410 as an exception out of
    /// <c>ConnectAsync</c>, while a browser only ever sees a close before open — and
    /// the SDK's handshake-failure policy is written against the browser's view. So
    /// every failure after construction is reported as close 1006.
    /// </remarks>
    internal sealed class ClientWebSocketTransport : IWebSocket
    {
        private const int ReceiveBufferSize = 8 * 1024;

        /// <summary>
        /// The largest message this client will reassemble, in bytes. The gateway
        /// caps its own outbound frames at 32 KB (its README's "text frames only,
        /// 16 KB inbound cap, 32 KB outbound cap"), so this is double the largest
        /// legitimate frame. Without it a peer streaming continuation frames grows a
        /// MemoryStream without bound, and the codec's own 1 MiB cap cannot help:
        /// that one is checked against a string the transport has already built.
        /// </summary>
        private const int MaxMessageBytes = 64 * 1024;

        /// <summary>The WebSocket close reason limit, in UTF-8 bytes.</summary>
        private const int MaxCloseReasonBytes = 123;

        private readonly Uri _uri;
        private readonly IReadOnlyList<string> _subProtocols;
        private readonly IWebSocketEventSink _sink;
        private readonly ClientWebSocket _socket = new ClientWebSocket();
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

        private int _closePosted;
        private int _started;
        private int _disposed;
        private int _localCloseCode;
        private string _localCloseReason = string.Empty;

        internal ClientWebSocketTransport(WebSocketCreateContext context)
        {
            // Everything that can be refused up front is refused here, synchronously,
            // so the SDK reports it as "cannot open WebSocket" rather than as a close.
            if (!Uri.TryCreate(context.Url, UriKind.Absolute, out var uri))
            {
                throw new UriFormatException("Not an absolute URL: " + context.Url);
            }

            _uri = uri;
            _subProtocols = context.SubProtocols;
            _sink = context.Sink;

            foreach (var subProtocol in _subProtocols)
            {
                RequireHttpToken(subProtocol);
            }
        }

        public void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) == 1)
            {
                return;
            }

            _ = Task.Run(RunAsync);
        }

        public void SendText(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            // Fire and forget on purpose: the caller is the game's main thread and
            // must not block on the network. A send that fails ends the socket, which
            // arrives as a close event like every other failure. Order is preserved
            // because SendAsync runs synchronously into the semaphore's FIFO wait
            // queue before its first suspension.
            _ = SendAsync(text);
        }

        public void Close(int code, string reason)
        {
            // 0 is this field's "no local close yet" sentinel, so it cannot also be a
            // code: storing it would leave the latch open, let the next call overwrite
            // the reason, and post a close of 0 that CloseCodes reads as unknown and
            // retries. A client may only send 1000 or 3000-4999 anyway, which is what
            // the fake has always enforced.
            if (code != 1000 && (code < 3000 || code > 4999))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(code), "A client close code must be 1000 or 3000-4999.");
            }

            // First local close wins, code and reason together. Assigning the reason
            // unconditionally would pair a later call's text with the earlier code.
            if (Interlocked.CompareExchange(ref _localCloseCode, code, 0) == 0)
            {
                _localCloseReason = Truncate(reason ?? string.Empty);
            }

            _ = CloseAsync();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            PostClose(1006, "abnormal");

            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            _socket.Dispose();
            _cancellation.Dispose();
            _sendLock.Dispose();
        }

        private async Task RunAsync()
        {
            try
            {
                foreach (var subProtocol in _subProtocols)
                {
                    _socket.Options.AddSubProtocol(subProtocol);
                }

                await _socket.ConnectAsync(_uri, _cancellation.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                PostClose(1006, "abnormal");
                return;
            }

            _sink.Post(SocketEvent.Opened(this, _socket.SubProtocol ?? string.Empty));

            try
            {
                await ReceiveLoopAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                PostClose(1006, "abnormal");
            }
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[ReceiveBufferSize];
            var segment = new ArraySegment<byte>(buffer);

            while (!_cancellation.IsCancellationRequested)
            {
                using (var message = new MemoryStream())
                {
                    WebSocketReceiveResult result;
                    var binary = false;
                    var total = 0L;
                    do
                    {
                        result = await _socket.ReceiveAsync(segment, _cancellation.Token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            var code = _socket.CloseStatus.HasValue ? (int)_socket.CloseStatus.Value : 1005;
                            var reason = _socket.CloseStatusDescription ?? string.Empty;

                            // Report first. The acknowledgement below waits on the
                            // send lock, and a send blocked against a peer that
                            // stopped reading would otherwise hold the close back
                            // indefinitely, leaving the state machine believing it is
                            // still connected and never reconnecting.
                            PostClose(code, reason);

                            // Answer the close frame. Without this the peer's own
                            // CloseAsync never completes and the gateway is left
                            // holding a half-closed connection until its idle timer.
                            await AcknowledgeCloseAsync().ConfigureAwait(false);
                            return;
                        }

                        binary |= result.MessageType == WebSocketMessageType.Binary;
                        total += result.Count;
                        if (total > MaxMessageBytes)
                        {
                            // Everything after construction arrives as a close, never
                            // as a throw. 1009 stops rather than reconnects, which is
                            // right: a client that reconnected would meet the same
                            // flood. The state machine disposes the socket from there.
                            PostClose(1009, "message too large");
                            return;
                        }

                        // A binary message is reported by kind and never by content,
                        // so its bytes are counted and dropped rather than buffered.
                        if (!binary)
                        {
                            message.Write(buffer, 0, result.Count);
                        }
                    }
                    while (!result.EndOfMessage);

                    if (binary)
                    {
                        _sink.Post(SocketEvent.BinaryMessage(this));
                        continue;
                    }

                    // Decode once over the whole message: a multi-byte character split
                    // across two frames would be corrupted by per-frame decoding.
                    _sink.Post(SocketEvent.Message(this, Encoding.UTF8.GetString(message.ToArray())));
                }
            }
        }

        private async Task AcknowledgeCloseAsync()
        {
            // Bounded: the acknowledgement is a courtesy to the peer, and a send stuck
            // against a full TCP buffer must not turn it into a hang.
            if (!await WaitForSendAsync().ConfigureAwait(false))
            {
                return;
            }

            try
            {
                if (_socket.State == WebSocketState.CloseReceived)
                {
                    using (var grace = new CancellationTokenSource(TimeSpan.FromSeconds(2)))
                    {
                        await _socket.CloseOutputAsync(
                                WebSocketCloseStatus.NormalClosure,
                                string.Empty,
                                grace.Token)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (Exception)
            {
                // The peer is gone either way; the close is reported regardless.
            }
            finally
            {
                Release(_sendLock);
            }
        }

        private async Task SendAsync(string text)
        {
            // The wait is inside the try: SendText discards this task, so an
            // ObjectDisposedException from a semaphore Dispose raced this far would
            // otherwise be captured into a task nobody observes.
            var held = false;
            try
            {
                held = await WaitForSendAsync().ConfigureAwait(false);
                if (!held || _socket.State != WebSocketState.Open)
                {
                    return;
                }

                var bytes = Encoding.UTF8.GetBytes(text);
                await _socket.SendAsync(
                        new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text,
                        true,
                        _cancellation.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                PostClose(1006, "abnormal");
            }
            finally
            {
                if (held)
                {
                    Release(_sendLock);
                }
            }
        }

        /// <summary>
        /// Takes the send lock with a bound, reporting whether it was acquired. Every
        /// write to the socket goes through it, because a second concurrent write
        /// aborts the socket on the older BCLs Unity ships.
        /// </summary>
        private async Task<bool> WaitForSendAsync()
        {
            try
            {
                return await _sendLock.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        private async Task CloseAsync()
        {
            // The close is a write like any other, so it takes the same lock: two
            // outstanding writes on one ClientWebSocket abort it on the BCL vintages
            // Unity's Mono and IL2CPP backends ship, and the requested code never
            // reaches the gateway.
            var held = await WaitForSendAsync().ConfigureAwait(false);
            try
            {
                if (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived)
                {
                    using (var grace = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token))
                    {
                        grace.CancelAfter(TimeSpan.FromSeconds(2));
                        await _socket.CloseOutputAsync(
                                (WebSocketCloseStatus)_localCloseCode,
                                _localCloseReason,
                                grace.Token)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (Exception)
            {
                // The close handshake failing changes nothing: the socket is over
                // either way, and PostClose below reports it exactly once.
            }
            finally
            {
                if (held)
                {
                    Release(_sendLock);
                }
            }

            PostClose(_localCloseCode, _localCloseReason);
        }

        /// <summary>
        /// Reports the close exactly once. A locally requested close reports the code
        /// this SDK asked for, not whatever the peer echoed, because the state machine
        /// keyed its own decision on that code.
        /// </summary>
        private void PostClose(int code, string reason)
        {
            if (Interlocked.Exchange(ref _closePosted, 1) == 1)
            {
                return;
            }

            var local = Volatile.Read(ref _localCloseCode);
            _sink.Post(SocketEvent.Closed(
                this,
                local != 0 ? local : code,
                local != 0 ? _localCloseReason : reason));
        }

        private static void Release(SemaphoreSlim semaphore)
        {
            try
            {
                semaphore.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static string Truncate(string reason)
        {
            if (Encoding.UTF8.GetByteCount(reason) <= MaxCloseReasonBytes)
            {
                return reason;
            }

            // Cut on a character boundary; CloseOutputAsync throws on a longer reason.
            // Start at the byte limit rather than the length: every char is at least
            // one UTF-8 byte, so nothing beyond it can fit, and starting at
            // reason.Length made this O(n^2) in both time and allocation on a string
            // a caller chose.
            var length = Math.Min(reason.Length, MaxCloseReasonBytes);
            while (length > 0 && Encoding.UTF8.GetByteCount(reason.Substring(0, length)) > MaxCloseReasonBytes)
            {
                length--;
            }

            // Never end on a high surrogate: the lone half would encode as U+FFFD and
            // read as corruption rather than as a truncation.
            if (length > 0 && length < reason.Length && char.IsHighSurrogate(reason[length - 1]))
            {
                length--;
            }

            return reason.Substring(0, length);
        }

        private static void RequireHttpToken(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException("A subprotocol cannot be empty.");
            }

            for (var index = 0; index < value.Length; index++)
            {
                var c = value[index];
                var ok = (c >= 'a' && c <= 'z')
                    || (c >= 'A' && c <= 'Z')
                    || (c >= '0' && c <= '9')
                    || "!#$%&'*+-.^_`|~".IndexOf(c) >= 0;
                if (!ok)
                {
                    // A JWT is base64url plus dots, all legal here. A padded or
                    // standard-base64 token is not, and failing now is what turns it
                    // into a clear "cannot open WebSocket" instead of a silent retry.
                    //
                    // The offending character is NOT in the message: the second
                    // subprotocol is the token, this message becomes a close reason
                    // that GatewaySocket logs, and rules/security.md admits no
                    // severity threshold for a credential reaching a log line. The
                    // position says as much for debugging and leaks nothing.
                    throw new ArgumentException(
                        "A subprotocol may only contain HTTP token characters; the one at index "
                        + index + " does not.");
                }
            }
        }
    }
}
