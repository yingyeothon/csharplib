using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Yingyeothon.Codec;

namespace Yingyeothon.Gamebase.Client.Tests
{
    [TestFixture]
    public class LobbyHandshakeTests
    {
        [Test]
        public void OpensTheGatewayUrlWithTheBearerSubprotocolPair()
        {
            var harness = new LobbyHarness();

            _ = harness.Client.ConnectAsync();

            Assert.That(harness.Socket.Url, Is.EqualTo("wss://gw.test/?channel=ch_lobby"));
            Assert.That(harness.Socket.SubProtocols, Is.EqualTo(new[] { "bearer", LobbyHarness.Token }));
            Assert.That(harness.Socket.Started, Is.True);
            Assert.That(harness.Client.State, Is.EqualTo(GatewayClientState.Connecting));
        }

        [Test]
        public void IsNotConnectedOnOpenOnlyOnHello()
        {
            var harness = new LobbyHarness();
            var pending = harness.Client.ConnectAsync();

            harness.Socket.ServerOpen();
            harness.Poll();

            Assert.That(harness.Client.State, Is.EqualTo(GatewayClientState.Connecting));
            Assert.That(harness.Client.Hello, Is.Null);
            Assert.That(pending.IsCompleted, Is.False);

            harness.Socket.ServerSend(Frames.Hello());
            harness.Poll();

            Assert.That(harness.Client.State, Is.EqualTo(GatewayClientState.Connected));
            Assert.That(harness.Client.Hello!.UserId, Is.EqualTo("alice"));
        }

        [Test]
        public async Task ConnectCompletesOnlyAfterHelloIsVisibleOnTheClient()
        {
            // tslib gets this from the microtask queue: markReady resolves before the
            // hello handler runs, but the continuation still sees the filled-in
            // client. Settling inside the transition would break that here.
            var harness = new LobbyHarness();
            var pending = harness.Client.ConnectAsync();
            harness.Socket.ServerOpen();
            harness.Socket.ServerSend(Frames.Hello());
            harness.Poll();

            var hello = await pending;

            Assert.That(hello.UserId, Is.EqualTo("alice"));
            Assert.That(harness.Client.Hello, Is.SameAs(hello));
            Assert.That(harness.Client.Capabilities, Is.Not.Null);
        }

        [Test]
        public void NothingHappensWithoutPoll()
        {
            var harness = new LobbyHarness();
            var pending = harness.Client.ConnectAsync();
            harness.Socket.ServerOpen();
            harness.Socket.ServerSend(Frames.Hello());

            Assert.That(harness.Client.State, Is.EqualTo(GatewayClientState.Connecting));
            Assert.That(harness.Client.Hello, Is.Null);
            Assert.That(pending.IsCompleted, Is.False);
        }

        [Test]
        public void ANonHelloFirstFrameIsAProtocolErrorAndTheClientKeepsWaiting()
        {
            var harness = new LobbyHarness();
            var errors = new System.Collections.Generic.List<string>();
            harness.Client.ProtocolError += e => errors.Add(e.Message);
            _ = harness.Client.ConnectAsync();
            harness.Socket.ServerOpen();

            harness.Socket.ServerSend(Json.Object().Set("type", "pong").Build());
            harness.Poll();

            Assert.That(errors, Is.EqualTo(new[] { "expected hello, got pong" }));
            Assert.That(harness.Client.State, Is.EqualTo(GatewayClientState.Connecting));

            harness.Socket.ServerSend(Frames.Hello());
            harness.Poll();

            Assert.That(harness.Client.State, Is.EqualTo(GatewayClientState.Connected));
        }

        [Test]
        public void ReconnectsWhenHelloDoesNotArriveInTime()
        {
            var harness = new LobbyHarness();
            _ = harness.Client.ConnectAsync();
            harness.Socket.ServerOpen();
            harness.Poll();

            harness.Advance(9999);

            Assert.That(harness.Factory.Sockets, Has.Count.EqualTo(1));

            harness.Advance(1);

            Assert.That(harness.Client.State, Is.EqualTo(GatewayClientState.Reconnecting));
            Assert.That(harness.Socket.ClientClose!.Value.Code, Is.EqualTo(GatewayCloseCode.Local));

            harness.Advance(500);

            Assert.That(harness.Factory.Sockets, Has.Count.EqualTo(2));
        }

        [Test]
        public void ClearsTheHelloDeadlineOnceHelloArrives()
        {
            var harness = new LobbyHarness();
            _ = harness.Client.ConnectAsync();
            harness.Socket.ServerOpen();
            harness.Socket.ServerSend(Frames.Hello());
            harness.Poll();

            harness.Advance(60000);

            Assert.That(harness.Client.State, Is.EqualTo(GatewayClientState.Connected));
            Assert.That(harness.Factory.Sockets, Has.Count.EqualTo(1));
        }

