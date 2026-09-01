using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Yingyeothon.Codec;

namespace Yingyeothon.Gamebase.Client.Tests
{
    [TestFixture]
    public class LobbyFrameTests
    {
        [Test]
        public async Task BuildsThePeerMapAndFiltersSelf()
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync();
            var seen = new List<string>();
            harness.Client.Snapshot += f => seen.Add("snapshot:" + f.Zone + ":" + f.Peers.Count);
            harness.Client.PeerEnter += p => seen.Add("enter:" + p.UserId);
            harness.Client.PeerLeave += id => seen.Add("leave:" + id);
            harness.Client.PeerMove += ps => seen.Add("move:" + string.Join(",", ps.Select(p => p.UserId)));

            harness.Socket.ServerSend(Frames.Snapshot("town", Frames.Peer("bob", 1, 1), Frames.Peer("alice", 0, 0)));
            harness.Socket.ServerSend(Frames.Enter("town", "carol", 2, 2));
            harness.Socket.ServerSend(Frames.Pos("town", Frames.Peer("alice", 9, 9), Frames.Peer("bob", 3, 3)));
            harness.Socket.ServerSend(Frames.Leave("town", "bob"));
            harness.Poll();

            // The Snapshot event carries the frame as sent, self included; the
            // filtered view is the peer map.
            Assert.That(seen, Is.EqualTo(new[] { "snapshot:town:2", "enter:carol", "move:bob", "leave:bob" }));
            Assert.That(harness.Client.Peers.All().Select(p => p.UserId), Is.EqualTo(new[] { "carol" }));
            Assert.That(harness.Client.Peers.Get("alice"), Is.Null);
        }

        [Test]
        public async Task RoutesSayEventPartyPongAndErrorToTypedEvents()
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync();
            var seen = new List<string>();
            harness.Client.Said += f => seen.Add("say:" + f.From + ":" + f.Scope + ":" + f.Text + ":" + (f.To ?? "-"));
            harness.Client.EventReceived += f => seen.Add("event:" + f.Name + ":" + Json.Stringify(f.Payload!));
            harness.Client.PartyChanged += f => seen.Add("party:" + (f.PartyId ?? "-"));
            harness.Client.PartyInvited += f => seen.Add("invite:" + f.PartyId + ":" + f.From);
            harness.Client.PartyDeclined += f => seen.Add("declined:" + f.PartyId + ":" + f.UserId);
            harness.Client.Pong += () => seen.Add("pong");
            harness.Client.Refused += f => seen.Add("error:" + f.Code + ":" + f.Message);
            harness.Client.ProtocolError += e => seen.Add("protocol:" + e.Message);

            harness.Socket.ServerSend(Json.Object().Set("type", "say").Set("from", "bob").Set("scope", "zone").Set("text", "hi").Build());
            harness.Socket.ServerSend(Json.Object().Set("type", "event").Set("from", "bob").Set("scope", "party")
                .Set("name", "loot").Set("payload", Json.Object().Set("id", 7d).Build()).Build());
            harness.Socket.ServerSend(Json.Object().Set("type", "party").Set("partyId", "pty_1")
                .Set("members", Json.Array()).Build());
            harness.Socket.ServerSend(Json.Object().Set("type", "party.invite").Set("partyId", "pty_2").Set("from", "carol").Build());
            harness.Socket.ServerSend(Json.Object().Set("type", "party.declined").Set("partyId", "pty_2").Set("userId", "dave").Build());
            harness.Socket.ServerSend(Json.Object().Set("type", "pong").Build());
            harness.Socket.ServerSend(Json.Object().Set("type", "error").Set("code", "rate_limited").Set("message", "slow down").Build());
            harness.Socket.ServerSend(Json.Object().Set("type", "who_knows").Build());
            harness.Poll();

