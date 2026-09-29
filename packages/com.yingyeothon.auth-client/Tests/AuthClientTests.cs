using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Yingyeothon.Codec;
using Yingyeothon.Logger;

namespace Yingyeothon.Auth.Tests
{
    [TestFixture]
    public class AuthClientTests
    {
        /// <summary>Obviously not a token; the never-logs tests search for it.</summary>
        private const string Jwt = "eyJ.secret-token.sig";

        private static IAuthClient Create(FakeAuthTransport transport, ILogger? logger = null, TimeSpan? timeout = null)
            => AuthClient.Create(new AuthClientOptions
            {
                BaseUrl = "https://auth.test/",
                ChannelId = "auth_0123456789abcdef",
                Transport = transport,
                Logger = logger,
                Timeout = timeout,
            });

        private static ILogger Capture(CapturingLogWriter log)
            => FilteredLogger.Create(new FilteredLoggerOptions { Severity = LogSeverity.Debug, Writer = log });

        // ---- config ------------------------------------------------------------

        [Test]
        public async Task FetchConfigReadsEveryField()
        {
            var transport = new FakeAuthTransport().Reply(200,
                "{\"channelId\":\"auth_1\",\"issuer\":\"yyt-auth/auth_1\",\"audience\":\"game\"," +
                "\"tokenTtlSec\":86400,\"providers\":[\"github\",7]," +
                "\"callbackUrls\":{\"github\":\"https://auth.test/c/auth_1/github/callback\",\"google\":7}," +
                "\"startUrl\":\"https://auth.test/c/auth_1/start\",\"redirectAllowlist\":[\"https://game.test/\"]," +
                "\"expiresAt\":253402300799,\"extra\":true}");

            var config = await Create(transport).FetchConfigAsync();

            Assert.That(transport.Sent[0].Method, Is.EqualTo("GET"));
            Assert.That(transport.Sent[0].Url.AbsoluteUri, Is.EqualTo("https://auth.test/c/auth_0123456789abcdef/.well-known/config"));
            Assert.That(transport.Sent[0].Headers, Is.Empty);
            Assert.That(config.ChannelId, Is.EqualTo("auth_1"));
            Assert.That(config.Issuer, Is.EqualTo("yyt-auth/auth_1"));
            Assert.That(config.Audience, Is.EqualTo("game"));
            Assert.That(config.TokenTtlSec, Is.EqualTo(86400));
            Assert.That(config.Providers.ToArray(), Is.EqualTo(new[] { "github" }));
            // An object keyed by provider — the service's Object.fromEntries — not a list.
            Assert.That(config.CallbackUrls.Count, Is.EqualTo(1));
            Assert.That(config.CallbackUrls["github"], Is.EqualTo("https://auth.test/c/auth_1/github/callback"));
            Assert.That(config.StartUrl, Is.EqualTo("https://auth.test/c/auth_1/start"));
            Assert.That(config.RedirectAllowlist.ToArray(), Is.EqualTo(new[] { "https://game.test/" }));
            Assert.That(config.ExpiresAt, Is.EqualTo(253402300799L));
            Assert.That(config.Raw.GetBool("extra"), Is.True);
        }

        [Test]
        public async Task AConfigMissingFieldsReadsThemAsEmpty()
        {
            var config = await Create(new FakeAuthTransport().Reply(200, "{}")).FetchConfigAsync();

            Assert.That(config.ChannelId, Is.Empty);
            Assert.That(config.Providers, Is.Empty);
            Assert.That(config.CallbackUrls.Count, Is.EqualTo(0));
            Assert.That(config.ExpiresAt, Is.Null);
        }

        [TestCase("not json")]
        [TestCase("[1,2]")]
        [TestCase("")]
        public void ASuccessThatIsNotAJsonObjectIsNotJson(string body)
        {
            var error = Assert.ThrowsAsync<AuthException>(() => Create(new FakeAuthTransport().Reply(200, body)).FetchConfigAsync());

            Assert.That(error!.Code, Is.EqualTo(AuthErrorCodes.NotJson));
            Assert.That(error.Status, Is.EqualTo(200));
        }

        [Test]
        public void AnOversizedBodyIsRefusedBeforeItIsParsed()
        {
            var body = "{\"x\":\"" + new string('a', MaxBodyBytes) + "\"}";

            var error = Assert.ThrowsAsync<AuthException>(() => Create(new FakeAuthTransport().Reply(200, body)).FetchConfigAsync());

            Assert.That(error!.Code, Is.EqualTo(AuthErrorCodes.NotJson));
        }

