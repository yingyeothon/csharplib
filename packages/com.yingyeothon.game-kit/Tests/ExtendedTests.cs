using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Yingyeothon.Codec;
using Yingyeothon.Gamebase.Client;
using Yingyeothon.KvStore;
using Yingyeothon.Platform;
using Yingyeothon.Push;

namespace Yingyeothon.GameKit.Tests
{
    [TestFixture]
    public class ExtendedTests
    {
        private sealed class Clock : IClock { public double NowMillis { get; set; } }
        private sealed class Topics : IKitPushTopics
        { internal int Sub, Unsub; public Task SubscribeAsync(string topic, CancellationToken ct) { Sub++; return Task.CompletedTask; } public Task UnsubscribeAsync(string topic, CancellationToken ct) { Unsub++; return Task.CompletedTask; } }
        private sealed class Server : IHttpTransport
        {
            internal readonly List<HttpCall> Calls = new List<HttpCall>(); internal int Version = 1; internal bool Missing = false, Deleted, ServerOnly, Cooldown, Online = true;
            internal string Ticket = "waiting"; internal int Reads; internal bool SecondDeleteMissing;
            public Task<HttpReply> SendAsync(HttpCall call, CancellationToken ct)
            {
                Calls.Add(call); var path = call.Url.AbsolutePath; var headers = new List<KeyValuePair<string, string>>(); var status = 200; string body = "{}";
                if (path == "/time")
                {
                    body = "{\"epochMs\":1700000000000}";
                }
                else if (path.Contains("/push/"))
                {
                    status = 204;
                }
                else if (path == "/kv/mail")
                {
                    body = "{\"id\":\"kv_fixture\",\"name\":\"mail\",\"readScope\":\"user\",\"writeScope\":\"project\",\"encrypted\":false,\"maxEntries\":100,\"maxEntriesPerOwner\":3}";
                }
                else if (path.StartsWith("/kv/", StringComparison.Ordinal))
                {
                    if (call.Method == "DELETE") { status = Deleted || SecondDeleteMissing ? 404 : 204; Deleted = true; }
                    else if (call.Method == "PUT")
                    {
                        status = 204;
                    }
                    else if (path.EndsWith("/entries", StringComparison.Ordinal))
                    {
                        body = "{\"entries\":[{\"key\":\"letter\",\"version\":1,\"bytes\":1,\"updatedAt\":100,\"from\":\"server\",\"valueText\":\"42\"}],\"nextCursor\":null}";
                    }
                    else { Reads++; if (Missing || Deleted) { status = 404; } else { body = "42"; headers.Add(new KeyValuePair<string, string>("etag", Version.ToString())); headers.Add(new KeyValuePair<string, string>("x-kv-from", "server")); headers.Add(new KeyValuePair<string, string>("x-kv-at", "100")); } }
                }
                else if (path == "/lb/weekly")
                {
                    body = "{\"submit\":\"" + (ServerOnly ? "server" : "owner") + "\",\"periods\":[{\"period\":\"weekly\"}]}";
                }
                else if (path == "/lb/weekly/scores/me")
                {
                    body = call.Method == "PUT" ? "{\"submitted\":7,\"periods\":[{\"period\":\"weekly\",\"score\":7,\"periodEndsAt\":1700003600}]}" : "{\"rank\":1,\"score\":7,\"total\":1}";
                }
                else if (path == "/lb/weekly/top")
                {
                    body = "{\"entries\":[],\"total\":1}";
                }
                else if (path == "/social/friends")
                {
                    body = "{\"friends\":[{\"owner\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"since\":1}]}";
                }
                else if (path == "/presence")
                {
                    body = "{\"type\":\"presence\",\"users\":[{\"userId\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"online\":" + (Online ? "true" : "false") + "}]}";
                }
                else if (path == "/social/requests")
                {
                    body = "{\"incoming\":[{\"owner\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"since\":1}],\"outgoing\":[]}";
                }
                else if (path.StartsWith("/social/", StringComparison.Ordinal) && call.Method != "GET" && call.Method != "PUT")
                {
                    status = 204;
                }
                else if (path.StartsWith("/m/", StringComparison.Ordinal))
                {
                    if (Cooldown && call.Method == "POST") { Cooldown = false; status = 429; body = "{\"error\":{\"code\":\"rate_limited\",\"details\":{\"reason\":\"cooldown\",\"retryAfter\":2}}}"; }
                    else if (call.Method == "DELETE")
                    {
                        status = 204;
                    }
                    else if (Ticket == "proposed")
                    {
                        body = "{\"state\":\"proposed\",\"matchId\":\"m\",\"partial\":false,\"accepted\":true,\"acceptBy\":1700000200}";
                    }
                    else if (Ticket == "confirmed")
                    {
                        body = "{\"state\":\"confirmed\",\"matchId\":\"m\",\"partial\":false,\"result\":null,\"members\":[{\"userId\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}]}";
                    }
                    else
                    {
                        body = "{\"state\":\"waiting\",\"position\":1,\"waited\":0}";
                    }
                }
                if (status == 204 || status == 404)
                {
                    body = "";
                }

                return Task.FromResult(new HttpReply(status, headers, body));
            }
        }
        private static GameKitOptions Options(Server server, Clock clock, Topics? topics = null)
        { return new GameKitOptions { Config = Json.Object().Set("auth", Json.Object().Set("url", "https://auth.example.test").Set("channelId", "auth_fixture").Build()).Set("state", Json.Object().Set("url", "https://state.example.test").Build()).Set("gateway", Json.Object().Set("url", "wss://gateway.example.test").Set("lobbyChannelId", "lobby_fixture").Build()).Set("collections", Json.Object().Set("content", "content").Set("save", "save").Set("mail", "mail").Build()).Set("boards", Json.Object().Set("weekly", "weekly").Build()).Set("match", Json.Object().Set("mode", "deferred").Set("apiBase", "https://match.example.test").Set("channelId", "match_fixture").Build()).Set("push", Json.Object().Set("apiBase", "https://state.example.test").Set("channelId", "push_fixture").Set("topic", "topic_fixture").Build()).Build(), Transport = server, Clock = clock, PushTopics = topics }; }
        private static readonly string Token = RoomTurnsTests.Token(new string('a', 32));
        private static void Done(IGameKit kit, Task task) { for (var i = 0; i < 1000 && !task.IsCompleted; i++) { kit.Poll(); } Assert.That(task.IsCompleted, Is.True); task.GetAwaiter().GetResult(); }
        [Test]
        public void BoardUsesRealSubmissionThenRanksAndClampsAround()
        {
            var server = new Server(); using var kit = GameKit.Create(Options(server, new Clock())); Done(kit, kit.Session.FromTokenAsync(Token)); var board = kit.Board("weekly"); Done(kit, board.InfoAsync()); var submit = board.SubmitAsync(new PlatformScoreOptions { Score = 7 }); Done(kit, submit); Assert.That(submit.Result.GetArrayOrEmpty("periods")[0].GetNumber("rank"), Is.EqualTo(1));
            Done(kit, board.MeAsync("weekly")); Done(kit, board.TopAsync(new PlatformBoardPageOptions { Period = "weekly" })); Done(kit, board.AroundAsync("weekly", 3)); Assert.That(server.Calls.Last().Url.Query, Does.Contain("offset=0"));
            server.ServerOnly = true; var fail = board.SubmitAsync(new PlatformScoreOptions { Score = 7 }); for (var i = 0; i < 10 && !fail.IsCompleted; i++)
            {
                kit.Poll();
            }

            Assert.That(((KitException)fail.Exception!.InnerException!).Code, Is.EqualTo("server_only"));
        }
        [Test]
        public void MailListsStampsClaimsOnceAndDeduplicatesArrivals()
        {
            var server = new Server(); var clock = new Clock(); using var kit = GameKit.Create(Options(server, clock)); Done(kit, kit.Session.FromTokenAsync(Token)); var arrivals = 0; kit.Mail.Arrived += _ => arrivals++;
            var list = kit.Mail.ListAsync(); Done(kit, list); Assert.That(list.Result[0].From, Is.EqualTo("server")); kit.Poll(); clock.NowMillis = 30000; for (var i = 0; i < 10; i++)
            {
                kit.Poll();
            }

            Assert.That(arrivals, Is.EqualTo(1));
            Done(kit, kit.Mail.ReadAsync("letter")); Done(kit, kit.Mail.SendAsync(new KitMailSendOptions { To = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", Value = JsonValue.Of(7) }));
            var claim = kit.Mail.ClaimAsync("letter"); Done(kit, claim); Assert.That(claim.Result.From, Is.EqualTo("server")); var second = kit.Mail.ClaimAsync("letter"); for (var i = 0; i < 10 && !second.IsCompleted; i++)
            {
                kit.Poll();
            }

            Assert.That(((KitException)second.Exception!.InnerException!).Code, Is.EqualTo("already_claimed")); Done(kit, kit.Mail.RemoveAsync("letter"));
        }
        [Test]
        public void ConditionalDeleteMissingDoesNotReturnAClaimedLetter()
        {
            var server = new Server { SecondDeleteMissing = true }; using var kit = GameKit.Create(Options(server, new Clock())); Done(kit, kit.Session.FromTokenAsync(Token)); var task = kit.Mail.ClaimAsync("letter"); for (var i = 0; i < 10 && !task.IsCompleted; i++)
            {
                kit.Poll();
            }

            Assert.That(((KitException)task.Exception!.InnerException!).Code, Is.EqualTo("already_claimed"));
        }
        [Test]
        public void FriendsJoinPresenceAndRequestsNotifyOnce()
        {
            var server = new Server(); var clock = new Clock(); using var kit = GameKit.Create(Options(server, clock)); Done(kit, kit.Session.FromTokenAsync(Token)); var list = kit.Friends.ListAsync(); Done(kit, list); Assert.That(list.Result.GetArrayOrEmpty("friends")[0].GetBool("online"), Is.True);
            var requested = 0; kit.Friends.Requested += _ => requested++; Done(kit, kit.Friends.RequestsAsync()); clock.NowMillis = 30000; for (var i = 0; i < 10; i++)
            {
                kit.Poll();
            }

            Assert.That(requested, Is.EqualTo(1)); var id = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            Done(kit, kit.Friends.RequestAsync(id)); Done(kit, kit.Friends.AcceptAsync(id)); Done(kit, kit.Friends.DeclineAsync(id)); Done(kit, kit.Friends.RemoveAsync(id)); Done(kit, kit.Friends.BlockAsync(id)); Done(kit, kit.Friends.UnblockAsync(id)); Done(kit, kit.Friends.ProfileAsync()); Done(kit, kit.Friends.ProfilesAsync(new[] { id })); Done(kit, kit.Friends.SetProfileAsync(new PlatformProfileOptions { DisplayName = "Player" }));
        }
        [Test]
        public void PushWakesDeferredStatusAndSignoutDeletesTokenAndTopic()
        {
            var server = new Server(); var clock = new Clock(); var topics = new Topics(); using var kit = GameKit.Create(Options(server, clock, topics)); Done(kit, kit.Session.FromTokenAsync(Token)); Done(kit, kit.Push.RegisterAsync(new PushRegisterOptions { Token = "device-fixture", Project = "game-fixture" })); Assert.That(topics.Sub, Is.EqualTo(1));
            var states = 0; kit.Matchmaking.StateChanged += _ => states++; Done(kit, kit.Matchmaking.SubmitAsync()); server.Ticket = "proposed"; kit.Push.Receive(new Dictionary<string, string> { ["channelId"] = "match_fixture", ["matchId"] = "m", ["state"] = "proposed" }); for (var i = 0; i < 10; i++)
            {
                kit.Poll();
            }

            Assert.That(states, Is.EqualTo(2)); Done(kit, kit.Matchmaking.AcceptAsync()); server.Ticket = "confirmed"; Done(kit, kit.Matchmaking.StatusAsync()); Done(kit, kit.Matchmaking.DeclineAsync()); Done(kit, kit.Matchmaking.CancelAsync());
            var campaigns = 0; kit.Push.MessageReceived += _ => campaigns++; kit.Push.Receive(new Dictionary<string, string> { ["campaign"] = "fixture" }); kit.Poll(); Assert.That(campaigns, Is.EqualTo(1)); Done(kit, kit.Session.SignOutAsync()); Assert.That(topics.Unsub, Is.EqualTo(1)); Assert.That(server.Calls.Any(x => x.Method == "DELETE" && x.Url.AbsolutePath.StartsWith("/push/", StringComparison.Ordinal)), Is.True);
        }
        [Test]
        public void BackgroundStopsPollingAndCooldownWaitsForMonotonicDeadline()
        {
            var server = new Server { Cooldown = true }; var clock = new Clock(); using var kit = GameKit.Create(Options(server, clock)); Done(kit, kit.Session.FromTokenAsync(Token)); var submit = kit.Matchmaking.SubmitAsync(); for (var i = 0; i < 10; i++)
            {
                kit.Poll();
            }

            Assert.That(submit.IsCompleted, Is.False); clock.NowMillis = 1999; kit.Poll(); Assert.That(submit.IsCompleted, Is.False); clock.NowMillis = 2000; Done(kit, submit);
            kit.SetForeground(false); kit.Poll(); var count = server.Calls.Count; clock.NowMillis = 100000; for (var i = 0; i < 10; i++)
            {
                kit.Poll();
            }

            Assert.That(server.Calls.Count, Is.EqualTo(count)); kit.SetForeground(true); for (var i = 0; i < 10; i++)
            {
                kit.Poll();
            }

            Assert.That(server.Calls.Count, Is.GreaterThan(count));
        }
        [Test]
        public void ContentCachingAndSubscriptionUseVersions()
        {
            var server = new Server(); var clock = new Clock(); using var kit = GameKit.Create(Options(server, clock)); Done(kit, kit.Session.FromTokenAsync(Token)); var notifications = 0; using var subscription = kit.Content.Subscribe("k", _ => notifications++);
            var first = kit.Content.GetAsync("k"); Done(kit, first); Assert.That(first.Result!.AsNumber(), Is.EqualTo(42)); var second = kit.Content.GetAsync("k"); Done(kit, second); Assert.That(notifications, Is.EqualTo(1)); server.Version++; clock.NowMillis = 60000; for (var i = 0; i < 10; i++)
            {
                kit.Poll();
            }

            Assert.That(notifications, Is.EqualTo(2)); Done(kit, kit.Content.ListAsync());
        }
    }
}
