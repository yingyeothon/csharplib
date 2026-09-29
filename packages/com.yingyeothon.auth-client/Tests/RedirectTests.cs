using System;
using System.Linq;
using NUnit.Framework;

namespace Yingyeothon.Auth.Tests
{
    [TestFixture]
    public class RedirectTests
    {
        private const string Nonce = "n0nce-value_1";

        private static IAuthClient Create(string? nonceParameter = null)
            => AuthClient.Create(new AuthClientOptions
            {
                BaseUrl = "https://auth.test",
                ChannelId = "auth_1",
                Transport = new FakeAuthTransport(),
                NonceParameter = nonceParameter,
            });

        [Test]
        public void TheStartUrlCarriesTheProviderAndTheRedirectWithItsNonce()
        {
            var url = Create().BuildStartUrl("github", new Uri("https://game.test/signin"), Nonce);

            Assert.That(url.AbsoluteUri, Is.EqualTo(
                "https://auth.test/c/auth_1/start?provider=github&redirect="
                + Uri.EscapeDataString("https://game.test/signin?nonce=" + Nonce)));
        }

        [Test]
        public void AnExistingQueryIsKeptAndAnOldNonceReplaced()
        {
            var url = Create().BuildStartUrl("github", new Uri("https://game.test/signin?lang=ko&nonce=stale"), Nonce);

            var redirect = Uri.UnescapeDataString(url.Query.Split(new[] { "redirect=" }, StringSplitOptions.None)[1]);
            Assert.That(redirect, Is.EqualTo("https://game.test/signin?lang=ko&nonce=" + Nonce));
        }

        [Test]
        public void TheNonceParameterIsConfigurable()
        {
            var client = Create("state_x");
            var url = client.BuildStartUrl("google", new Uri("http://localhost:7777/cb"), Nonce);

            Assert.That(Uri.UnescapeDataString(url.AbsoluteUri), Does.EndWith("redirect=http://localhost:7777/cb?state_x=" + Nonce));
            var token = client.ParseRedirect(new Uri("http://localhost:7777/cb?state_x=" + Nonce + "#token=t&userId=u&exp=1"), Nonce);
            Assert.That(token.Jwt, Is.EqualTo("t"));
        }

        [Test]
        public void AnIpv6LoopbackAPortAndAnEscapedPathSurvive()
        {
            var url = Create().BuildStartUrl("github", new Uri("http://[::1]:7777/sign%20in/cb"), Nonce);

            Assert.That(Uri.UnescapeDataString(url.Query), Does.EndWith("redirect=http://[::1]:7777/sign%20in/cb?nonce=" + Nonce));
        }

        [Test]
        public void ANonAsciiHostGoesOutInItsAsciiForm()
        {
            var url = Create().BuildStartUrl("github", new Uri("https://게임.test/cb"), Nonce);

            Assert.That(Uri.UnescapeDataString(url.Query), Does.Contain("redirect=https://xn--"));
        }

        [Test]
        public void ARedirectThatCannotCarryTheFragmentIsRefused()
        {
            var client = Create();

            Assert.Throws<ArgumentException>(() => client.BuildStartUrl("github", new Uri("https://game.test/#x"), Nonce));
            Assert.Throws<ArgumentException>(() => client.BuildStartUrl("github", new Uri("https://user:pass@game.test/cb"), Nonce));
            Assert.Throws<ArgumentException>(() => client.BuildStartUrl("github", new Uri("http://game.test/cb"), Nonce));
            // "https://game.test/" is 18 characters and "?nonce=" + the nonce 20 more.
            var fits = new Uri("https://game.test/" + new string('a', 2048 - 18 - 20));
            var over = new Uri("https://game.test/" + new string('a', 2048 - 18 - 20 + 1));
            Assert.That(client.BuildStartUrl("github", fits, Nonce), Is.Not.Null);
            Assert.Throws<ArgumentException>(() => client.BuildStartUrl("github", over, Nonce));
            Assert.Throws<ArgumentException>(() => client.BuildStartUrl("github", new Uri("mygame://cb"), Nonce));
            Assert.Throws<ArgumentException>(() => client.BuildStartUrl("github", new Uri("/relative", UriKind.Relative), Nonce));
            Assert.Throws<ArgumentException>(() => client.BuildStartUrl("", new Uri("https://game.test/"), Nonce));
            Assert.Throws<ArgumentException>(() => client.BuildStartUrl("github", new Uri("https://game.test/"), ""));
        }