        [Test]
        public void TheCapIsInBytesNotCharacters()
        {
            // 400 Ki three-byte characters: under the cap in characters, over it in bytes.
            var body = "{\"x\":\"" + new string('\uAC00', 400 * 1024) + "\"}";

            var error = Assert.ThrowsAsync<AuthException>(() => Create(new FakeAuthTransport().Reply(200, body)).FetchConfigAsync());

            Assert.That(error!.Code, Is.EqualTo(AuthErrorCodes.NotJson));
        }

        private const int MaxBodyBytes = 1024 * 1024;

        // ---- exchange ----------------------------------------------------------

        [Test]
        public async Task ExchangeAccessTokenPostsTheProviderAndTheCredential()
        {
            var transport = new FakeAuthTransport().Reply(200, "{\"jwt\":\"" + Jwt + "\",\"userId\":\"8d0f\",\"exp\":1767225600}");

            var token = await Create(transport).ExchangeAccessTokenAsync("github", "provider-access-token");

            var sent = transport.Sent[0];
            Assert.That(sent.Method, Is.EqualTo("POST"));
            Assert.That(sent.Url.AbsoluteUri, Is.EqualTo("https://auth.test/c/auth_0123456789abcdef/token"));
            Assert.That(sent.Body, Is.EqualTo("{\"provider\":\"github\",\"accessToken\":\"provider-access-token\"}"));
            Assert.That(sent.Headers.Single().Key, Is.EqualTo("Content-Type"));
            Assert.That(token.Jwt, Is.EqualTo(Jwt));
            Assert.That(token.UserId, Is.EqualTo("8d0f"));
            Assert.That(token.ExpiresAt, Is.EqualTo(1767225600L));
        }

        [Test]
        public async Task ExchangeIdTokenSendsIdToken()
        {
            var transport = new FakeAuthTransport().Reply(200, "{\"jwt\":\"" + Jwt + "\",\"userId\":\"u\",\"exp\":1}");

            await Create(transport).ExchangeIdTokenAsync("google", "id.token.value");

            Assert.That(transport.Sent[0].Body, Is.EqualTo("{\"provider\":\"google\",\"idToken\":\"id.token.value\"}"));
        }

        [TestCase("{\"userId\":\"u\",\"exp\":1}")]
        [TestCase("{\"jwt\":\"\",\"userId\":\"u\",\"exp\":1}")]
        [TestCase("{\"jwt\":\"j\",\"exp\":1}")]
        [TestCase("{\"jwt\":\"j\",\"userId\":\"u\"}")]
        [TestCase("{\"jwt\":\"j\",\"userId\":\"u\",\"exp\":\"1\"}")]
        [TestCase("{\"jwt\":\"j\",\"userId\":\"\",\"exp\":1}")]
        [TestCase("{\"jwt\":\"j\",\"userId\":\"u\",\"exp\":1.5}")]
        [TestCase("{\"jwt\":\"j\",\"userId\":\"u\",\"exp\":-1}")]
        [TestCase("{\"jwt\":\"j\",\"userId\":\"u\",\"exp\":1e300}")]
        public void AnExchangeReplyWithoutAFieldIsMissingField(string body)
        {
            var error = Assert.ThrowsAsync<AuthException>(
                () => Create(new FakeAuthTransport().Reply(200, body)).ExchangeAccessTokenAsync("github", "t"));

            Assert.That(error!.Code, Is.EqualTo(AuthErrorCodes.MissingField));
        }

        [Test]
        public void AFailedExchangeReportsTheStatusAndNothingOfTheBody()
        {
            var transport = new FakeAuthTransport().Reply(400, "{\"error\":\"bad accessToken provider-access-token\"}");

            var error = Assert.ThrowsAsync<AuthException>(() => Create(transport).ExchangeAccessTokenAsync("github", "provider-access-token"));

            Assert.That(error!.Code, Is.EqualTo(AuthErrorCodes.Http));
            Assert.That(error.Status, Is.EqualTo(400));
            Assert.That(error.Message, Is.EqualTo("auth http (400)"));
        }

        [Test]
        public void EmptyArgumentsAreRefusedBeforeAnyRequest()
        {
            var transport = new FakeAuthTransport();
            var client = Create(transport);

            Assert.ThrowsAsync<ArgumentException>(() => client.ExchangeAccessTokenAsync("", "t"));
            Assert.ThrowsAsync<ArgumentException>(() => client.ExchangeAccessTokenAsync("github", ""));
            Assert.ThrowsAsync<ArgumentException>(() => client.ExchangeIdTokenAsync("google", null!));
            Assert.ThrowsAsync<ArgumentException>(() => client.VerifyAsync(""));
            Assert.That(transport.Sent, Is.Empty);
        }

