using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Yingyeothon.Codec;

namespace Yingyeothon.Gamebase.Client.Tests
{
    [TestFixture]
    public class LobbySenderTests
    {
        [Test]
        public async Task SendsTypedLobbyFrames()
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync();
            var client = harness.Client;

            client.Pos("town", 1, 2, "n");
            client.Pos("town", 3.5, 4.5);
            client.Say(SayScope.Zone, "hi");
            client.Say(SayScope.User, "psst", "bob");
            client.Event(SayScope.Party, "loot", Json.Object().Set("id", 7d).Build());
            client.Party.Create();
            client.Party.Invite("bob");
            client.Party.Accept("pty_1");
            client.Party.Decline("pty_2");
            client.Party.Leave();
            client.Party.List();
            client.Ping();

            var sent = harness.Socket.Sent;
            Assert.That(Json.Stringify(sent[0]), Is.EqualTo("{\"type\":\"pos\",\"zone\":\"town\",\"x\":1,\"y\":2,\"dir\":\"n\"}"));

            // An omitted dir must be absent, not null: the gateway reads this with a
            // Go struct where the two are different frames.
            Assert.That(Json.Stringify(sent[1]), Is.EqualTo("{\"type\":\"pos\",\"zone\":\"town\",\"x\":3.5,\"y\":4.5}"));
            Assert.That(Json.Stringify(sent[2]), Is.EqualTo("{\"type\":\"say\",\"scope\":\"zone\",\"text\":\"hi\"}"));
            Assert.That(Json.Stringify(sent[3]), Is.EqualTo("{\"type\":\"say\",\"scope\":\"user\",\"to\":\"bob\",\"text\":\"psst\"}"));
            Assert.That(Json.Stringify(sent[4]), Is.EqualTo("{\"type\":\"event\",\"scope\":\"party\",\"name\":\"loot\",\"payload\":{\"id\":7}}"));
            Assert.That(Json.Stringify(sent[5]), Is.EqualTo("{\"type\":\"party.create\"}"));
            Assert.That(Json.Stringify(sent[6]), Is.EqualTo("{\"type\":\"party.invite\",\"userId\":\"bob\"}"));
            Assert.That(Json.Stringify(sent[7]), Is.EqualTo("{\"type\":\"party.accept\",\"partyId\":\"pty_1\"}"));
            Assert.That(Json.Stringify(sent[8]), Is.EqualTo("{\"type\":\"party.decline\",\"partyId\":\"pty_2\"}"));
            Assert.That(Json.Stringify(sent[9]), Is.EqualTo("{\"type\":\"party.leave\"}"));
            Assert.That(Json.Stringify(sent[10]), Is.EqualTo("{\"type\":\"party.list\"}"));
            Assert.That(Json.Stringify(sent[11]), Is.EqualTo("{\"type\":\"ping\"}"));
        }

        [Test]
        public async Task AnEventWithoutAPayloadOmitsTheField()
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync();

            harness.Client.Event(SayScope.Zone, "wave", null);