        [Test]
        public void TheReturnedUrlYieldsTheToken()
        {
            // URLSearchParams on the service side: '+' is a space, and the JWT's own
            // characters arrive escaped or not.
            var returned = new Uri("https://game.test/signin?nonce=" + Nonce + "#token=eyJ.a%2Db.c&userId=8d0f&exp=1767225600");

            var token = Create().ParseRedirect(returned, Nonce);

            Assert.That(token.Jwt, Is.EqualTo("eyJ.a-b.c"));
            Assert.That(token.UserId, Is.EqualTo("8d0f"));
            Assert.That(token.ExpiresAt, Is.EqualTo(1767225600L));
        }

        [TestCase("https://game.test/signin#token=t&userId=u&exp=1")]
        [TestCase("https://game.test/signin?nonce=other#token=t&userId=u&exp=1")]
        [TestCase("https://game.test/signin?nonce=n0nce-value_#token=t&userId=u&exp=1")]
        [TestCase("https://game.test/signin?nonce=n0nce-value_12#token=t&userId=u&exp=1")]
        [TestCase("https://game.test/signin?nonce=n0nce-value_1&nonce=n0nce-value_1#token=t&userId=u&exp=1")]
        [TestCase("https://game.test/signin?nonce=attacker&nonce=n0nce-value_1#token=t&userId=u&exp=1")]
        [TestCase("https://game.test/signin#nonce=n0nce-value_1&token=t&userId=u&exp=1")]
        public void AMissingDuplicatedOrForeignNonceIsNonceMismatch(string returned)
        {
            var error = Assert.Throws<AuthException>(() => Create().ParseRedirect(new Uri(returned), Nonce));

            Assert.That(error!.Code, Is.EqualTo(AuthErrorCodes.NonceMismatch));
            Assert.That(error.Message, Is.EqualTo("auth nonce_mismatch (0)"));
        }

        [TestCase("https://game.test/signin?nonce=n0nce-value_1")]
        [TestCase("https://game.test/signin?nonce=n0nce-value_1#")]
        [TestCase("https://game.test/signin?nonce=n0nce-value_1#userId=u&exp=1")]
        [TestCase("https://game.test/signin?nonce=n0nce-value_1#token=&userId=u")]
        public void NoTokenInTheFragmentIsMissingFragment(string returned)
        {
            var error = Assert.Throws<AuthException>(() => Create().ParseRedirect(new Uri(returned), Nonce));

            Assert.That(error!.Code, Is.EqualTo(AuthErrorCodes.MissingFragment));
        }

        [TestCase("https://game.test/signin?nonce=n0nce-value_1#token=t&exp=1")]
        [TestCase("https://game.test/signin?nonce=n0nce-value_1#token=t&userId=u")]
        [TestCase("https://game.test/signin?nonce=n0nce-value_1#token=t&userId=u&exp=soon")]
        public void AFragmentWithoutTheUserOrTheExpiryIsMissingField(string returned)
        {
            var error = Assert.Throws<AuthException>(() => Create().ParseRedirect(new Uri(returned), Nonce));

            Assert.That(error!.Code, Is.EqualTo(AuthErrorCodes.MissingField));
        }

        [Test]
        public void APlusInTheFragmentIsASpace()
        {
            var token = Create().ParseRedirect(new Uri("https://game.test/signin?nonce=" + Nonce + "#token=a+b&userId=u%2B1&exp=1"), Nonce);

            Assert.That(token.Jwt, Is.EqualTo("a b"));
            Assert.That(token.UserId, Is.EqualTo("u+1"));
        }

        [Test]
        public void AMalformedEscapeIsDataNotAnError()
        {
            // System.Uri itself re-escapes a malformed %ZZ as %25ZZ, so the decoder sees a
            // well-formed escape and hands the text back unchanged; nothing throws, so no
            // exception message can quote it.
            var returned = new Uri("https://game.test/signin?nonce=" + Nonce + "#token=secret%ZZtoken&userId=u&exp=1");

            var token = Create().ParseRedirect(returned, Nonce);

            Assert.That(token.Jwt, Is.EqualTo("secret%ZZtoken"));
        }

        [Test]
        public void NewNonceIsUrlSafeAndFresh()
        {
            var nonces = Enumerable.Range(0, 50).Select(_ => AuthClient.NewNonce()).ToList();

            Assert.That(nonces.Distinct().Count(), Is.EqualTo(50));
            foreach (var nonce in nonces)
            {
                Assert.That(nonce.Length, Is.EqualTo(43));
                Assert.That(nonce, Does.Match("^[A-Za-z0-9_-]+$"));
            }
        }
    }
}
