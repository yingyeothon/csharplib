using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Yingyeothon.Codec;
using Yingyeothon.Gamebase.Client;
using Yingyeothon.KvStore;

namespace Yingyeothon.GameKit.Tests
{
    [TestFixture]
    public class RoomTurnsTests
    {
        internal sealed class Clock : IClock { public double NowMillis { get; set; } }
        private sealed class Store : IHttpTransport
        { public Task<HttpReply> SendAsync(HttpCall call, CancellationToken ct) => Task.FromResult(new HttpReply(200, Array.Empty<KeyValuePair<string, string>>(), "{\"epochMs\":1700000000000}")); }
        internal sealed class Gateway : IWebSocketFactory
        {
            internal readonly Dictionary<string, Socket> Sockets = new Dictionary<string, Socket>(); internal readonly List<string> Members = new List<string>(); internal readonly List<JsonValue> Sent = new List<JsonValue>(); internal string? Host; internal string? Id; internal readonly List<string> Invites = new List<string>();
            public IWebSocket Create(WebSocketCreateContext c)
            {
                if (c.Url.Contains("channel=match_fixture"))
                {
                    return new MatchSocket(c);
                }

                var p = c.SubProtocols[1].Split('.')[1]; p += new string('=', (4 - p.Length % 4) % 4); var user = Json.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(p))).GetString("sub")!; var socket = new Socket(this, c, user); Sockets[user] = socket; return socket;
            }
            private sealed class MatchSocket : IWebSocket
            {
                private readonly WebSocketCreateContext _c;
                internal MatchSocket(WebSocketCreateContext c) { _c = c; }
                public void Start() { _c.Sink.Post(SocketEvent.Opened(this, "bearer")); _c.Sink.Post(SocketEvent.Message(this, "{\"type\":\"matched\",\"matchId\":\"m\",\"partial\":false,\"result\":null,\"members\":[{\"userId\":\"alice\"},{\"userId\":\"bob\"},{\"userId\":\"carol\"},{\"userId\":\"dave\"}]}")); }
                public void SendText(string text) { }
                public void Close(int code, string reason) { }
                public void Dispose() { }
            }
            internal void Roster()
            {
                foreach (var s in Sockets.Values)
                {
                    s.Post(Json.Object().Set("type", "party").Set("partyId", Members.Contains(s.User) ? Id : null).Set("leaderId", Members.Contains(s.User) ? Host : "").Set("invited", JsonValue.Array(Invites.Select(JsonValue.Of))).Set("members", JsonValue.Array(Members.Contains(s.User) ? Members.Select(id => Json.Object().Set("userId", id).Set("online", true).Build()) : Array.Empty<JsonValue>())).Build());
                }
            }
            internal sealed class Socket : IWebSocket
            {
                private readonly Gateway _g; private readonly WebSocketCreateContext _c; internal readonly string User;
                internal Socket(Gateway g, WebSocketCreateContext c, string user) { _g = g; _c = c; User = user; }
                internal void Post(JsonValue value) => _c.Sink.Post(SocketEvent.Message(this, Json.Stringify(value)));
                public void Start()
                {
                    _c.Sink.Post(SocketEvent.Opened(this, "bearer")); Post(Json.Object().Set("type", "hello").Set("userId", User).Set("connectionId", "fixture:connection").Set("tick", 200).Set("mapUrl", "https://example.test/map").Set("partyId", _g.Members.Contains(User) ? _g.Id : null).Set("capabilities", Json.Object().Set("party", true).Set("event", true).Build()).Build()); if (_g.Members.Contains(User))
                    {
                        _g.Roster();
                    }
                }
                public void SendText(string text)
                {
                    var f = Json.Parse(text); _g.Sent.Add(f);
                    switch (f.GetString("type"))
                    {
                        case "party.create": _g.Id = "party_fixture"; _g.Host = User; _g.Members.Clear(); _g.Members.Add(User); _g.Roster(); break;
                        case "party.invite": _g.Invites.Add(f.GetString("userId")!); _g.Roster(); if (_g.Sockets.TryGetValue(f.GetString("userId")!, out var invitee)) { invitee.Post(Json.Object().Set("type", "party.invite").Set("partyId", _g.Id).Set("from", User).Build()); } break;
                        case "party.accept": if (!_g.Members.Contains(User)) { _g.Members.Add(User); } _g.Roster(); break;
                        case "party.leave": _g.Members.Remove(User); _g.Host = _g.Members.FirstOrDefault(); _g.Roster(); break;
                        case "party.list": _g.Roster(); break;
                        case "event": foreach (var target in _g.Sockets.Values.ToArray()) { if (target.User != User && (f.GetString("scope") == "user" ? f.GetString("to") == target.User : _g.Members.Contains(target.User))) { target.Post(Json.Object().Set("type", "event").Set("scope", f.GetString("scope")).Set("from", User).Set("to", f.GetString("to")).Set("name", f.GetString("name")).Set("payload", f.TryGetMember("payload", out var payload) ? payload : JsonValue.Null).Build()); } } break;
                    }
                }
                public void Close(int code, string reason) => _c.Sink.Post(SocketEvent.Closed(this, code, reason)); public void Dispose() { }
            }
        }
        internal static string Token(string user) => "fixture." + Convert.ToBase64String(Encoding.UTF8.GetBytes(Json.Stringify(Json.Object().Set("sub", user).Set("exp", 1700003600d).Build()))).TrimEnd('=') + ".signature";
        internal static IGameKit Kit(Gateway gateway, Clock clock, IHttpTransport? store = null, bool callback = false) => GameKit.Create(new GameKitOptions { Config = Json.Object().Set("match", Json.Object().Set("mode", "live").Set("url", "wss://match.example.test").Set("channelId", "match_fixture").Build()).Set("auth", Json.Object().Set("url", "https://auth.example.test").Set("channelId", "auth_fixture").Build()).Set("collections", Json.Object().Set("save", "save").Build()).Set("state", Json.Object().Set("url", "https://state.example.test").Build()).Set("gateway", Json.Object().Set("url", "wss://gateway.example.test").Set("lobbyChannelId", "lobby_fixture").Build()).Build(), Transport = store ?? new Store(), WebSocketFactory = gateway, Clock = clock, MatchCallback = callback });
        internal static void Pump(Task task, params IGameKit[] kits)
        { for (var i = 0; i < 1000 && !task.IsCompleted; i++) { foreach (var kit in kits) { kit.Poll(); } } Assert.That(task.IsCompleted, Is.True, "task stalled"); task.GetAwaiter().GetResult(); }
        internal static KitTurnsOptions Turns() => new KitTurnsOptions { Initial = JsonValue.Of(0), Apply = (state, move, by) => JsonValue.Of(state.AsNumber() + move.AsNumber()), Players = new[] { "alice", "bob" } };
        [Test]
        public void RosterHostSuccessionAndStateStayInsidePoll()
        {
            var g = new Gateway(); var clock = new Clock(); using var a = Kit(g, clock); using var b = Kit(g, clock); Pump(a.Session.FromTokenAsync(Token("alice")), a, b); Pump(b.Session.FromTokenAsync(Token("bob")), a, b);
            Pump(a.Room.CreateAsync(), a, b); Pump(b.Room.CreateAsync(), a, b); // Opens bob's lobby; restore Alice as host for the fixture.
            g.Host = "alice"; g.Members.Clear(); g.Members.Add("alice"); g.Members.Add("bob"); g.Roster(); a.Poll(); b.Poll();
            Pump(a.Room.PublishAsync(JsonValue.Of(42)), a, b); b.Poll(); Assert.That(b.Room.LastKnown!.AsNumber(), Is.EqualTo(42));
            var became = 0; b.Room.BecomeHost += state => { became++; Assert.That(state!.AsNumber(), Is.EqualTo(42)); };
            var leave = a.Room.LeaveAsync(); Assert.That(became, Is.Zero); Pump(leave, a, b); b.Poll(); Assert.That(became, Is.EqualTo(1)); Assert.That(b.Room.IsHost, Is.True);
            var tooBig = b.Room.SendAsync("game", JsonValue.Of(new string('x', 8193))); for (var i = 0; i < 10 && !tooBig.IsCompleted; i++)
            {
                b.Poll();
            }

            Assert.That(((KitException)tooBig.Exception!.InnerException!).Code, Is.EqualTo("payload_too_large"));
        }
        [Test]
        public void TurnsReplayTamperFreezeAndOutsiderIsolation()
        {
            var g = new Gateway(); var clock = new Clock(); using var a = Kit(g, clock); using var b = Kit(g, clock); Pump(a.Session.FromTokenAsync(Token("alice")), a, b); Pump(b.Session.FromTokenAsync(Token("bob")), a, b); Pump(a.Room.CreateAsync(), a, b); Pump(b.Room.CreateAsync(), a, b);
            g.Host = "alice"; g.Members.Clear(); g.Members.AddRange(new[] { "alice", "bob" }); g.Roster(); a.Poll(); b.Poll();
            var ga = a.Turns.StartAsync(Turns()); Pump(ga, a, b); var gb = b.Turns.StartAsync(Turns()); for (var i = 0; i < 1000 && !gb.IsCompleted; i++) { clock.NowMillis += 200; a.Poll(); b.Poll(); }
            Pump(gb, a, b);
            Pump(ga.Result.SubmitAsync(JsonValue.Of(2)), a, b); b.Poll(); Assert.That(gb.Result.State.AsNumber(), Is.EqualTo(2));
            Pump(gb.Result.SubmitAsync(JsonValue.Of(3)), a, b); a.Poll(); Assert.That(ga.Result.State.AsNumber(), Is.EqualTo(5));
            var game = ga.Result.GameId;
            JsonValue Envelope(JsonValue data, int seq) => Json.Object().Set("v", 1).Set("roomId", "party_fixture").Set("senderSession", "injected").Set("seq", seq).Set("data", data).Build();
            JsonValue Event(string from, string name, JsonValue data, int seq) => Json.Object().Set("type", "event").Set("scope", "user").Set("from", from).Set("name", name).Set("payload", Envelope(data, seq)).Build();
            var tamper = Json.Object().Set("gameId", game).Set("index", 2).Set("by", "alice").Set("move", 100).Set("hash", "bad").Build();
            g.Sockets["bob"].Post(Event("outsider", "kit.turn", tamper, 1)); b.Poll(); Assert.That(gb.Result.Frozen, Is.False);
            g.Sockets["bob"].Post(Event("alice", "kit.turn", tamper, 2)); b.Poll(); Assert.That(gb.Result.Frozen, Is.True); Assert.That(gb.Result.Index, Is.EqualTo(2));
            var validHash = KitJson.Hash(JsonValue.ArrayOf(JsonValue.Of(g.Sent.Last(x => x.GetString("name") == "kit.turn").GetString("unused") ?? "bad"), JsonValue.Of(2), JsonValue.Of("alice"), JsonValue.Of(1)));
            g.Sockets["bob"].Post(Event("alice", "kit.turn", Json.Object().Set("gameId", game).Set("index", 2).Set("by", "alice").Set("move", 1).Set("hash", validHash).Build(), 3)); b.Poll(); Assert.That(gb.Result.Index, Is.EqualTo(2));
        }

        [TestCase("kit.turn", "{\"index\":0,\"by\":\"bob\",\"move\":1,\"hash\":\"bad\"}")]
        [TestCase("kit.turn", "{\"index\":0,\"by\":\"alice\",\"move\":\"\\ud800\",\"hash\":\"bad\"}")]
        [TestCase("kit.turn", "{\"index\":0,\"by\":\"alice\",\"hash\":\"bad\"}")]
        [TestCase("kit.turns.start", "{\"players\":[7],\"initialHash\":\"bad\"}")]
        [TestCase("kit.turns.finish", "{\"count\":2,\"finalHash\":\"bad\",\"result\":7}")]
        public void MalformedAuthenticatedTurnsFreezeWithoutEscapingPoll(string name, string fields)
        {
            var g = new Gateway(); var clock = new Clock(); using var a = Kit(g, clock); Pump(a.Session.FromTokenAsync(Token("alice")), a); Pump(a.Room.CreateAsync(), a);
            var started = a.Turns.StartAsync(Turns()); Pump(started, a); var game = started.Result;
            var b = Json.Object(); foreach (var m in Json.Parse(fields).AsObject())
            {
                b.Set(m.Key, m.Value);
            }

            b.Set("gameId", game.GameId);
            var envelope = Json.Object().Set("v", 1).Set("roomId", a.Room.Id).Set("senderSession", "fixture").Set("seq", 1).Set("data", b.Build()).Build();
            g.Sockets["alice"].Post(Json.Object().Set("type", "event").Set("scope", "user").Set("from", "alice").Set("name", name).Set("payload", envelope).Build());
            Assert.DoesNotThrow(a.Poll); Assert.That(game.Frozen, Is.True); Assert.That(game.Index, Is.Zero);
        }
        [Test]
        public void RoomPrivateEventsRejectReservedNamesAndResetStateBetweenRooms()
        {
            var g = new Gateway(); var clock = new Clock(); using var a = Kit(g, clock); Pump(a.Session.FromTokenAsync(Token("alice")), a); Pump(a.Room.ConnectAsync(), a); Pump(a.Room.CreateAsync(), a);
            Pump(a.Room.PublishAsync(JsonValue.Of(9)), a); Pump(a.Room.SendAsync("move", JsonValue.Of(1)), a); Pump(a.Room.WhisperAsync("alice", "move", JsonValue.Of(2)), a);
            var bad = a.Room.SendAsync("kit.bad", JsonValue.Null); for (var i = 0; i < 10 && !bad.IsCompleted; i++)
            {
                a.Poll();
            }

            Assert.That(bad.Exception!.InnerException, Is.TypeOf<ArgumentException>());
            Pump(a.Room.DeclineAsync("other"), a); Pump(a.Room.InviteAsync("absent"), a); Pump(a.Room.LeaveAsync(), a); Assert.That(a.Room.LastKnown, Is.Null);
        }
        [Test]
        public void PendingResyncSettlesOnSignoutAndOldGameHandlesAreRetired()
        {
            var g = new Gateway(); var clock = new Clock(); using var a = Kit(g, clock); Pump(a.Session.FromTokenAsync(Token("alice")), a); Pump(a.Room.CreateAsync(), a); var started = a.Turns.StartAsync(Turns()); Pump(started, a);
            var pending = started.Result.ResyncAsync(); a.Poll(); Pump(a.Session.SignOutAsync(), a); for (var i = 0; i < 10 && !pending.IsCompleted; i++)
            {
                a.Poll();
            }

            Assert.That(pending.IsCompleted, Is.True);
            var old = started.Result.SubmitAsync(JsonValue.Of(1)); for (var i = 0; i < 10 && !old.IsCompleted; i++)
            {
                a.Poll();
            }

            Assert.That(old.IsFaulted, Is.True);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void LiveMatchFormsOneRoomAndReportsMissingMember(bool missing, bool callback)
        {
            var gateway = new Gateway(); var clock = new Clock(); var users = new[] { "alice", "bob", "carol", "dave" }; var kits = users.Select(user => Kit(gateway, clock, null, callback)).ToArray();
            try
            {
                for (var i = 0; i < kits.Length; i++) { Pump(kits[i].Session.FromTokenAsync(Token(users[i])), kits); Pump(kits[i].Room.ConnectAsync(), kits); }
                var finds = kits.Take(missing ? 3 : 4).Select(k => k.Matchmaking.FindAsync()).ToArray(); var all = Task.WhenAll(finds);
                for (var step = 0; step < 400 && !all.IsCompleted; step++) { foreach (var kit in kits) { kit.Poll(); } clock.NowMillis += 100; }
                Pump(all, kits); foreach (var found in finds)
                {
                    Assert.That(found.Result.Partial, Is.EqualTo(missing));
                }

                if (callback)
                {
                    Assert.That(kits.All(k => k.Room.Id == null), Is.True);
                }
                else
                {
                    Assert.That(kits.Take(missing ? 3 : 4).All(k => k.Room.Id == "party_fixture"), Is.True);
                }
            }
            finally { foreach (var kit in kits) { kit.Dispose(); kit.Poll(); } }
        }
        [TestCase("{\"count\":-1,\"chunks\":1,\"finalHash\":\"bad\"}")]
        [TestCase("{\"count\":0.5,\"chunks\":1,\"finalHash\":\"bad\"}")]
        [TestCase("{\"count\":1,\"chunks\":-1,\"finalHash\":\"bad\"}")]
        [TestCase("{\"count\":1,\"chunks\":1}")]
        [TestCase("{\"count\":0,\"chunks\":0,\"finalHash\":\"bad\"}")]
        public void MalformedResyncManifestIsRejectedBeforeStateReplacement(string fields)
        {
            var gateway = new Gateway(); var clock = new Clock(); using var a = Kit(gateway, clock); using var b = Kit(gateway, clock); Pump(a.Session.FromTokenAsync(Token("alice")), a, b); Pump(b.Session.FromTokenAsync(Token("bob")), a, b); Pump(a.Room.CreateAsync(), a, b); Pump(b.Room.ConnectAsync(), a, b); Pump(b.Room.AcceptAsync("party_fixture"), a, b);
            var ga = a.Turns.StartAsync(Turns()); Pump(ga, a, b); var gb = b.Turns.StartAsync(Turns()); for (var i = 0; i < 100 && !gb.IsCompleted; i++) { clock.NowMillis += 200; a.Poll(); b.Poll(); }
            Pump(gb, a, b);
            var resync = gb.Result.ResyncAsync(); b.Poll();
            var request = gateway.Sent.Last(f => f.GetString("name") == "kit.turns.log"); var req = request.AsObject().First(m => m.Key == "payload").Value; var data = req.AsObject().First(m => m.Key == "data").Value;
            var builder = Json.Object().Set("gameId", gb.Result.GameId).Set("requestId", data.GetString("requestId")); foreach (var m in Json.Parse(fields).AsObject())
            {
                builder.Set(m.Key, m.Value);
            }

            var envelope = Json.Object().Set("v", 1).Set("roomId", "party_fixture").Set("senderSession", "malformed").Set("seq", 1).Set("data", builder.Build()).Build();
            gateway.Sockets["bob"].Post(Json.Object().Set("type", "event").Set("scope", "user").Set("from", "alice").Set("name", "kit.turns.manifest").Set("payload", envelope).Build()); Assert.DoesNotThrow(b.Poll); Assert.That(gb.Result.Frozen, Is.True); Assert.That(gb.Result.Index, Is.Zero);
        }

        [Test]
        public void ThrowingTurnObserverDoesNotFreezeOrStrandTheSubmittedTask()
        {
            var gateway = new Gateway(); var clock = new Clock(); using var kit = Kit(gateway, clock); Pump(kit.Session.FromTokenAsync(Token("alice")), kit); Pump(kit.Room.CreateAsync(), kit); var start = kit.Turns.StartAsync(Turns()); Pump(start, kit);
            start.Result.Turn += state => throw new ArgumentException("consumer observer fixture"); var submit = start.Result.SubmitAsync(JsonValue.Of(1)); Assert.Throws<ArgumentException>(kit.Poll);
            Assert.That(submit.IsCompletedSuccessfully, Is.True); Assert.That(start.Result.Frozen, Is.False); Assert.That(start.Result.Index, Is.EqualTo(1));
        }
    }
}
