using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Yingyeothon.Codec;

namespace Yingyeothon.Gamebase.Client.Tests
{
    [TestFixture]
    public class LobbyReconnectTests
    {
        [TestCase(4002)]
        [TestCase(1001)]
        [TestCase(1006)]
        [TestCase(1011)]
        public async Task ReconnectsWithBackoffAndAFreshPeerMap(int code)
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync();
            var order = new List<string>();
            harness.Client.Disconnected += e => order.Add("disconnected:" + e.Code + ":" + e.WillReconnect);
            harness.Client.Reconnecting += e => order.Add("reconnecting:" + e.Attempt + ":" + e.DelayMillis);
            harness.Client.Connected += _ => order.Add("connected");

            harness.Socket.ServerSend(Frames.Snapshot("town", Frames.Peer("bob", 1, 1)));
            harness.Poll();
            Assert.That(harness.Client.Peers.All(), Has.Count.EqualTo(1));

            harness.Socket.ServerClose(code, "bye");
            harness.Poll();

            Assert.That(harness.Client.State, Is.EqualTo(GatewayClientState.Reconnecting));
            Assert.That(harness.Client.Peers.All(), Is.Empty);

            harness.Advance(499);
            Assert.That(harness.Factory.Sockets, Has.Count.EqualTo(1));

            harness.Advance(1);
            Assert.That(harness.Factory.Sockets, Has.Count.EqualTo(2));

            harness.Socket.ServerError();
            harness.Poll();
            harness.Advance(1000);
            Assert.That(harness.Factory.Sockets, Has.Count.EqualTo(3));

            harness.Socket.ServerOpen();
            harness.Socket.ServerSend(Frames.Hello(partyId: "pty_after"));
            harness.Poll();

            Assert.That(harness.Client.State, Is.EqualTo(GatewayClientState.Connected));
            Assert.That(harness.Client.PartyId, Is.EqualTo("pty_after"));
            Assert.That(order, Is.EqualTo(new[]
            {
                "disconnected:" + code + ":True",
                "reconnecting:1:500",
                "disconnected:1006:True",
                "reconnecting:2:1000",
                "connected",
            }));

            // The backoff reset on a successful connect, so the next drop starts over.
            harness.Socket.ServerClose(4002);
            harness.Poll();
            harness.Advance(500);
            Assert.That(harness.Factory.Sockets, Has.Count.EqualTo(4));
        }

        [TestCase(4000)]
        [TestCase(4003)]
        [TestCase(4004)]
        [TestCase(1000)]
        [TestCase(1003)]
        [TestCase(1009)]
        public async Task StopsWithoutOpeningAnotherSocket(int code)
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync();
            DisconnectedEvent? disconnected = null;
            StoppedEvent? stopped = null;
            harness.Client.Disconnected += e => disconnected = e;
            harness.Client.Stopped += e => stopped = e;

            harness.Socket.ServerClose(code);
            harness.Poll();
            harness.Advance(60000);

