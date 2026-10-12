using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Yingyeothon.Codec;
using Yingyeothon.KvStore;
using Yingyeothon.Platform;

namespace Yingyeothon.Push.Tests
{
    [TestFixture]
    public class PushTests
    {
        private sealed class Transport : IHttpTransport
        {
            internal HttpCall? Call; internal int Status = 204;
            public Task<HttpReply> SendAsync(HttpCall call, CancellationToken ct) { Call = call; return Task.FromResult(new HttpReply(Status, Array.Empty<KeyValuePair<string, string>>(), "")); }
        }
        private static IPushClient Client(Transport t) => PushClient.Create(new PushClientOptions { ChannelId = "push_fixture", Http = new PlatformClientOptions { BaseUrl = "https://example.test", Token = "fixture", Transport = t } });
        [Test]
        public void RegisterRefreshAndDeleteUseTheDeviceTokenAndProject()
        {
            var t = new Transport(); using var c = Client(t);
            var task = c.RegisterAsync(new PushRegisterOptions { Token = "device-fixture", Project = "game-fixture" }); Assert.That(task.IsCompleted, Is.False); c.Poll(); Assert.That(task.IsCompletedSuccessfully, Is.True);
            Assert.That(t.Call!.Method, Is.EqualTo("PUT")); Assert.That(Json.Parse(t.Call.Body!).GetString("project"), Is.EqualTo("game-fixture"));
            Assert.That(t.Call.Url.AbsolutePath, Is.EqualTo("/push/push_fixture/token"));
            task = c.UnregisterAsync("device-fixture"); c.Poll(); Assert.That(task.IsCompletedSuccessfully, Is.True); Assert.That(t.Call.Method, Is.EqualTo("DELETE"));
            t.Status = 409; task = c.RegisterAsync(new PushRegisterOptions { Token = "device-fixture", Project = "game-fixture" }); c.Poll(); Assert.That(((PlatformException)task.Exception!.InnerException!).Status, Is.EqualTo(409));
        }
        [TestCase("")]
        [TestCase("with space")]
        [TestCase("line\nbreak")]
        public void InvalidTokensNeverSend(string token)
        {
            var t = new Transport(); using var c = Client(t);
            Assert.That(() => c.RegisterAsync(new PushRegisterOptions { Token = token, Project = "game" }), Throws.ArgumentException);
            Assert.That(() => c.UnregisterAsync(token), Throws.ArgumentException); Assert.That(t.Call, Is.Null);
        }
        [TestCase("")]
        [TestCase("UPPER")]
        [TestCase("bad/project")]
        public void ProjectIsRequiredAndBounded(string project)
        {
            using var c = Client(new Transport()); Assert.That(() => c.RegisterAsync(new PushRegisterOptions { Token = "fixture", Project = project }), Throws.ArgumentException);
        }
        [TestCase("proposed", PushMessageKind.Match)]
        [TestCase("confirmed", PushMessageKind.Match)]
        [TestCase("campaign", PushMessageKind.Message)]
        public void ClassificationIsOnlyAWakeup(string state, PushMessageKind kind)
            => Assert.That(PushClient.Classify(new Dictionary<string, string> { ["channelId"] = "match_fixture", ["matchId"] = "", ["state"] = state }), Is.EqualTo(kind));
        [Test]
        public void BroadcastDoesNotBecomeAMatch() => Assert.That(PushClient.Classify(new Dictionary<string, string> { ["body"] = "hello" }), Is.EqualTo(PushMessageKind.Message));
    }
}