            Assert.That(seen, Is.EqualTo(new[]
            {
                "say:bob:zone:hi:-",
                "event:loot:{\"id\":7}",
                "party:pty_1",
                "invite:pty_2:carol",
                "declined:pty_2:dave",
                "pong",
                "error:rate_limited:slow down",
                "protocol:unknown frame type who_knows",
            }));
        }

        [Test]
        public async Task FillsTheRosterFieldsTheGatewayOmitsWhenEmpty()
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync();
            PartyFrame? roster = null;
            harness.Client.PartyChanged += f => roster = f;

            // Go omitempty: leaderId, invited and max are simply absent when empty,
            // which is always the case right after a leave or dissolve.
            harness.Socket.ServerSend(Json.Object().Set("type", "party").Set("partyId", "").Build());
            harness.Poll();

            Assert.That(roster!.LeaderId, Is.EqualTo(string.Empty));
            Assert.That(roster.Members, Is.Empty);
            Assert.That(roster.Invited, Is.Empty);
            Assert.That(roster.Max, Is.Zero);
            Assert.That(roster.PartyId, Is.Null);
            Assert.That(harness.Client.PartyId, Is.Null);

            harness.Socket.ServerSend(Json.Object()
                .Set("type", "party")
                .Set("partyId", "pty_1")
                .Set("leaderId", "alice")
                .Set("members", Json.Array(
                    Json.Object().Set("userId", "alice").Set("online", true).Build(),
                    Json.Object().Set("userId", "bob").Set("online", false).Build()))
                .Set("invited", Json.ArrayOfStrings(new[] { "carol" }))
                .Set("max", 4d)
                .Build());
            harness.Poll();

            Assert.That(roster!.PartyId, Is.EqualTo("pty_1"));
            Assert.That(roster.LeaderId, Is.EqualTo("alice"));
            Assert.That(roster.Members.Select(m => m.UserId + ":" + m.Online), Is.EqualTo(new[] { "alice:True", "bob:False" }));
            Assert.That(roster.Invited, Is.EqualTo(new[] { "carol" }));
            Assert.That(roster.Max, Is.EqualTo(4));
            Assert.That(harness.Client.PartyId, Is.EqualTo("pty_1"));
            Assert.That(harness.Client.Roster, Is.SameAs(roster));
        }

        [Test]
        public async Task TakesPartyIdFromHelloAndNormalisesAnEmptyOne()
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync(Frames.Hello(partyId: "pty_from_hello"));

            Assert.That(harness.Client.PartyId, Is.EqualTo("pty_from_hello"));

            var other = new LobbyHarness();
            await other.ConnectAsync(Frames.Hello(partyId: ""));

            Assert.That(other.Client.PartyId, Is.Null);
        }

        [Test]
        public async Task TheFrameEventCarriesEveryFrameAfterHelloAlreadyNormalised()
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync();
            var types = new List<string>();
            PartyFrame? party = null;
            harness.Client.Frame += f =>
            {
                types.Add(f.Type);
                if (f is PartyFrame p)
                {
                    party = p;
                }
            };

            harness.Socket.ServerSend(Json.Object().Set("type", "pong").Build());
            harness.Socket.ServerSend(Json.Object().Set("type", "party").Set("partyId", "pty_1").Build());
            harness.Poll();

            Assert.That(types, Is.EqualTo(new[] { "pong", "party" }));
            Assert.That(party!.Invited, Is.Empty);
        }

        [Test]
        public async Task AnOpaqueEventPayloadReachesTheHandlerUnchanged()
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync();
            JsonValue? payload = null;
            harness.Client.EventReceived += f => payload = f.Payload;
            var original = Json.Parse("{\"deep\":{\"list\":[1,\"two\",null,true]}}");

            harness.Socket.ServerSend(Json.Object().Set("type", "event").Set("from", "bob")
                .Set("scope", "zone").Set("name", "x").Set("payload", original).Build());
            harness.Poll();

            Assert.That(payload, Is.EqualTo(original));
        }

        [Test]
        public async Task AnEventWithNoPayloadReportsNullRatherThanAJsonNull()
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync();
            EventBroadcastFrame? received = null;
            harness.Client.EventReceived += f => received = f;

            harness.Socket.ServerSend(Json.Object().Set("type", "event").Set("from", "bob")
                .Set("scope", "zone").Set("name", "x").Build());
            harness.Poll();

            Assert.That(received!.Payload, Is.Null);
        }

        [Test]
        public async Task MalformedFramesSurfaceAsProtocolErrors()
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync();
            var errors = new List<string>();
            harness.Client.ProtocolError += e => errors.Add(e.Message);

            harness.Socket.ServerSendRaw("not json");
            harness.Socket.ServerSendRaw("[1,2]");
            harness.Socket.ServerSendRaw("{\"type\":5}");
            harness.Socket.ServerSendBinary();
            harness.Poll();

            Assert.That(errors, Is.EqualTo(new[]
            {
                "frame is not JSON",
                "frame has no string type",
                "frame has no string type",
                "non-text frame",
            }));
        }

        [Test]
        public async Task UnsubscribingStopsDelivery()
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync();
            var calls = 0;
            System.Action handler = () => calls++;
            harness.Client.Pong += handler;

            harness.Socket.ServerSend(Json.Object().Set("type", "pong").Build());
            harness.Poll();
            harness.Client.Pong -= handler;
            harness.Socket.ServerSend(Json.Object().Set("type", "pong").Build());
            harness.Poll();

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public async Task RaisesToTheHandlerSetAsItWasWhenTheRaiseStarted()
        {
            var harness = new LobbyHarness();
            await harness.ConnectAsync();
            var seen = new List<string>();
            harness.Client.Pong += () =>
            {
                seen.Add("first");
                harness.Client.Pong += () => seen.Add("late");
            };

            harness.Socket.ServerSend(Json.Object().Set("type", "pong").Build());
            harness.Poll();

            Assert.That(seen, Is.EqualTo(new[] { "first" }));

            harness.Socket.ServerSend(Json.Object().Set("type", "pong").Build());
            harness.Poll();

            Assert.That(seen, Is.EqualTo(new[] { "first", "first", "late" }));
        }
    }
}
