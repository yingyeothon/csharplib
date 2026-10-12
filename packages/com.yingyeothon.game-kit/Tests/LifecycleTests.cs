using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Yingyeothon.Codec;
using Yingyeothon.KvStore;

namespace Yingyeothon.GameKit.Tests
{
    [TestFixture]
    public class LifecycleTests
    {
        private sealed class Storage : IKitTokenStorage
        {
            internal readonly TaskCompletionSource<string?> Loaded = new TaskCompletionSource<string?>();
            internal int Cleared;
            public Task<string?> LoadAsync(CancellationToken ct) { return Loaded.Task; }
            public Task StoreAsync(string jwt, CancellationToken ct) { return Task.CompletedTask; }
            public Task ClearAsync(CancellationToken ct) { Cleared++; return Task.CompletedTask; }
        }
        private sealed class Login : IKitLoginAdapter
        {
            internal readonly TaskCompletionSource<Uri> Returned = new TaskCompletionSource<Uri>();
            internal Uri? Start;
            public Task<Uri> OpenAsync(Uri start, CancellationToken ct) { Start = start; return Returned.Task; }
        }
        private static GameKitOptions Options(IKitTokenStorage? storage = null, IKitLoginAdapter? login = null)
        {
            return new GameKitOptions
            {
                Config = Json.Object().Set("auth", Json.Object().Set("url", "https://auth.example.test").Set("channelId", "auth_fixture").Set("provider", "google").Build()).Set("state", Json.Object().Set("url", "https://state.example.test").Build()).Build(),
                Transport = new KitTests.Store(),
                TokenStorage = storage,
                LoginAdapter = login,
                RedirectUrl = new Uri("https://game.example.test/return")
            };
        }
        [Test]
        public void ADelayedRestoreCannotResurrectASignedOutSession()
        {
            var storage = new Storage(); using var kit = GameKit.Create(Options(storage)); var restore = kit.Session.RestoreAsync(); kit.Poll();
            var signout = kit.Session.SignOutAsync(); for (var i = 0; i < 20 && !signout.IsCompleted; i++) { kit.Poll(); }
            storage.Loaded.SetResult(RoomTurnsTests.Token("alice")); for (var i = 0; i < 20 && !restore.IsCompleted; i++) { kit.Poll(); }
            Assert.That(restore.IsFaulted, Is.True); Assert.That(kit.Session.Token, Is.Null); Assert.That(storage.Cleared, Is.EqualTo(1));
            var shutdown = kit.ShutdownAsync(); RoomTurnsTests.Pump(shutdown, kit);
        }
        [Test]
        public void LoginUsesConfigProviderAndLateReturnCannotReplaceANewerToken()
        {
            var login = new Login(); using var kit = GameKit.Create(Options(null, login)); var flow = kit.Session.SignInAsync(); kit.Poll();
            Assert.That(login.Start!.Query, Does.Contain("provider=google")); RoomTurnsTests.Pump(kit.Session.FromTokenAsync(RoomTurnsTests.Token("bob")), kit);
            var redirect = login.Start.Query.TrimStart('?').Split('&').First(x => x.StartsWith("redirect=", StringComparison.Ordinal)).Substring("redirect=".Length);
            login.Returned.SetResult(new Uri(Uri.UnescapeDataString(redirect) + "#token=fixture&userId=alice&exp=2100000000"));
            for (var i = 0; i < 20 && !flow.IsCompleted; i++) { kit.Poll(); }
            Assert.That(flow.IsFaulted, Is.True); Assert.That(kit.Session.UserId, Is.EqualTo("bob"));
        }

