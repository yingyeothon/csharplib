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

        private async Task<HttpListenerWebSocketContext> AcceptAsync(string? subProtocol = "bearer")
        {
            var context = await _listener!.GetContextAsync().ConfigureAwait(false);
            return await context.AcceptWebSocketAsync(subProtocol).ConfigureAwait(false);
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
            var sink = new CollectingSink();
            var accepting = AcceptAsync();
            using var socket = Connect(sink);
            var server = await accepting;
            await sink.NextAsync(Timeout);

            // Split inside the three bytes of a Hangul syllable. Decoding per frame
            // would corrupt it; only whole-message decoding survives.
            var payload = Encoding.UTF8.GetBytes("{\"type\":\"say\",\"text\":\"한글 테스트\"}");
            var split = 12;
            await server.WebSocket.SendAsync(
                new ArraySegment<byte>(payload, 0, split), WebSocketMessageType.Text, false, CancellationToken.None);
            await server.WebSocket.SendAsync(
                new ArraySegment<byte>(payload, split, payload.Length - split),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None);

            var message = await sink.NextAsync(Timeout);

            Assert.That(message.Kind, Is.EqualTo(SocketEventKind.Message));
            Assert.That(message.IsText, Is.True);
            Assert.That(message.Text, Is.EqualTo("{\"type\":\"say\",\"text\":\"한글 테스트\"}"));
        }

        [Test]
        public async Task ABinaryFrameIsReportedAsANonTextMessage()
        {
            var sink = new CollectingSink();
            var accepting = AcceptAsync();
            using var socket = Connect(sink);
            var server = await accepting;
            await sink.NextAsync(Timeout);

            await server.WebSocket.SendAsync(
                new ArraySegment<byte>(new byte[] { 1, 2, 3 }),
                WebSocketMessageType.Binary,
                true,
                CancellationToken.None);

            var message = await sink.NextAsync(Timeout);

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
            var sink = new CollectingSink();
            var accepting = AcceptAsync();
            using var socket = Connect(sink);
            var server = await accepting;
            await sink.NextAsync(Timeout);

            socket.SendText("{\"type\":\"ping\"}");

            var buffer = new byte[256];
            var result = await server.WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            Assert.That(Encoding.UTF8.GetString(buffer, 0, result.Count), Is.EqualTo("{\"type\":\"ping\"}"));
        }

        [Test]
        public async Task AServerCloseIsReportedWithItsCode()
        {
            var sink = new CollectingSink();
            var accepting = AcceptAsync();
            using var socket = Connect(sink);
            var server = await accepting;
            await sink.NextAsync(Timeout);

            await server.WebSocket.CloseAsync((WebSocketCloseStatus)4002, "idle", CancellationToken.None);

            var closed = await sink.NextAsync(Timeout);

            Assert.That(closed.Kind, Is.EqualTo(SocketEventKind.Closed));
            Assert.That(closed.Code, Is.EqualTo(4002));
        }

        [Test]
        public async Task ALocalCloseReportsTheCodeThisSdkAskedFor()
        {
            // The state machine keys its decision on the code it requested, so the
            // adapter must not report whatever the peer echoed back instead.
            var sink = new CollectingSink();
            var accepting = AcceptAsync();
            using var socket = Connect(sink);
            var server = await accepting;
            await sink.NextAsync(Timeout);

            socket.Close(GatewayCloseCode.Local, "unexpected subprotocol");

            var closed = await sink.NextAsync(Timeout);

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
    }
}