        // ---- verify ------------------------------------------------------------

        [Test]
        public async Task VerifySendsTheBearerAndKeepsTheJwt()
        {
            var transport = new FakeAuthTransport().Reply(200, "{\"userId\":\"u1\",\"exp\":99,\"channelId\":\"auth_1\"}");

            var token = await Create(transport).VerifyAsync(Jwt);

            var sent = transport.Sent[0];
            Assert.That(sent.Url.AbsoluteUri, Is.EqualTo("https://auth.test/c/auth_0123456789abcdef/verify"));
            Assert.That(sent.Headers.Single().Key, Is.EqualTo("Authorization"));
            Assert.That(sent.Headers.Single().Value, Is.EqualTo("Bearer " + Jwt));
            Assert.That(token!.Jwt, Is.EqualTo(Jwt));
            Assert.That(token.UserId, Is.EqualTo("u1"));
            Assert.That(token.ExpiresAt, Is.EqualTo(99L));
        }

        [Test]
        public async Task VerifyAnswersNullOnlyFor401()
        {
            Assert.That(await Create(new FakeAuthTransport().Reply(401, "")).VerifyAsync(Jwt), Is.Null);

            var error = Assert.ThrowsAsync<AuthException>(() => Create(new FakeAuthTransport().Reply(410, "")).VerifyAsync(Jwt));
            Assert.That(error!.Code, Is.EqualTo(AuthErrorCodes.Http));
            Assert.That(error.Status, Is.EqualTo(410));
        }

        [Test]
        public void AJwtAHeaderCannotCarryIsRefusedWithoutQuotingIt()
        {
            var transport = new FakeAuthTransport();

            var error = Assert.ThrowsAsync<ArgumentException>(() => Create(transport).VerifyAsync("eyJ.secret\ntoken"));

            Assert.That(error!.Message, Does.Not.Contain("secret"));
            Assert.That(transport.Sent, Is.Empty);
        }

        // ---- transport failures -----------------------------------------------

        [Test]
        public void ATransportThatThrowsIsNetworkAndItsExceptionIsTheInner()
        {
            var cause = new System.IO.IOException("socket closed");

            var error = Assert.ThrowsAsync<AuthException>(() => Create(new FakeAuthTransport().Throw(cause)).FetchConfigAsync());

            Assert.That(error!.Code, Is.EqualTo(AuthErrorCodes.Network));
            Assert.That(error.Status, Is.EqualTo(0));
            Assert.That(error.InnerException, Is.SameAs(cause));
        }

        // A hanging transport really yields, so these two await rather than block:
        // Assert.ThrowsAsync deadlocks the editor's main thread there (rules/testing.md).
        [Test]
        public async Task NoReplyWithinTheTimeoutIsNetwork()
        {
            var client = Create(new FakeAuthTransport().Hang(), timeout: TimeSpan.FromMilliseconds(50));

            var error = await Fails.WithAuthException(() => client.FetchConfigAsync());

            Assert.That(error.Code, Is.EqualTo(AuthErrorCodes.Network));
        }

        [Test]
        public async Task TheCallersOwnCancellationIsACancellationNotANetworkFailure()
        {
            using (var cancel = new CancellationTokenSource())
            {
                var pending = Create(new FakeAuthTransport().Hang()).FetchConfigAsync(cancel.Token);
                cancel.Cancel();

                await Fails.WithCancellation(() => pending);
            }
        }

        [Test]
        public async Task NeverWritesACredentialToTheLog()
        {
            var log = new CapturingLogWriter();
            var transport = new FakeAuthTransport()
                .Reply(200, "{\"jwt\":\"" + Jwt + "\",\"userId\":\"u\",\"exp\":1}")
                .Reply(200, "{\"userId\":\"u\",\"exp\":1}")
                .Reply(500, "{\"echo\":\"provider-access-token\"}")
                .Throw(new System.IO.IOException("down"));
            var client = Create(transport, Capture(log));

            await client.ExchangeAccessTokenAsync("github", "provider-access-token");
            await client.VerifyAsync(Jwt);
            Assert.ThrowsAsync<AuthException>(() => client.ExchangeAccessTokenAsync("github", "provider-access-token"));
            Assert.ThrowsAsync<AuthException>(() => client.VerifyAsync(Jwt));

            // Positive control: the requests were logged at all.
            Assert.That(log.Lines.Count(l => l.Contains("auth request")), Is.EqualTo(4));
            Assert.That(log.Text, Does.Not.Contain("secret-token"));
            Assert.That(log.Text, Does.Not.Contain("provider-access-token"));
            Assert.That(log.Text, Does.Not.Contain("auth.test"));
        }

