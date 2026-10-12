using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Yingyeothon.Codec;
using Yingyeothon.KvStore;

namespace Yingyeothon.Platform.Tests
{
    [TestFixture]
    public class PlatformTests
    {
        private sealed class Transport : IHttpTransport
        {
            internal HttpCall? Call;
            internal HttpReply Reply = new HttpReply(200, Array.Empty<KeyValuePair<string, string>>(), "{}");
            internal TaskCompletionSource<HttpReply>? Pending;
            internal bool Throw;
            public Task<HttpReply> SendAsync(HttpCall call, CancellationToken ct)
            { Call = call; if (Throw) { throw new InvalidOperationException("secret"); } return Pending?.Task ?? Task.FromResult(Reply); }
        }
        private static PlatformClientOptions Options(Transport t) => new PlatformClientOptions { BaseUrl = "https://example.test/base", Token = "fixture", Transport = t };
        [Test]
        public void ResponseAndTypedParserSettleOnlyInsidePoll()
        {
            var t = new Transport { Reply = new HttpReply(200, Array.Empty<KeyValuePair<string, string>>(), "{\"epochMs\":1700000000123}") };
            using var c = PlatformClient.Create(Options(t));
            var task = c.TimeAsync();
            Assert.That(task.IsCompleted, Is.False);
            Assert.That(t.Call!.Headers.Count, Is.EqualTo(0));
            c.Poll(); Assert.That(task.Result, Is.EqualTo(1700000000123L));
        }
        [Test]
        public void ForeignResponsesWaitForTheOwnerAndCancellationDropsLateAnswers()
        {
            var t = new Transport { Pending = new TaskCompletionSource<HttpReply>() };
            using var c = PlatformHttpClient.Create(Options(t)); using var cancel = new CancellationTokenSource();
            var task = c.RequestAsync(new PlatformRequestOptions(), cancel.Token);
            cancel.Cancel(); Assert.That(task.IsCompleted, Is.False);
            c.Poll(); Assert.That(task.IsCanceled, Is.True);
            t.Pending.SetResult(t.Reply); c.Poll(); Assert.That(task.IsCanceled, Is.True);
        }
        [Test]
        public void TimeoutAndDisposalArePumped()
        {
            var now = 0d; var t = new Transport { Pending = new TaskCompletionSource<HttpReply>() }; var o = Options(t); o.NowMillis = () => now;
            using var c = PlatformHttpClient.Create(o);
            var task = c.RequestAsync(new PlatformRequestOptions()); now = 15000; c.Poll();
            Assert.That(((PlatformException)task.Exception!.InnerException!).Code, Is.EqualTo("timeout"));
            task = c.RequestAsync(new PlatformRequestOptions()); c.Dispose(); Assert.That(task.IsCompleted, Is.False); c.Poll(); Assert.That(task.IsCanceled, Is.True);
            Assert.That(() => c.RequestAsync(new PlatformRequestOptions()), Throws.InstanceOf<ObjectDisposedException>());
        }
        [TestCase(302, "", "http", null)]
        [TestCase(429, "{\"error\":{\"code\":\"rate_limited\",\"details\":{\"reason\":\"cooldown\",\"retryAfter\":3}}}", "rate_limited", "cooldown")]
        [TestCase(403, "{\"error\":{\"code\":\"secret body\",\"details\":{\"reason\":\"secret body\"}}}", "http", null)]
        public void RefusalsAreSafe(int status, string body, string code, string? reason)
        {
            var t = new Transport { Reply = new HttpReply(status, Array.Empty<KeyValuePair<string, string>>(), body) };
            using var c = PlatformHttpClient.Create(Options(t)); var task = c.RequestAsync(new PlatformRequestOptions()); c.Poll();
            var e = (PlatformException)task.Exception!.InnerException!;
            Assert.That(e.Code, Is.EqualTo(code)); Assert.That(e.Reason, Is.EqualTo(reason));
            Assert.That(e.Message, Does.Not.Contain("secret")); Assert.That(e.InnerException, Is.Null);
            if (reason == "cooldown")
            {
                Assert.That(e.RetryAfterMillis, Is.EqualTo(3000));
            }
        }
        [TestCase("/../private")]
        [TestCase("/%2e%2e/private")]
        [TestCase("//evil.test")]
        [TestCase("/path#fragment")]
        [TestCase("/path\\evil")]
        public void PathsCannotEscapeTheOrigin(string path)
        {
            using var c = PlatformHttpClient.Create(Options(new Transport()));
            Assert.That(() => c.RequestAsync(new PlatformRequestOptions { Path = path }), Throws.ArgumentException);
        }
        [Test]
        public void ReentrantPollIsRefusedAndFaultsAreQueued()
        {
            var t = new Transport(); using var c = PlatformHttpClient.Create(Options(t));
            var reentrant = false;
            var task = c.RequestAsync(new PlatformRequestOptions(), v => { reentrant = Assert.Throws<InvalidOperationException>(c.Poll) != null; return v; });
            c.Poll(); Assert.That(reentrant, Is.True); Assert.That(task.IsCompletedSuccessfully, Is.True);
            t.Throw = true; task = c.RequestAsync(new PlatformRequestOptions()); c.Poll();
            Assert.That(((PlatformException)task.Exception!.InnerException!).Code, Is.EqualTo("network"));
        }
        [Test]
        public void SocialAndBoardRoutesUseTheirWireShapes()
        {
            var t = new Transport(); using var c = PlatformClient.Create(Options(t));
            var id = new string('a', 32);
            Task[] tasks = {
                c.Social.SetProfileAsync(new PlatformProfileOptions { DisplayName = "Player" }), c.Social.ProfilesAsync(new[] { id }),
                c.Social.FriendsAsync(), c.Social.RequestsAsync(), c.Social.RequestAsync(id), c.Social.AcceptAsync(id),
                c.Social.DeclineAsync(id), c.Social.RemoveAsync(id), c.Social.BlockAsync(id), c.Social.UnblockAsync(id),
                c.Board("board").InfoAsync(), c.Board("board").SubmitAsync(new PlatformScoreOptions { Score = 123 }),
                c.Board("board").TopAsync(new PlatformBoardPageOptions()), c.Board("board").MeAsync("weekly"),
                c.PresenceAsync(new PlatformPresenceOptions { ChannelId = "lobby_fixture", UserIds = new[] { id } }) };
            c.Poll(); foreach (var task in tasks)
            {
                Assert.That(task.IsCompletedSuccessfully, Is.True);
            }

            Assert.That(t.Call!.Url.AbsoluteUri, Does.Contain("/presence?channel=lobby_fixture&users="));
            Assert.That(t.Call.Headers[0].Value, Is.EqualTo("Bearer fixture"));
            t.Reply = new HttpReply(404, Array.Empty<KeyValuePair<string, string>>(), "");
            var missing = c.Social.ProfileAsync(); c.Poll(); Assert.That(missing.Result, Is.Null);
            missing = c.Board("board").MeAsync("daily"); c.Poll(); Assert.That(missing.Result, Is.Null);
        }
        [Test]
        public void LocalInputsAreValidatedBeforeSending()
        {
            var t = new Transport(); using var c = PlatformClient.Create(Options(t));
            Assert.That(() => c.Board(""), Throws.ArgumentException);
            Assert.That(() => c.Board("b").SubmitAsync(new PlatformScoreOptions { Score = long.MaxValue }), Throws.ArgumentException);
            Assert.That(() => c.Board("b").TopAsync(new PlatformBoardPageOptions { Offset = -1 }), Throws.ArgumentException);
            Assert.That(() => c.Board("b").MeAsync("bad"), Throws.ArgumentException);
            Assert.That(() => c.Social.RequestAsync("bad"), Throws.ArgumentException);
            Assert.That(() => c.Social.ProfilesAsync(Array.Empty<string>()), Throws.ArgumentException);
            Assert.That(() => c.Social.SetProfileAsync(new PlatformProfileOptions()), Throws.ArgumentException);
            Assert.That(t.Call, Is.Null);
        }
        [TestCase("{}")]
        [TestCase("null")]
        [TestCase("{\"epochMs\":-1}")]
        [TestCase("{\"epochMs\":1.5}")]
        [TestCase("bad")]
        public void InvalidTimeFailsOnPoll(string body)
        {
            var t = new Transport { Reply = new HttpReply(200, Array.Empty<KeyValuePair<string, string>>(), body) };
            using var c = PlatformClient.Create(Options(t)); var task = c.TimeAsync(); c.Poll();
            Assert.That(task.IsFaulted, Is.True);
        }
    }
}