        [Test]
        public void StopsWhenTheGatewayDoesNotEchoTheBearerSubprotocol()
        {
            var harness = new LobbyHarness();
            StoppedEvent? stopped = null;
            harness.Client.Stopped += e => stopped = e;
            var pending = harness.Client.ConnectAsync();

            harness.Socket.ServerOpen(string.Empty);
            harness.Poll();

            Assert.That(stopped, Is.Not.Null);
            Assert.That(stopped!.Value.Kind, Is.EqualTo(CloseDispositionKind.Stop));
            Assert.That(stopped.Value.Reason, Is.EqualTo("gateway did not select the bearer subprotocol"));

            // The SDK's own close code has to survive back through the transport, or
            // the stop cannot be told apart from a gateway-initiated one.
            Assert.That(stopped.Value.Code, Is.EqualTo(GatewayCloseCode.Local));
            Assert.That(harness.Factory.Sockets, Has.Count.EqualTo(1));
            Assert.That(pending.IsFaulted, Is.True);
        }

        [Test]
        public void ConnectFailsWhenTheSocketCannotBeConstructed()
        {
            var harness = new LobbyHarness();
            harness.Factory.CreateOverride = _ => throw new UriFormatException("bad url");
            StoppedEvent? stopped = null;
            harness.Client.Stopped += e => stopped = e;

            var pending = harness.Client.ConnectAsync();

            Assert.That(pending.IsFaulted, Is.True);
            Assert.That(stopped!.Value.Reason, Is.EqualTo("cannot open WebSocket: bad url"));
            Assert.That(harness.Client.State, Is.EqualTo(GatewayClientState.Closed));
        }

        [Test]
        public void ASecondConnectIsRefused()
        {
            var harness = new LobbyHarness();
            _ = harness.Client.ConnectAsync();

            var second = harness.Client.ConnectAsync();

            Assert.That(second.IsFaulted, Is.True);
            Assert.That(second.Exception!.InnerException, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public async Task NeverWritesTheTokenToTheLog()
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync();
            harness.Socket.ServerClose(4002);
            harness.Poll();
            harness.Advance(500);
            harness.Socket.ServerClose(4000);
            harness.Poll();

            // Positive control: the assertion below would pass just as well against an
            // empty log, so pin that the expected lines really were written.
            Assert.That(harness.Log.Text, Does.Contain("lobby connected"));
            Assert.That(harness.Log.Text, Does.Contain("gateway reconnecting"));
            Assert.That(harness.Log.Text, Does.Not.Contain(LobbyHarness.Token));
            Assert.That(harness.Log.Text, Does.Not.Contain("secret-token"));
            Assert.That(harness.Log.Text, Does.Not.Contain("bearer"));
        }

        [Test]
        public async Task TheClientMayBePumpedFromWhicheverThreadTheHostResumesOn()
        {
            // A host with no synchronization context resumes every await on a
            // different pool thread while still using the client one call at a time.
            // Pinning the pump to one thread identity would reject that, so only
            // genuine concurrency is refused.
            var harness = new LobbyHarness();
            var pending = harness.Client.ConnectAsync();

            await Task.Run(() =>
            {
                harness.Socket.ServerOpen();
                harness.Socket.ServerSend(Frames.Hello());
                harness.Poll();
            });

            await pending;
            await Task.Run(() =>
            {
                harness.Client.Ping();
                harness.Poll();
            });

            Assert.That(harness.Socket.Sent, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task SendingWhileAnotherThreadIsPollingIsRefused()
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync();
            var inHandler = new SemaphoreSlim(0, 1);
            var release = new SemaphoreSlim(0, 1);
            Exception? caught = null;

            harness.Client.Pong += () =>
            {
                inHandler.Release();
                release.Wait(TimeSpan.FromSeconds(5));
            };

            harness.Socket.ServerSend(Json.Object().Set("type", "pong").Build());
            var pumping = Task.Run(() => harness.Poll());

            Assert.That(inHandler.Wait(TimeSpan.FromSeconds(5)), Is.True);
            try
            {
                harness.Client.Ping();
            }
            catch (Exception error)
            {
                caught = error;
            }
            finally
            {
                release.Release();
            }

            await pumping;

            Assert.That(caught, Is.TypeOf<InvalidOperationException>());
            Assert.That(caught!.Message, Does.Contain("another thread"));
        }

        [Test]
        public async Task AHandlerMaySendDuringItsOwnPump()
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync();
            harness.Client.Pong += () => harness.Client.Ping();

            harness.Socket.ServerSend(Json.Object().Set("type", "pong").Build());
            harness.Poll();

            Assert.That(harness.Socket.Sent, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task PollIsNotReEntrant()
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync();
            Exception? caught = null;
            harness.Client.Pong += () =>
            {
                try
                {
                    harness.Poll();
                }
                catch (Exception error)
                {
                    caught = error;
                }
            };

            harness.Socket.ServerSend(Json.Object().Set("type", "pong").Build());
            harness.Poll();

            Assert.That(caught, Is.TypeOf<InvalidOperationException>());
        }
    }
}