        [Test]
        public void QueuedWorkAfterShutdownCannotResurrectCredentialsOrOpenLogin()
        {
            var login = new Login(); using var kit = GameKit.Create(Options(null, login)); var shutdown = kit.ShutdownAsync(); var adopt = kit.Session.FromTokenAsync(RoomTurnsTests.Token("alice")); var signIn = kit.Session.SignInAsync();
            RoomTurnsTests.Pump(shutdown, kit); Assert.That(adopt.IsCanceled, Is.True); Assert.That(signIn.IsCanceled, Is.True); Assert.That(kit.Session.Token, Is.Null); Assert.That(login.Start, Is.Null);
        }
        private sealed class PushServer : IHttpTransport
        {
            internal readonly TaskCompletionSource<HttpReply> Deleted = new TaskCompletionSource<HttpReply>();
            internal bool DelayDelete; internal bool Registered;
            public Task<HttpReply> SendAsync(HttpCall call, CancellationToken ct)
            {
                if (call.Url.AbsolutePath == "/time")
                {
                    return Task.FromResult(new HttpReply(200, Array.Empty<KeyValuePair<string, string>>(), "{\"epochMs\":1700000000000}"));
                }

                if (call.Method == "DELETE" && DelayDelete)
                {
                    return CompleteDelete();
                }

                if (call.Method == "PUT")
                {
                    Registered = true;
                }

                return Task.FromResult(new HttpReply(204, Array.Empty<KeyValuePair<string, string>>(), ""));
            }
            private async Task<HttpReply> CompleteDelete() { var reply = await Deleted.Task; Registered = false; return reply; }
        }
        private sealed class Topics : IKitPushTopics
        {
            internal bool Subscribed;
            public Task SubscribeAsync(string topic, CancellationToken ct) { Subscribed = true; return Task.CompletedTask; }
            public Task UnsubscribeAsync(string topic, CancellationToken ct) { Subscribed = false; return Task.CompletedTask; }
        }
        [Test]
        public void ExplicitDeleteFinishesBeforeTheSameDeviceCanBeRegisteredAgain()
        {
            var transport = new PushServer(); var options = Options(); options.Transport = transport;
            var builder = Json.Object(); foreach (var member in options.Config.AsObject()) { builder.Set(member.Key, member.Value); }
            builder.Set("push", Json.Object().Set("apiBase", "https://state.example.test").Set("channelId", "push_fixture").Set("topic", "topic_fixture").Build()); options.Config = builder.Build();
            using var kit = GameKit.Create(options); RoomTurnsTests.Pump(kit.Session.FromTokenAsync(RoomTurnsTests.Token("alice")), kit);
            var device = new Push.PushRegisterOptions { Token = "same-device", Project = "game" }; RoomTurnsTests.Pump(kit.Push.RegisterAsync(device), kit);
            transport.DelayDelete = true; var deleting = kit.Push.UnregisterAsync(); kit.Poll(); var registering = kit.Push.RegisterAsync(device);
            for (var i = 0; i < 10; i++) { kit.Poll(); }
            Assert.That(deleting.IsCompleted, Is.False); Assert.That(registering.IsCompleted, Is.False);
            transport.Deleted.SetResult(new HttpReply(204, Array.Empty<KeyValuePair<string, string>>(), "")); RoomTurnsTests.Pump(registering, kit);
            Assert.That(deleting.IsCompleted, Is.True); Assert.That(transport.Registered, Is.True);
        }
        [TestCase(false)]
        [TestCase(true)]
        public void OldLogoutAndDetachedDeleteCannotEraseANewSessionOrTopic(bool renewal)
        {
            var transport = new PushServer(); var topics = new Topics(); var options = Options(); options.Transport = transport; options.PushTopics = topics;
            var builder = Json.Object(); foreach (var member in options.Config.AsObject()) { builder.Set(member.Key, member.Value); }
            builder.Set("push", Json.Object().Set("apiBase", "https://state.example.test").Set("channelId", "push_fixture").Set("topic", "topic_fixture").Build()); options.Config = builder.Build();
            using var kit = GameKit.Create(options); RoomTurnsTests.Pump(kit.Session.FromTokenAsync(RoomTurnsTests.Token("alice")), kit); RoomTurnsTests.Pump(kit.Push.RegisterAsync(new Push.PushRegisterOptions { Token = "device-old", Project = "game" }), kit);
            transport.DelayDelete = true; var oldLogout = kit.Session.SignOutAsync(); kit.Poll(); RoomTurnsTests.Pump(kit.Session.FromTokenAsync(RoomTurnsTests.Token("intermediate")), kit); kit.Poll(); RoomTurnsTests.Pump(kit.Session.FromTokenAsync(RoomTurnsTests.Token(renewal ? "alice" : "bob")), kit); var registration = kit.Push.RegisterAsync(new Push.PushRegisterOptions { Token = renewal ? "device-old" : "device-new", Project = "game" }); for (var i = 0; i < 5; i++) { kit.Poll(); }
            Assert.That(registration.IsCompleted, Is.False);
            transport.Deleted.SetResult(new HttpReply(204, Array.Empty<KeyValuePair<string, string>>(), "")); RoomTurnsTests.Pump(registration, kit); for (var i = 0; i < 30; i++) { kit.Poll(); }
            Assert.That(kit.Session.UserId, Is.EqualTo(renewal ? "alice" : "bob")); Assert.That(topics.Subscribed, Is.True); Assert.That(transport.Registered, Is.True); Assert.That(oldLogout.IsCompleted, Is.True);
        }
    }
}
