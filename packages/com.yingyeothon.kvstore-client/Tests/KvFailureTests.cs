using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Yingyeothon.KvStore.Tests
{
    [TestFixture]
    public class KvFailureTests
    {
        [Test]
        public void ATransportThrowIsANetworkFailureWithTheCauseInside()
        {
            var harness = new KvHarness();
            harness.Transport.Throws = new IOException("connection reset");

            var error = Assert.ThrowsAsync<KvStoreException>(async () => await harness.Profile.Mine.GetAsync("k"));

            Assert.That(error!.Status, Is.EqualTo(0));
            Assert.That(error.Code, Is.EqualTo(KvErrorCodes.Network));
            Assert.That(error.Message, Is.EqualTo("kv network (0)"));
            Assert.That(error.InnerException, Is.InstanceOf<IOException>());
            Assert.That(error.IsConflict, Is.False);
            Assert.That(harness.Log.Text, Does.Contain("kv request failed"));
            Assert.That(harness.Log.Text, Does.Contain("\"code\":\"network\""));
            Assert.That(harness.Log.Text, Does.Not.Contain("secret-token"));
        }

        [Test]
        public async Task ATransportThatNeverAnswersIsATimeout()
        {
            var harness = new KvHarness(o => o.Timeout = TimeSpan.FromMilliseconds(1));
            harness.Transport.Hangs = true;

            var error = await Fails.WithKvStoreException(() => harness.Profile.Mine.GetAsync("k"));

            Assert.That(error!.Status, Is.EqualTo(0));
            Assert.That(error.Code, Is.EqualTo(KvErrorCodes.Timeout));
            Assert.That(error.InnerException, Is.InstanceOf<OperationCanceledException>());
            Assert.That(harness.Log.Text, Does.Contain("\"code\":\"timeout\""));
        }

        [Test]
        public void ATransportThatCancelsOnItsOwnIsATimeoutToo()
        {
            // HttpClient reports its own timeout as a TaskCanceledException on a
            // token nobody cancelled; that is still "no reply in time".
            var harness = new KvHarness();
            harness.Transport.Throws = new TaskCanceledException("the request was canceled");

            var error = Assert.ThrowsAsync<KvStoreException>(async () => await harness.Profile.Mine.GetAsync("k"));

            Assert.That(error!.Code, Is.EqualTo(KvErrorCodes.Timeout));
        }

        [Test]
        public async Task TheCallersCancellationPassesThroughUntouched()
        {
            var harness = new KvHarness();
            harness.Transport.Hangs = true;
            using var cancel = new CancellationTokenSource();

            var pending = harness.Profile.Mine.GetAsync("k", cancel.Token);
            Assert.That(pending.IsCompleted, Is.False);
            cancel.Cancel();

            await Fails.WithCancellation(() => pending);
            Assert.That(harness.Log.Text, Does.Not.Contain("kv request failed"), "a cancellation is not a failure");
        }

        [Test]
        public void AnAlreadyCancelledTokenStopsTheCallBeforeTheTransportIsAsked()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(200, "1");
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();

            Assert.That(async () => await harness.Profile.Mine.GetAsync("k", cancel.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That(harness.Transport.Calls, Is.Empty);
        }

        [Test]
        public async Task ATransportThatCancelsOnTheCallersTokenPassesItThrough()
        {
            var harness = new KvHarness();
            harness.Transport.Hangs = true;
            using var cancel = new CancellationTokenSource();
            var pending = harness.Profile.Mine.GetAsync("k", cancel.Token);

            cancel.Cancel();

            // Awaited, not read off the task: under Unity's synchronization context the
            // continuation that settles it is posted, not run inline by Cancel().
            await Fails.WithCancellation(() => pending);
            Assert.That(harness.Transport.Calls, Has.Count.EqualTo(1));
        }

        [Test]
        public void TheTokenGivenToTheTransportCanBeCancelled()
        {
            var harness = new KvHarness();
            var seen = CancellationToken.None;
            harness.Transport.Reply(404);
            var probe = new ProbeTransport(harness.Transport, token => seen = token);
            var client = KvStoreClient.Create(new KvStoreClientOptions
            {
                BaseUrl = KvHarness.BaseUrl,
                Token = KvHarness.Token,
                Transport = probe,
                Timeout = TimeSpan.FromSeconds(3),
            });

            client.Collection("profile").GetAsync("k").GetAwaiter().GetResult();

            Assert.That(seen.CanBeCanceled, Is.True, "the timeout is wired even when the caller passed no token");
            Assert.That(harness.Transport.LastCall.Timeout, Is.EqualTo(TimeSpan.FromSeconds(3)), "and the call names the same bound");
        }

        [TestCase(502, "<html>bad gateway</html>")]
        [TestCase(500, "")]
        [TestCase(400, "{\"message\":\"no error envelope\"}")]
        [TestCase(400, "{\"error\":\"a string\"}")]
        [TestCase(301, "")]
        public void AFailureWithoutTheErrorBodyIsAnHttpCode(int status, string body)
        {
            var harness = new KvHarness();
            harness.Transport.Reply(status, body);

            var error = Assert.ThrowsAsync<KvStoreException>(async () => await harness.Profile.Mine.GetAsync("k"));

            Assert.That(error!.Status, Is.EqualTo(status));
            Assert.That(error.Code, Is.EqualTo(KvErrorCodes.Http));
            Assert.That(error.Reason, Is.Null);
            Assert.That(error.HasCurrentVersion, Is.False);
        }

        /// <remarks>
        /// A code or reason is whatever the peer put there, and the message is what the
        /// game logs. Anything outside the store's own spelling is folded, so a proxy or
        /// an attacker cannot inject a megabyte of control characters into a log line.
        /// </remarks>
        [TestCase("conflict\n\u001b[31mINJECTED")]
        [TestCase("Conflict")]
        [TestCase("a-b")]
        [TestCase("")]
        [TestCase("conflict_conflict_conflict_conflict_x", Description = "33 characters")]
        public void AWireCodeOutsideTheStoresSpellingIsFoldedToHttp(string code)
        {
            var harness = new KvHarness();
            harness.Transport.Reply(400, Codec.Json.Stringify(Codec.Json.Object()
                .Set("error", Codec.Json.Object().Set("code", code)
                    .Set("details", Codec.Json.Object().Set("reason", code).Build()).Build())
                .Build()));

            var error = Assert.ThrowsAsync<KvStoreException>(async () => await harness.Profile.Mine.GetAsync("k"));

            Assert.That(error!.Code, Is.EqualTo(KvErrorCodes.Http));
            Assert.That(error.Reason, Is.Null);
            Assert.That(error.Message, Is.EqualTo("kv http (400)"));
        }

        [Test]
        public void AWireCodeOfThirtyTwoSafeCharactersIsKept()
        {
            var harness = new KvHarness();
            var code = new string('c', 32);
            harness.Transport.Error(503, code, reason: "kv_encryption_not_configured");

            var error = Assert.ThrowsAsync<KvStoreException>(async () => await harness.Profile.Mine.GetAsync("k"));

            Assert.That(error!.Code, Is.EqualTo(code));
            Assert.That(error.Reason, Is.EqualTo(KvReasons.EncryptionNotConfigured));
        }

        [TestCase(200)]
        [TestCase(502)]
        public void AReplyOverTheCodecsLimitIsRefusedUnread(int status)
        {
            var harness = new KvHarness();
            harness.Transport.Reply(status, "[" + new string('1', Codec.Json.MaxLength) + "]");

            var error = Assert.ThrowsAsync<KvStoreException>(async () => await harness.Profile.Mine.GetAsync("k"));

            Assert.That(error!.Status, Is.EqualTo(status));
            Assert.That(error.Code, Is.EqualTo(status == 200 ? KvErrorCodes.BadBody : KvErrorCodes.Http));
        }

        [Test]
        public void AnErrorBodyWithoutACodeIsAnHttpCodeToo()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(400, "{\"error\":{\"message\":\"x\",\"details\":{\"reason\":\"why\",\"current\":\"3\"}}}");

            var error = Assert.ThrowsAsync<KvStoreException>(async () => await harness.Profile.Mine.GetAsync("k"));

            Assert.That(error!.Code, Is.EqualTo(KvErrorCodes.Http));
            Assert.That(error.Reason, Is.EqualTo("why"));
            Assert.That(error.HasCurrentVersion, Is.True, "present, even though it was not a number");
            Assert.That(error.CurrentVersion, Is.Null);
        }

        [TestCase(KvReasons.EncryptionNotConfigured)]
        [TestCase(KvReasons.ValueUnreadable)]
        public void AFiveOhThreeCarriesItsReason(string reason)
        {
            var harness = new KvHarness();
            harness.Transport.Error(503, "unavailable", reason: reason);

            var error = Assert.ThrowsAsync<KvStoreException>(async () => await harness.Profile.Mine.GetAsync("k"));

            Assert.That(error!.Status, Is.EqualTo(503));
            Assert.That(error.Code, Is.EqualTo(KvErrorCodes.Unavailable));
            Assert.That(error.Reason, Is.EqualTo(reason));
        }

        [Test]
        public void AMissingCollectionOnAWriteIsNotFound()
        {
            var harness = new KvHarness();
            harness.Transport.Error(404, "not_found");

            var error = Assert.ThrowsAsync<KvStoreException>(
                async () => await harness.Profile.Mine.PutAsync("k", Codec.JsonValue.Of(1)));

            Assert.That(error!.Code, Is.EqualTo(KvErrorCodes.NotFound));
        }

        [Test]
        public void AnUnexpectedSuccessStatusOnAReadIsStillRead()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(203, "true", etag: "\"1\"");

            var value = harness.Profile.Mine.GetAsync("k").GetAwaiter().GetResult();

            Assert.That(value!.AsBool(), Is.True);
        }

        [Test]
        public void ANullReplyFromATransportIsANetworkFailure()
        {
            var client = KvStoreClient.Create(new KvStoreClientOptions
            {
                BaseUrl = KvHarness.BaseUrl,
                Token = KvHarness.Token,
                Transport = new NullReplyTransport(),
            });

            var error = Assert.ThrowsAsync<KvStoreException>(async () => await client.Collection("profile").GetAsync("k"));

            Assert.That(error!.Code, Is.EqualTo(KvErrorCodes.Network));
        }

        [Test]
        public void NothingIsLoggedBelowTheThreshold()
        {
            var log = new CapturingLogWriter();
            var harness = new KvHarness(o => o.Logger = Logger.FilteredLogger.Create(
                new Logger.FilteredLoggerOptions { Severity = Logger.LogSeverity.Warn, Writer = log }));
            harness.Transport.Reply(404);

            harness.Profile.Mine.GetAsync("k").GetAwaiter().GetResult();
            Assert.That(log.Lines, Is.Empty, "kv request is Debug");

            // Positive control: the same writer does receive the Warn line.
            harness.Transport.Throws = new IOException("down");
            Assert.ThrowsAsync<KvStoreException>(async () => await harness.Profile.Mine.GetAsync("k"));
            Assert.That(log.Lines, Has.Count.EqualTo(1));
            Assert.That(log.Text, Does.Contain("kv request failed"));
        }

        private sealed class ProbeTransport : IHttpTransport
        {
            private readonly IHttpTransport _inner;
            private readonly Action<CancellationToken> _observe;

            internal ProbeTransport(IHttpTransport inner, Action<CancellationToken> observe)
            {
                _inner = inner;
                _observe = observe;
            }

            public Task<HttpReply> SendAsync(HttpCall call, CancellationToken cancellationToken)
            {
                _observe(cancellationToken);
                return _inner.SendAsync(call, cancellationToken);
            }
        }

        private sealed class NullReplyTransport : IHttpTransport
        {
            public Task<HttpReply> SendAsync(HttpCall call, CancellationToken cancellationToken)
                => Task.FromResult<HttpReply>(null!);
        }
    }
}
