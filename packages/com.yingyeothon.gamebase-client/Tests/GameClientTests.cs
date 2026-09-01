using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Yingyeothon.Codec;

namespace Yingyeothon.Gamebase.Client.Tests
{
    [TestFixture]
    public class GameClientTests
    {
        [Test]
        public void ConnectsWithChannelAndGameIdAndIsReadyOnOpen()
        {
            var harness = new GameHarness();
            var connects = 0;
            harness.Client.Connected += () => connects++;
            var pending = harness.Client.ConnectAsync();

            Assert.That(harness.Socket.Url, Is.EqualTo("wss://gw.test/?channel=q_dungeon&gameId=g_1"));
            Assert.That(harness.Socket.SubProtocols, Is.EqualTo(new[] { "bearer", GameHarness.Token }));
            Assert.Throws<InvalidOperationException>(
                () => harness.Client.Send(Json.Object().Set("type", "attack").Build()));

            harness.Socket.ServerOpen();
            harness.Poll();

            Assert.That(pending.IsCompletedSuccessfully, Is.True);
            Assert.That(harness.Client.State, Is.EqualTo(GatewayClientState.Connected));
            Assert.That(connects, Is.EqualTo(1));
        }

        [Test]
        public async Task PassesGameFramesThroughVerbatimAndSeparatesGatewayErrors()
        {
            var harness = new GameHarness();
            await harness.ConnectAsync();
            var frames = new List<JsonValue>();
            var errors = new List<ErrorFrame>();
            harness.Client.Frame += f => frames.Add(f);
            harness.Client.Refused += e => errors.Add(e);

            var snapshot = Json.Parse("{\"type\":\"snapshot\",\"tick\":3,\"units\":[{\"hp\":10}]}");
            harness.Socket.ServerSend(snapshot);
            harness.Socket.ServerSend(Json.Object().Set("type", "error").Set("code", "rate_limited").Set("message", "slow").Build());

            // `error` without the string fields belongs to the game, not the gateway.
            harness.Socket.ServerSend(Json.Object().Set("type", "error").Set("code", 5d).Build());
            harness.Poll();

            Assert.That(frames, Has.Count.EqualTo(2));
            Assert.That(frames[0], Is.EqualTo(snapshot));
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0].Code, Is.EqualTo("rate_limited"));
        }

        [Test]
        public async Task SendsOpaqueFramesAndRefusesTheReservedTypesLocally()
        {
            var harness = new GameHarness();
            await harness.ConnectAsync();

            harness.Client.Send(Json.Object().Set("type", "attack").Set("power", 3d).Build());

            Assert.That(Json.Stringify(harness.Socket.Sent[0]), Is.EqualTo("{\"type\":\"attack\",\"power\":3}"));

            // The gateway synthesises these and uses them to decide which member a
            // connection speaks for, so a client must never forge one.
            Assert.Throws<InvalidOperationException>(
                () => harness.Client.Send(Json.Object().Set("type", "enter").Build()));
            Assert.Throws<InvalidOperationException>(
                () => harness.Client.Send(Json.Object().Set("type", "leave").Build()));
            Assert.That(harness.Socket.Sent, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task Close4001IsAbortedNotFinishedAndNeverReconnects()
        {
            var harness = new GameHarness();
            await harness.ConnectAsync();
            GameEndedEvent? aborted = null;
            var finished = 0;
            var stopped = 0;
            harness.Client.Aborted += e => aborted = e;
            harness.Client.Finished += _ => finished++;
            harness.Client.Stopped += _ => stopped++;

            harness.Socket.ServerClose(GatewayCloseCode.Aborted);
            harness.Poll();
            harness.Advance(60000);

            Assert.That(aborted!.Value.Code, Is.EqualTo(4001));
            Assert.That(aborted.Value.Reason, Is.EqualTo("the game actor stopped responding"));
            Assert.That(finished, Is.Zero);
            Assert.That(stopped, Is.Zero);
            Assert.That(harness.Factory.Sockets, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task Close1000IsFinishedNotAborted()
        {
            var harness = new GameHarness();
            await harness.ConnectAsync();
            GameEndedEvent? finished = null;
            var aborted = 0;
            harness.Client.Finished += e => finished = e;
            harness.Client.Aborted += _ => aborted++;

            harness.Socket.ServerClose(1000);
            harness.Poll();
            harness.Advance(60000);

            Assert.That(finished!.Value.Code, Is.EqualTo(1000));
            Assert.That(aborted, Is.Zero);
            Assert.That(harness.Factory.Sockets, Has.Count.EqualTo(1));
        }

        [TestCase(1011)]
        [TestCase(4002)]
        [TestCase(1001)]
        public async Task ReconnectsAndCanSendAgain(int code)
        {
            var harness = new GameHarness();
            await harness.ConnectAsync();
            var connects = 0;
            harness.Client.Connected += () => connects++;

            harness.Socket.ServerClose(code);
            harness.Poll();
            harness.Advance(500);
            harness.Socket.ServerOpen();
            harness.Poll();

            Assert.That(connects, Is.EqualTo(1));
            Assert.That(harness.Client.State, Is.EqualTo(GatewayClientState.Connected));
            Assert.DoesNotThrow(() => harness.Client.Send(Json.Object().Set("type", "move").Build()));
        }

        [TestCase(4000)]
        [TestCase(4003)]
        [TestCase(4004)]
        public async Task TerminalCodesStopWithoutAbortedOrFinished(int code)
        {
            var harness = new GameHarness();
            await harness.ConnectAsync();
            StoppedEvent? stopped = null;
            var ended = 0;
            harness.Client.Stopped += e => stopped = e;
            harness.Client.Aborted += _ => ended++;
            harness.Client.Finished += _ => ended++;

            harness.Socket.ServerClose(code);
            harness.Poll();
            harness.Advance(60000);

            Assert.That(stopped!.Value.Code, Is.EqualTo(code));
            Assert.That(ended, Is.Zero);
            Assert.That(harness.Factory.Sockets, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task ANonJsonFrameSurfacesAsAProtocolError()
        {
            var harness = new GameHarness();
            await harness.ConnectAsync();
            var errors = new List<string>();
            harness.Client.ProtocolError += e => errors.Add(e.Message);

            harness.Socket.ServerSendRaw("<html>");
            harness.Poll();

            // The reason is a code and an offset, so a field engineer can tell "the
            // proxy returned an error page" from "a string was truncated".
            Assert.That(errors, Is.EqualTo(new[] { "frame is not JSON: ExpectedValue at 0" }));
        }

        [Test]
        public async Task AMalformedFrameNeverPutsItsContentInTheProtocolError()
        {
            // A ProtocolError message reaches whatever log writer the consumer
            // installed, and a frame body is a payload or a credential echo.
            const string Token = "S3CRET-TOKEN";
            var harness = new GameHarness();
            await harness.ConnectAsync();
            var errors = new List<string>();
            harness.Client.ProtocolError += e => errors.Add(e.Message);

            harness.Socket.ServerSendRaw("{\"authorization\":\"bearer " + Token + "\"");
            harness.Poll();

            // Positive control: Does.Not.Contain passes against an empty list too.
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0], Does.Contain("ExpectedCommaOrEnd"));
            Assert.That(errors[0], Does.Not.Contain(Token));
            Assert.That(errors[0], Does.Not.Contain("bearer"));
            Assert.That(errors[0], Does.Not.Contain("authorization"));
        }

        [Test]
        public async Task NeverWritesTheTokenToTheLog()
        {
            var harness = new GameHarness();
            await harness.ConnectAsync();
            harness.Socket.ServerSend(Json.Object().Set("type", "error").Set("code", "unavailable").Set("message", "x").Build());
            harness.Socket.ServerClose(4001);
            harness.Poll();

            Assert.That(harness.Log.Text, Does.Contain("game connected"));
            Assert.That(harness.Log.Text, Does.Contain("gateway connection stopped"));
            Assert.That(harness.Log.Text, Does.Not.Contain(GameHarness.Token));
            Assert.That(harness.Log.Text, Does.Not.Contain("secret-token"));
        }

        [Test]
        public void FailsClearlyWhenNoWebSocketFactoryWorks()
        {
            var harness = new GameHarness(o => o.WebSocketFactory = new ThrowingFactory());
            StoppedEvent? stopped = null;
            harness.Client.Stopped += e => stopped = e;

            var pending = harness.Client.ConnectAsync();

            Assert.That(pending.IsFaulted, Is.True);
            Assert.That(stopped!.Value.Reason, Does.StartWith("cannot open WebSocket: "));
            Assert.That(stopped.Value.Reason, Does.Contain("WebGL"));
        }

        private sealed class ThrowingFactory : IWebSocketFactory
        {
            public IWebSocket Create(WebSocketCreateContext context)
                => throw new PlatformNotSupportedException(
                    "ClientWebSocket does not work on WebGL; set WebSocketFactory on the client options.");
        }

        /// <remarks>
        /// The q bridge forwards the actor's message with SendRaw, verbatim, so it has
        /// no vocabulary at all. Requiring a JSON object with a string `type` — the
        /// lobby's rule — dropped a run's own data and the game just appeared to hang.
        /// </remarks>
        [Test]
        public async Task PassesThroughAFrameThatIsNotAnObjectWithATypeAtAll()
        {
            var harness = new GameHarness();
            await harness.ConnectAsync();
            var frames = new List<JsonValue>();
            var errors = new List<ProtocolErrorEvent>();
            harness.Client.Frame += f => frames.Add(f);
            harness.Client.ProtocolError += e => errors.Add(e);

            harness.Socket.ServerSendRaw("[1,2,3]");
            harness.Socket.ServerSendRaw("{\"result\":\"win\",\"score\":10}");
            harness.Socket.ServerSendRaw("\"just a string\"");
            harness.Socket.ServerSendRaw("42");
            harness.Poll();

            Assert.That(errors, Is.Empty);
            Assert.That(frames, Has.Count.EqualTo(4));
            Assert.That(frames[0].Kind, Is.EqualTo(JsonKind.Array));
            Assert.That(frames[1].GetString("result"), Is.EqualTo("win"));
            Assert.That(frames[2].Kind, Is.EqualTo(JsonKind.String));
            Assert.That(frames[3].Kind, Is.EqualTo(JsonKind.Number));

            // Positive control: malformed JSON is still a protocol error.
            harness.Socket.ServerSendRaw("{oops");
            harness.Poll();

            Assert.That(errors, Has.Count.EqualTo(1));
        }

        /// <remarks>
        /// The gateway's ErrorFrame marshals `message` with omitempty, so requiring it
        /// handed a refusal to the game as its own data and let the client keep
        /// sending into a rising `bad` counter — 50 of which close the socket with
        /// 4003.
        /// </remarks>
        [Test]
        public async Task AGatewayRefusalWithNoMessageIsStillARefusal()
        {
            var harness = new GameHarness();
            await harness.ConnectAsync();
            var frames = new JsonValue?[] { null };
            var errors = new List<ErrorFrame>();
            harness.Client.Frame += f => frames[0] = f;
            harness.Client.Refused += e => errors.Add(e);

            harness.Socket.ServerSend(Json.Object().Set("type", "error").Set("code", "rate_limited").Build());
            harness.Poll();

            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0].Code, Is.EqualTo("rate_limited"));
            Assert.That(frames[0], Is.Null);

            // Positive control: a numeric `code` is still the game's own frame, since
            // the string code is the whole discriminator.
            harness.Socket.ServerSend(Json.Object().Set("type", "error").Set("code", 5d).Build());
            harness.Poll();

            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(frames[0], Is.Not.Null);
        }

        /// <remarks>
        /// A q channel has no hello, so the dungeon client turns the socket's open
        /// into its public Connected — and the documented pattern is to send the first
        /// frame from that handler. It used to be raised before MarkReady, so it threw
        /// "cannot send in state Connecting".
        /// </remarks>
        [Test]
        public void TheFirstFrameCanBeSentFromTheConnectedHandler()
        {
            var harness = new GameHarness();
            var states = new List<GatewayClientState>();
            Exception? caught = null;
            harness.Client.Connected += () =>
            {
                states.Add(harness.Client.State);
                try
                {
                    harness.Client.Send(Json.Object().Set("type", "join").Build());
                }
                catch (Exception error)
                {
                    caught = error;
                }
            };

            harness.Client.ConnectAsync();
            harness.Socket.ServerOpen();
            harness.Poll();

            Assert.That(caught, Is.Null);
            Assert.That(states, Is.EqualTo(new[] { GatewayClientState.Connected }));
            Assert.That(harness.Socket.Sent, Has.Count.EqualTo(1));
        }
    }
}