            Assert.That(Json.Stringify(harness.Socket.Sent[0]),
                Is.EqualTo("{\"type\":\"event\",\"scope\":\"zone\",\"name\":\"wave\"}"));
        }

        [Test]
        public async Task RefusesLocallyWhatTheChannelsCapabilitiesDisable()
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync(Frames.Hello(
                say: new[] { "zone" },
                pos: false,
                party: false,
                channelEvent: false));
            var client = harness.Client;

            Assert.Throws<InvalidOperationException>(() => client.Pos("town", 1, 1));
            Assert.Throws<InvalidOperationException>(() => client.Say(SayScope.Party, "hi"));
            Assert.Throws<InvalidOperationException>(() => client.Event(SayScope.Zone, "x", null));
            Assert.Throws<InvalidOperationException>(() => client.Party.Create());
            Assert.Throws<InvalidOperationException>(() => client.Party.Invite("bob"));
            Assert.Throws<InvalidOperationException>(() => client.Party.Accept("p"));
            Assert.Throws<InvalidOperationException>(() => client.Party.Decline("p"));
            Assert.Throws<InvalidOperationException>(() => client.Party.Leave());
            Assert.Throws<InvalidOperationException>(() => client.Party.List());

            client.Say(SayScope.Zone, "allowed");

            Assert.That(harness.Socket.Sent, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task AllowsEverythingWhenTheCapabilityObjectIsEmpty()
        {
            var harness = new LobbyHarness();
            var hello = Json.Object()
                .Set("type", "hello")
                .Set("userId", "alice")
                .Set("connectionId", "c")
                .Set("tick", 200d)
                .Set("mapUrl", "https://cdn/map.json")
                .Set("zone", "town")
                .Set("capabilities", Json.Object().Build())
                .Build();
            await harness.ConnectAsync(hello);

            Assert.DoesNotThrow(() => harness.Client.Pos("town", 1, 1));
            Assert.DoesNotThrow(() => harness.Client.Say(SayScope.User, "hi", "bob"));
            Assert.DoesNotThrow(() => harness.Client.Event(SayScope.Party, "x", null));
            Assert.DoesNotThrow(() => harness.Client.Party.Create());
        }

        [Test]
        public async Task ANullSayListMeansUnrestricted()
        {
            // The gateway's Go `[]string` has no omitempty, so a channel that
            // restricts nothing marshals `"say": null`. Folding that into an empty
            // list would refuse every chat message the channel actually allows.
            var harness = new LobbyHarness();
            var hello = Json.Object()
                .Set("type", "hello")
                .Set("userId", "alice")
                .Set("connectionId", "c")
                .Set("tick", 200d)
                .Set("mapUrl", "https://cdn/map.json")
                .Set("zone", "town")
                .Set("capabilities", Json.Object().Set("pos", true).SetNull("say").Build())
                .Build();
            await harness.ConnectAsync(hello);

            Assert.That(harness.Client.Capabilities!.Say, Is.Null);
            Assert.DoesNotThrow(() => harness.Client.Say(SayScope.Party, "hi"));
        }

        [Test]
        public async Task RefusesADirLongerThanTheGatewayAccepts()
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync();

            Assert.DoesNotThrow(() => harness.Client.Pos("town", 1, 1, "0123456789abcdef"));

            // Sixteen bytes, not sixteen characters: a Hangul syllable is three.
            Assert.Throws<ArgumentException>(() => harness.Client.Pos("town", 1, 1, "0123456789abcdefg"));
            Assert.Throws<ArgumentException>(() => harness.Client.Pos("town", 1, 1, "북북북북북북"));
            Assert.DoesNotThrow(() => harness.Client.Pos("town", 1, 1, "북북북북북"));
        }

        [Test]
        public async Task SendIsRefusedOutsideTheConnectedState()
        {
            var harness = new LobbyHarness();
            var client = harness.Client;

            Assert.Throws<InvalidOperationException>(() => client.Ping());

            await harness.ConnectAsync();
            harness.Socket.ServerClose(4002);
            harness.Poll();

            Assert.That(client.State, Is.EqualTo(GatewayClientState.Reconnecting));
            Assert.Throws<InvalidOperationException>(() => client.Ping());

            harness.Advance(500);
            harness.Socket.ServerClose(4000);
            harness.Poll();

            Assert.That(client.State, Is.EqualTo(GatewayClientState.Closed));
            Assert.Throws<InvalidOperationException>(() => client.Ping());
        }

        [Test]
        public async Task SendPassesAnArbitraryFrameThrough()
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync();

            harness.Client.Send(Json.Object().Set("type", "custom").Set("n", 1d).Build());

            Assert.That(Json.Stringify(harness.Socket.Sent[0]), Is.EqualTo("{\"type\":\"custom\",\"n\":1}"));
        }
    }
}