        // ---- options -----------------------------------------------------------

        [TestCase(null)]
        [TestCase("")]
        [TestCase("auth.test")]
        [TestCase("ftp://auth.test")]
        [TestCase("https://user:pass@auth.test")]
        [TestCase("https://auth.test/?q=1")]
        [TestCase("https://auth.test/#f")]
        [TestCase("http://auth.test")]
        public void ABaseUrlThatIsNotAPlainSecureOriginIsRefused(string? baseUrl)
        {
            Assert.Throws<ArgumentException>(() => AuthClient.Create(new AuthClientOptions { BaseUrl = baseUrl, ChannelId = "auth_1" }));
        }

        [Test]
        public void TheOtherOptionsAreChecked()
        {
            Assert.Throws<ArgumentNullException>(() => AuthClient.Create(null!));
            Assert.Throws<ArgumentException>(() => AuthClient.Create(new AuthClientOptions { BaseUrl = "https://a.test", ChannelId = "" }));
            Assert.Throws<ArgumentException>(() => AuthClient.Create(new AuthClientOptions { BaseUrl = "https://a.test", ChannelId = "c", NonceParameter = "a b" }));
            Assert.Throws<ArgumentException>(() => AuthClient.Create(new AuthClientOptions { BaseUrl = "https://a.test", ChannelId = "c", Timeout = TimeSpan.Zero }));
        }

        [Test]
        public async Task ABaseUrlWithAPathPrefixKeepsIt()
        {
            var transport = new FakeAuthTransport().Reply(200, "{}");
            var client = AuthClient.Create(new AuthClientOptions { BaseUrl = "https://gw.test/auth/", ChannelId = "auth 1", Transport = transport });

            await client.FetchConfigAsync();

            Assert.That(transport.Sent[0].Url.AbsoluteUri, Is.EqualTo("https://gw.test/auth/c/auth%201/.well-known/config"));
        }

        [TestCase("http://localhost:8787")]
        [TestCase("http://127.0.0.1:8787/")]
        [TestCase("http://[::1]:8787")]
        public void PlainHttpIsAcceptedForLoopbackOnly(string baseUrl)
        {
            Assert.That(AuthClient.Create(new AuthClientOptions { BaseUrl = baseUrl, ChannelId = "c" }), Is.Not.Null);
        }

        [Test]
        public void TheTokenNeverAppearsInItsOwnToString()
        {
            Assert.That(new ChannelToken(Jwt, "u", 5).ToString(), Is.EqualTo("ChannelToken(userId: u, expiresAt: 5)"));
            Assert.That(new ChannelToken(Jwt, "a\nb\u202Ec", 5).ToString(), Is.EqualTo("ChannelToken(userId: a?b?c, expiresAt: 5)"));
            Assert.That(new ChannelToken(Jwt, new string('u', 65), 5).ToString(), Is.EqualTo("ChannelToken(userId: " + new string('u', 64) + "\u2026, expiresAt: 5)"));
        }

        [Test]
        public void ARequestsTimeoutIsChecked()
        {
            var url = new Uri("https://auth.test/");
            var none = new System.Collections.Generic.KeyValuePair<string, string>[0];

            Assert.Throws<ArgumentOutOfRangeException>(() => new AuthHttpRequest("GET", url, none, null, TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AuthHttpRequest("GET", url, none, null, AuthClient.MaxTimeout + TimeSpan.FromMilliseconds(1)));
            Assert.That(new AuthHttpRequest("GET", url, none, null, AuthClient.MaxTimeout).Timeout, Is.EqualTo(AuthClient.MaxTimeout));
        }

        [Test]
        public void IsExpiredComparesWithTheCallersClock()
        {
            var token = new ChannelToken(Jwt, "u", 1000);

            Assert.That(token.IsExpired(DateTimeOffset.FromUnixTimeSeconds(999)), Is.False);
            Assert.That(token.IsExpired(DateTimeOffset.FromUnixTimeSeconds(1000)), Is.True);
        }
    }
}