            Assert.That(harness.Factory.Sockets, Has.Count.EqualTo(1));
            Assert.That(harness.Client.State, Is.EqualTo(GatewayClientState.Closed));
            Assert.That(disconnected!.Value.Code, Is.EqualTo(code));
            Assert.That(disconnected.Value.WillReconnect, Is.False);
            Assert.That(stopped!.Value.Code, Is.EqualTo(code));
            Assert.That(stopped.Value.Kind, Is.Not.EqualTo(CloseDispositionKind.Reconnect));
        }

        [Test]
        public void StopsAfterRepeatedClosesBeforeOpenButNotAfterARealSession()
        {
            var harness = new LobbyHarness(o => o.MaxHandshakeFailures = 3);
            StoppedEvent? stopped = null;
            harness.Client.Stopped += e => stopped = e;
            var pending = harness.Client.ConnectAsync();

            // A refused handshake is invisible except as a close before open, so a run
            // of them has to end the session rather than retry a dead token forever.
            harness.Socket.ServerError();
            harness.Poll();
            harness.Advance(500);
            harness.Socket.ServerError();
            harness.Poll();
            harness.Advance(1000);
            harness.Socket.ServerError();
            harness.Poll();

            Assert.That(stopped!.Value.Reason, Is.EqualTo("handshake failed 3 times in a row"));
            Assert.That(harness.Factory.Sockets, Has.Count.EqualTo(3));
            Assert.That(pending.IsFaulted, Is.True);
        }

        [Test]
        public async Task ASuccessfulOpenResetsTheHandshakeFailureCounter()
        {
            var harness = new LobbyHarness(o => o.MaxHandshakeFailures = 3);
            var pending = harness.Client.ConnectAsync();
            harness.Socket.ServerError();
            harness.Poll();
            harness.Advance(500);
            harness.Socket.ServerError();
            harness.Poll();
            harness.Advance(1000);

            harness.Socket.ServerOpen();
            harness.Socket.ServerSend(Frames.Hello());
            harness.Poll();
            await pending;

            // Two failures happened, but a real session wipes them: the next two
            // failures must not add up to three.
            harness.Socket.ServerClose(4002);
            harness.Poll();
            harness.Advance(500);
            harness.Socket.ServerError();
            harness.Poll();
            harness.Advance(1000);
            harness.Socket.ServerError();
            harness.Poll();

            Assert.That(harness.Client.State, Is.EqualTo(GatewayClientState.Reconnecting));
        }

        [Test]
        public async Task GivesUpAfterMaxAttempts()
        {
            var harness = new LobbyHarness(o =>
                o.Backoff = new BackoffOptions { InitialMs = 500, Jitter = 0, MaxAttempts = 2, Random = () => 0 });
            await harness.ConnectAsync();
            StoppedEvent? stopped = null;
            harness.Client.Stopped += e => stopped = e;

            harness.Socket.ServerClose(4002);
            harness.Poll();
            harness.Advance(500);
            harness.Socket.ServerError();
            harness.Poll();
            harness.Advance(1000);
            harness.Socket.ServerError();
            harness.Poll();

            Assert.That(stopped!.Value.Reason, Is.EqualTo("reconnect attempts exhausted"));
            Assert.That(harness.Factory.Sockets, Has.Count.EqualTo(3));
            harness.Advance(60000);
            Assert.That(harness.Factory.Sockets, Has.Count.EqualTo(3));
        }

        [Test]
        public async Task ForgetsTheRosterOnANewHelloWithoutAParty()
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync(Frames.Hello(partyId: "pty_1"));
            harness.Socket.ServerSend(Json.Object().Set("type", "party").Set("partyId", "pty_1")
                .Set("leaderId", "alice").Build());
            harness.Poll();
            Assert.That(harness.Client.Roster, Is.Not.Null);

            harness.Socket.ServerClose(4002);
            harness.Poll();
            harness.Advance(500);
            harness.Socket.ServerOpen();
            harness.Socket.ServerSend(Frames.Hello());
            harness.Poll();

            // A roster from before the outage may be stale; the gateway re-sends it
            // when it still knows the party.
            Assert.That(harness.Client.Roster, Is.Null);
            Assert.That(harness.Client.PartyId, Is.Null);
        }

        [Test]
        public async Task CloseEndsTheSessionWithoutReconnectingAndIsIdempotent()
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync();
            var disconnects = new List<DisconnectedEvent>();
            var stops = 0;
            harness.Client.Disconnected += e => disconnects.Add(e);
            harness.Client.Stopped += _ => stops++;

            harness.Client.Close();
            harness.Client.Close();
            harness.Advance(60000);

            Assert.That(harness.Factory.Sockets, Has.Count.EqualTo(1));
            Assert.That(harness.Client.State, Is.EqualTo(GatewayClientState.Closed));
            Assert.That(disconnects.Select(d => d.Code + ":" + d.WillReconnect), Is.EqualTo(new[] { "1000:False" }));

            // Close() is the caller's own decision, so it is not reported as a stop.
            Assert.That(stops, Is.Zero);
        }

        [Test]
        public async Task CloseWhileAReconnectIsPendingCancelsIt()
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync();
            harness.Socket.ServerClose(4002);
            harness.Poll();
            Assert.That(harness.Client.State, Is.EqualTo(GatewayClientState.Reconnecting));

            harness.Client.Close();
            harness.Advance(60000);

            Assert.That(harness.Factory.Sockets, Has.Count.EqualTo(1));
        }

        [Test]
        public void CloseBeforeHelloFailsThePendingConnectAndIgnoresALateOpen()
        {
            var harness = new LobbyHarness();
            var pending = harness.Client.ConnectAsync();

            harness.Client.Close();

            Assert.That(pending.IsFaulted, Is.True);
            Assert.That(pending.Exception!.InnerException, Is.TypeOf<GatewayStoppedException>());
            Assert.That(harness.Socket.ClientClose!.Value.Code, Is.EqualTo(1000));

            harness.Socket.ServerOpen();
            harness.Socket.ServerSend(Frames.Hello());
            harness.Poll();

            Assert.That(harness.Client.State, Is.EqualTo(GatewayClientState.Closed));
            Assert.That(harness.Client.Hello, Is.Null);
        }

        [Test]
        public async Task IgnoresEventsFromASocketItHasAlreadyReplaced()
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync();
            var stale = harness.Socket;

            stale.ServerClose(4002);
            harness.Poll();
            harness.Advance(500);
            var fresh = harness.Socket;
            Assert.That(fresh, Is.Not.SameAs(stale));

            var pongs = 0;
            var stops = 0;
            harness.Client.Pong += () => pongs++;
            harness.Client.Stopped += _ => stops++;

            stale.ServerSend(Json.Object().Set("type", "pong").Build());
            stale.ServerClose(4000);
            harness.Poll();

            Assert.That(pongs, Is.Zero);
            Assert.That(stops, Is.Zero);

            // A reopened socket stays "reconnecting" until its own hello, so the
            // stale close did not knock the live attempt off course.
            Assert.That(harness.Client.State, Is.EqualTo(GatewayClientState.Reconnecting));
        }

        [Test]
        public async Task EveryReplacedSocketIsDisposedExactlyOnce()
        {
            // TypeScript leaves this to the collector; here a socket that is not
            // disposed keeps a receive task and its cancellation source alive for the
            // rest of the session.
            var harness = new LobbyHarness();
            await harness.ConnectAsync();

            for (var i = 0; i < 5; i++)
            {
                harness.Socket.ServerClose(4002);
                harness.Poll();
                harness.Advance(500 * Math.Pow(2, i));
                harness.Socket.ServerOpen();
                harness.Socket.ServerSend(Frames.Hello());
                harness.Poll();
            }

            harness.Client.Close();

            Assert.That(harness.Factory.Sockets, Has.Count.EqualTo(6));
            Assert.That(harness.Factory.Sockets.Select(s => s.DisposeCount), Is.All.EqualTo(1));
        }

        /// <remarks>
        /// ScheduleReconnect raised Disconnected and only then armed the deadline, so
        /// "the connection dropped, tear it down" — a Close() from that handler — was
        /// followed by a Reconnecting event for a session the game had just ended.
        /// Only Open()'s own _closedByUser re-check kept a second socket from opening.
        /// </remarks>
        [Test]
        public async Task ClosingFromTheDisconnectedHandlerSuppressesTheReconnect()
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync();
            var order = new List<string>();
            harness.Client.Disconnected += e =>
            {
                order.Add("disconnected:" + e.Code + ":" + e.WillReconnect);
                harness.Client.Close();
            };
            harness.Client.Reconnecting += e => order.Add("reconnecting:" + e.Attempt);

            harness.Socket.ServerClose(GatewayCloseCode.Idle, "idle");
            harness.Poll();

            Assert.That(order, Is.EqualTo(new[] { "disconnected:4002:True" }));
            Assert.That(harness.Client.State, Is.EqualTo(GatewayClientState.Closed));

            // And no second socket, however long the game keeps pumping.
            harness.Advance(60000);

            Assert.That(harness.Factory.Sockets, Has.Count.EqualTo(1));
        }
    }
}
