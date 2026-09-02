using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Yingyeothon.Gamebase.Client.Tests
{
    internal sealed class CollectingSink : IWebSocketEventSink
    {
        private readonly ConcurrentQueue<SocketEvent> _events = new ConcurrentQueue<SocketEvent>();

        public void Post(SocketEvent socketEvent) => _events.Enqueue(socketEvent);

        internal bool TryDequeue(out SocketEvent socketEvent)
        {
            // The close of an unconnected socket is posted synchronously, so a test
            // that only wants that one does not need to wait for it.
            for (var i = 0; i < 200; i++)
            {
                if (_events.TryDequeue(out socketEvent))
                {
                    return true;
                }

                Thread.Sleep(5);
            }

            socketEvent = default;
            return false;
        }

        internal async Task<SocketEvent> NextAsync(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (_events.TryDequeue(out var socketEvent))
                {
                    return socketEvent;
                }

                await Task.Delay(10).ConfigureAwait(false);
            }

            throw new TimeoutException("no socket event arrived within " + timeout);
        }
    }

    /// <summary>
    /// Exercises the real <c>ClientWebSocket</c> adapter against a local server.
    /// </summary>
    /// <remarks>
    /// The fake socket cannot reach any of this: subprotocol negotiation, a message
    /// fragmented across frames in the middle of a UTF-8 sequence, and — the one that
    /// decides whether the reconnect policy works at all — a refused handshake
    /// arriving as a close rather than as a thrown exception.
    /// </remarks>
    [TestFixture]
    [Category("Integration")]
    public class ClientWebSocketTransportTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private HttpListener? _listener;
        private string _url = string.Empty;

        [SetUp]
        public void SetUp()
        {
            var port = FreePort();
            _listener = new HttpListener();
            _listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
            _listener.Start();
            _url = "ws://127.0.0.1:" + port + "/";
        }

        [TearDown]
        public void TearDown()
        {
            _listener?.Close();
            _listener = null;
        }

        private static int FreePort()
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private IWebSocket Connect(CollectingSink sink, IReadOnlyList<string>? protocols = null)
        {
            var socket = WebSocketTransport.Default.Create(new WebSocketCreateContext(
                _url,
                protocols ?? new[] { "bearer", "eyJ.token.sig" },
                sink));
            socket.Start();
            return socket;
        }

        /// <summary>
        /// A socket that has finished its handshake: the server side, the sink, and
        /// the client socket with its open event already consumed.
        /// </summary>
        private sealed class OpenSession : IDisposable
        {
            public OpenSession(CollectingSink sink, IWebSocket socket, HttpListenerWebSocketContext server)
            {
                Sink = sink;
                Socket = socket;
                Server = server;
            }

            public CollectingSink Sink { get; }

            public IWebSocket Socket { get; }

            public HttpListenerWebSocketContext Server { get; }

            public void Dispose() => Socket.Dispose();
        }

        /// <summary>Connects with the bearer subprotocol and waits for the open event, so a test starts on a ready socket.</summary>
        private async Task<OpenSession> OpenAsync()
        {
            var sink = new CollectingSink();
            var accepting = AcceptAsync();
            var socket = Connect(sink);
            try
            {
                var server = await accepting;
                await sink.NextAsync(Timeout);
                return new OpenSession(sink, socket, server);
            }
            catch
            {
                // The `using var socket` this replaced disposed on the way out too.
                socket.Dispose();
                throw;
            }
        }

        /// <summary>Sends one text frame from the server; <c>endOfMessage: false</c> leaves the message open for more frames.</summary>
        private static Task SendTextAsync(WebSocket server, byte[] payload, bool endOfMessage = true)
            => SendTextAsync(server, payload, 0, payload.Length, endOfMessage);

        private static Task SendTextAsync(WebSocket server, byte[] payload, int offset, int count, bool endOfMessage = true)
            => server.SendAsync(new ArraySegment<byte>(payload, offset, count), WebSocketMessageType.Text, endOfMessage, CancellationToken.None);

        /// <remarks>
        /// The server half of these tests, not the client half. Unity's Mono never
        /// implemented server-side WebSocket in HttpListener and throws
        /// NotImplementedException from AcceptWebSocketAsync, so inside the editor
        /// these are reported as ignored with the reason rather than as eleven red
        /// tests nobody can act on. The client half — the transport this SDK actually
        /// ships — is covered against the real gateway; see
        /// rules/manual-verification.md.
        /// </remarks>
        private async Task<HttpListenerWebSocketContext> AcceptAsync(string? subProtocol = "bearer")
        {
            var context = await _listener!.GetContextAsync().ConfigureAwait(false);
            try
            {
                return await context.AcceptWebSocketAsync(subProtocol).ConfigureAwait(false);
            }
            catch (NotImplementedException)
            {
                Assert.Ignore(
                    "HttpListener cannot accept a WebSocket on this runtime "
                    + "(Unity's Mono); the real transport is covered against the dev gateway.");
                throw;
            }
        }

        [Test]
        public async Task NegotiatesTheBearerSubprotocolAndReportsItOnOpen()
        {
            var sink = new CollectingSink();
            var accepting = AcceptAsync();
            using var socket = Connect(sink);

            var server = await accepting;
            var opened = await sink.NextAsync(Timeout);

            Assert.That(opened.Kind, Is.EqualTo(SocketEventKind.Opened));
            Assert.That(opened.Protocol, Is.EqualTo("bearer"));
            Assert.That(server.WebSocket.SubProtocol, Is.EqualTo("bearer"));
        }

        [Test]
        public async Task AServerThatEchoesNoSubprotocolReportsAnEmptyOne()
        {
            var sink = new CollectingSink();
            var accepting = AcceptAsync(null);
            using var socket = Connect(sink);

            await accepting;
            var opened = await sink.NextAsync(Timeout);

            Assert.That(opened.Protocol, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task ReassemblesAMessageSplitMidUtf8SequenceAcrossFrames()
        {
            using var session = await OpenAsync();

            // Split inside the three bytes of a Hangul syllable. Decoding per frame
            // would corrupt it; only whole-message decoding survives.
            var payload = Encoding.UTF8.GetBytes("{\"type\":\"say\",\"text\":\"한글 테스트\"}");
            var split = 12;
            await SendTextAsync(session.Server.WebSocket, payload, 0, split, endOfMessage: false);
            await SendTextAsync(session.Server.WebSocket, payload, split, payload.Length - split);

            var message = await session.Sink.NextAsync(Timeout);

            Assert.That(message.Kind, Is.EqualTo(SocketEventKind.Message));
            Assert.That(message.IsText, Is.True);
            Assert.That(message.Text, Is.EqualTo("{\"type\":\"say\",\"text\":\"한글 테스트\"}"));
        }

        [Test]
        public async Task ABinaryFrameIsReportedAsANonTextMessage()
        {
            using var session = await OpenAsync();

            await session.Server.WebSocket.SendAsync(
                new ArraySegment<byte>(new byte[] { 1, 2, 3 }),
                WebSocketMessageType.Binary,
                true,
                CancellationToken.None);

            var message = await session.Sink.NextAsync(Timeout);

            Assert.That(message.Kind, Is.EqualTo(SocketEventKind.Message));
            Assert.That(message.IsText, Is.False);
        }

        [Test]
        public async Task ARefusedHandshakeArrivesAsACloseNotAnException()
        {
            // This is what makes the handshake-failure policy work: a browser only
            // ever sees 401/403/404/410 as a close before open, and .NET would
            // otherwise raise it out of ConnectAsync where nothing is listening.
            var sink = new CollectingSink();
            var refusing = Task.Run(async () =>
            {
                var context = await _listener!.GetContextAsync().ConfigureAwait(false);
                context.Response.StatusCode = 401;
                context.Response.Close();
            });

            using var socket = Connect(sink);
            await refusing;

            var closed = await sink.NextAsync(Timeout);

            Assert.That(closed.Kind, Is.EqualTo(SocketEventKind.Closed));
            Assert.That(closed.Code, Is.EqualTo(1006));
        }

        [Test]
        public async Task ASendReachesTheServer()
        {
            using var session = await OpenAsync();

            session.Socket.SendText("{\"type\":\"ping\"}");

            var buffer = new byte[256];
            var result = await session.Server.WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            Assert.That(Encoding.UTF8.GetString(buffer, 0, result.Count), Is.EqualTo("{\"type\":\"ping\"}"));
        }

        [Test]
        public async Task AServerCloseIsReportedWithItsCode()
        {
            using var session = await OpenAsync();

            await session.Server.WebSocket.CloseAsync((WebSocketCloseStatus)4002, "idle", CancellationToken.None);

            var closed = await session.Sink.NextAsync(Timeout);

            Assert.That(closed.Kind, Is.EqualTo(SocketEventKind.Closed));
            Assert.That(closed.Code, Is.EqualTo(4002));
        }

        [Test]
        public async Task ALocalCloseReportsTheCodeThisSdkAskedFor()
        {
            // The state machine keys its decision on the code it requested, so the
            // adapter must not report whatever the peer echoed back instead.
            using var session = await OpenAsync();

            session.Socket.Close(GatewayCloseCode.Local, "unexpected subprotocol");

            var closed = await session.Sink.NextAsync(Timeout);

            Assert.That(closed.Code, Is.EqualTo(GatewayCloseCode.Local));
            Assert.That(closed.Reason, Is.EqualTo("unexpected subprotocol"));
        }

        [Test]
        public void ASubprotocolWithIllegalCharactersIsRefusedUpFront()
        {
            // A padded base64 token would be rejected by the transport at connect
            // time in a way no close event explains; failing here turns it into a
            // clear "cannot open WebSocket".
            var sink = new CollectingSink();

            Assert.Throws<ArgumentException>(() => WebSocketTransport.Default.Create(
                new WebSocketCreateContext(_url, new[] { "bearer", "token==" }, sink)));
            Assert.Throws<ArgumentException>(() => WebSocketTransport.Default.Create(
                new WebSocketCreateContext(_url, new[] { "bearer", "with space" }, sink)));
        }

        [Test]
        public void AMalformedUrlIsRefusedUpFront()
        {
            Assert.Throws<UriFormatException>(() => WebSocketTransport.Default.Create(
                new WebSocketCreateContext("not a url", new[] { "bearer", "t" }, new CollectingSink())));
        }

        [Test]
        public async Task DisposingBeforeAnyCloseStillReportsOne()
        {
            var sink = new CollectingSink();
            var accepting = AcceptAsync();
            var socket = Connect(sink);
            await accepting;
            await sink.NextAsync(Timeout);

            socket.Dispose();

            var closed = await sink.NextAsync(Timeout);

            Assert.That(closed.Kind, Is.EqualTo(SocketEventKind.Closed));
            Assert.That(closed.Code, Is.EqualTo(1006));
        }

        /// <remarks>
        /// The gateway caps its outbound frames at 32 KB, so nothing legitimate is
        /// near this. Without the cap a peer streaming continuation frames grew a
        /// MemoryStream without bound, and the codec's own 1 MiB limit could not help:
        /// it is checked against a string the transport has already built.
        /// </remarks>
        [Test]
        public async Task AMessageOverTheSizeCapArrivesAsACloseInsteadOfGrowingForever()
        {
            using var session = await OpenAsync();

            // One message, streamed as continuation frames that never end.
            var chunk = new byte[16 * 1024];
            for (var i = 0; i < chunk.Length; i++)
            {
                chunk[i] = (byte)'x';
            }

            for (var i = 0; i < 8; i++)
            {
                try
                {
                    await SendTextAsync(session.Server.WebSocket, chunk, endOfMessage: false);
                }
                catch (WebSocketException)
                {
                    // The client has already closed on us, which is the point.
                    break;
                }
            }

            var closed = await session.Sink.NextAsync(Timeout);

            Assert.That(closed.Kind, Is.EqualTo(SocketEventKind.Closed));
            Assert.That(closed.Code, Is.EqualTo(1009));

            // 1009 must stop rather than reconnect: a client that reconnected would
            // meet the same flood.
            Assert.That(
                CloseCodes.Classify(closed.Code, GatewayChannelKind.Lobby).Kind,
                Is.EqualTo(CloseDispositionKind.ClientBug));
        }

        /// <remarks>
        /// A message just under the cap must still arrive whole — the pair is what
        /// pins the boundary rather than a cap that swallowed everything.
        /// </remarks>
        [Test]
        public async Task AMessageUnderTheSizeCapStillArrivesWhole()
        {
            using var session = await OpenAsync();

            var text = new string('y', 63 * 1024);
            await SendTextAsync(session.Server.WebSocket, Encoding.UTF8.GetBytes(text));

            var message = await session.Sink.NextAsync(Timeout);

            Assert.That(message.Kind, Is.EqualTo(SocketEventKind.Message));
            Assert.That(message.Text, Is.EqualTo(text));
        }

        /// <remarks>
        /// The second subprotocol is the JWT, and this message becomes a close reason
        /// that GatewaySocket logs at info. rules/security.md admits no severity
        /// threshold for a credential reaching a log line.
        /// </remarks>
        [Test]
        public void ARefusedSubprotocolNeverQuotesTheOffendingTokenCharacter()
        {
            var sink = new CollectingSink();

            var error = Assert.Throws<ArgumentException>(() => WebSocketTransport.Default.Create(
                new WebSocketCreateContext(_url, new[] { "bearer", "eyJhbGci.PAYLOAD==" }, sink)));

            // Positive control: it did refuse, and it says why and where.
            Assert.That(error!.Message, Does.Contain("HTTP token characters"));
            Assert.That(error.Message, Does.Contain("index 16"));
            Assert.That(error.Message, Does.Not.Contain("="));
            Assert.That(error.Message, Does.Not.Contain("PAYLOAD"));
        }

        /// <remarks>
        /// 0 is the transport's "no local close yet" sentinel, so storing it left the
        /// latch open, let the next call overwrite the reason, and posted a close of 0
        /// that CloseCodes reads as unknown and retries.
        /// </remarks>
        [Test]
        public async Task ACloseCodeAClientMayNotSendIsRefusedUpFront()
        {
            using var session = await OpenAsync();

            Assert.Throws<ArgumentOutOfRangeException>(() => session.Socket.Close(0, "uninitialised"));
            Assert.Throws<ArgumentOutOfRangeException>(() => session.Socket.Close(1006, "abnormal"));
            Assert.Throws<ArgumentOutOfRangeException>(() => session.Socket.Close(999, "reserved"));

            // Positive control: the codes a client may send are accepted.
            Assert.DoesNotThrow(() => session.Socket.Close(GatewayCloseCode.Local, "hello timeout"));

            var closed = await session.Sink.NextAsync(Timeout);

            Assert.That(closed.Code, Is.EqualTo(GatewayCloseCode.Local));
        }
    }
}
