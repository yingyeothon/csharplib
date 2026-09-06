using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Yingyeothon.Codec;

namespace Yingyeothon.KvStore.Tests
{
    [TestFixture]
    public class KvClientTests
    {
        [Test]
        public void CreateRefusesNullOptions()
            => Assert.That(() => KvStoreClient.Create(null!), Throws.ArgumentNullException);

        [TestCase(null)]
        [TestCase("")]
        [TestCase("doc.yyt.life")]
        [TestCase("wss://doc.yyt.life")]
        [TestCase("/kv")]
        [TestCase("https://user:pw@doc.yyt.life", Description = "userinfo would put a credential in every URL")]
        [TestCase("https://doc.yyt.life/?debug=1")]
        [TestCase("https://doc.yyt.life/#frag")]
        public void CreateRefusesABaseUrlThatIsNotAPlainHttpOrigin(string? baseUrl)
            => Assert.That(
                () => KvStoreClient.Create(new KvStoreClientOptions { BaseUrl = baseUrl, Token = KvHarness.Token }),
                Throws.ArgumentException);

        [TestCase(null)]
        [TestCase("")]
        public void CreateRefusesAnEmptyToken(string? token)
            => Assert.That(
                () => KvStoreClient.Create(new KvStoreClientOptions { BaseUrl = KvHarness.BaseUrl, Token = token }),
                Throws.ArgumentException);

        [Test]
        public void CreateRefusesATimeoutOutOfRange()
        {
            Assert.That(
                () => KvStoreClient.Create(new KvStoreClientOptions { BaseUrl = KvHarness.BaseUrl, Token = KvHarness.Token, Timeout = TimeSpan.Zero }),
                Throws.ArgumentException);
            Assert.That(
                () => KvStoreClient.Create(new KvStoreClientOptions
                {
                    BaseUrl = KvHarness.BaseUrl,
                    Token = KvHarness.Token,
                    Timeout = KvStoreClient.MaxTimeout + TimeSpan.FromMilliseconds(1),
                }),
                Throws.ArgumentException);
            Assert.DoesNotThrow(() => KvStoreClient.Create(new KvStoreClientOptions
            {
                BaseUrl = KvHarness.BaseUrl,
                Token = KvHarness.Token,
                Timeout = KvStoreClient.MaxTimeout,
            }));
        }

        /// <remarks>
        /// A header value may hold only visible ASCII. The refusal reports the index and
        /// never the character: the string is the credential.
        /// </remarks>
        [TestCase("eyJ.secret\ntoken.sig", 10)]
        [TestCase("eyJ.secret token.sig", 10)]
        [TestCase("eyJ.secret-token.sig\r", 20)]
        [TestCase("토큰", 0)]
        public void CreateRefusesATokenAHeaderCannotCarryByIndexOnly(string token, int index)
        {
            var error = Assert.Throws<ArgumentException>(
                () => KvStoreClient.Create(new KvStoreClientOptions { BaseUrl = KvHarness.BaseUrl, Token = token }));

            Assert.That(error!.Message, Does.Contain("index " + index));
            Assert.That(error.Message, Does.Not.Contain("secret"));
            Assert.That(error.Message, Does.Not.Contain("토"));
        }

        [Test]
        public void TheDefaultsAreTheSharedTransportAndFifteenSeconds()
        {
            var client = KvStoreClient.Create(new KvStoreClientOptions { BaseUrl = KvHarness.BaseUrl, Token = KvHarness.Token });

            Assert.That(client, Is.Not.Null);
            Assert.That(KvStoreClient.DefaultTimeout, Is.EqualTo(TimeSpan.FromSeconds(15)));
            Assert.That(HttpClientTransport.Default, Is.Not.Null);
        }

        [Test]
        public async Task ATrailingSlashOnTheBaseUrlIsNotDoubled()
        {
            var harness = new KvHarness(o => o.BaseUrl = "https://doc-dev.yyt.life/");
            harness.Transport.Reply(404);

            await harness.Profile.GetAsync("k");

            Assert.That(harness.Transport.LastCall.Url.AbsoluteUri, Is.EqualTo("https://doc-dev.yyt.life/kv/profile/entries/k"));
        }

        [Test]
        public async Task ABasePathIsKept()
        {
            var harness = new KvHarness(o => o.BaseUrl = "http://localhost:8080/state");
            harness.Transport.Reply(404);

            await harness.Profile.GetAsync("k");

            Assert.That(harness.Transport.LastCall.Url.AbsoluteUri, Is.EqualTo("http://localhost:8080/state/kv/profile/entries/k"));
        }

        [TestCase("a:b")]
        [TestCase("")]
        [TestCase("with space")]
        [TestCase("KV_01H5XK4R7Z3B2M9Q8W6E5T4Y3N", Description = "id-shaped once lower-cased: the store answers it without a lookup")]
        public void CollectionRefusesASegmentThatIsNeitherAnIdNorAName(string reference)
        {
            var harness = new KvHarness();

            Assert.That(() => harness.Client.Collection(reference), Throws.ArgumentException);
            Assert.That(harness.Transport.Calls, Is.Empty);
        }

        [Test]
        public void CollectionIsPureAndRemembersItsRef()
        {
            var harness = new KvHarness();

            var collection = harness.Client.Collection("kv_01h5xk4r7z3b2m9q8w6e5t4y3n");

            Assert.That(collection.Ref, Is.EqualTo("kv_01h5xk4r7z3b2m9q8w6e5t4y3n"));
            Assert.That(harness.Transport.Calls, Is.Empty);
        }

        [Test]
        public async Task TheCollectionItselfIsTheSharedNamespace()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(404);

            await harness.Client.Collection("announcements").GetAsync("today");

            Assert.That(harness.Transport.LastCall.Url.AbsoluteUri, Is.EqualTo(KvHarness.BaseUrl + "/kv/announcements/entries/today"));
        }

        [Test]
        public async Task MineIsThePlayerAlias()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(404);

            await harness.Profile.Mine.GetAsync("settings");

            Assert.That(harness.Transport.LastCall.Url.AbsoluteUri, Is.EqualTo(KvHarness.BaseUrl + "/kv/profile/u/me/entries/settings"));
        }

        [Test]
        public async Task OwnerNamesTheNamespace()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(404);

            await harness.Profile.Owner("party:raid-7").GetAsync("loot");

            Assert.That(harness.Transport.LastCall.Url.AbsoluteUri, Is.EqualTo(KvHarness.BaseUrl + "/kv/profile/u/party:raid-7/entries/loot"));
        }

        [TestCase("")]
        [TestCase("ME")]
        [TestCase("party:a/b")]
        [TestCase("0123456789ABCDEF0123456789abcdef")]
        public void OwnerRefusesAnIdTheServerWould(string owner)
        {
            var harness = new KvHarness();

            Assert.That(() => harness.Profile.Owner(owner), Throws.ArgumentException);
            Assert.That(harness.Transport.Calls, Is.Empty);
        }

        [Test]
        public async Task InfoReadsTheCollectionShape()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(200, "{\"id\":\"kv_01h5xk4r7z3b2m9q8w6e5t4y3n\",\"name\":\"profile\",\"readScope\":\"user\","
                + "\"writeScope\":\"user\",\"encrypted\":true,\"maxEntries\":10000,\"maxEntriesPerOwner\":100}");

            var info = await harness.Profile.InfoAsync();

            var call = harness.Transport.LastCall;
            Assert.That(call.Method, Is.EqualTo("GET"));
            Assert.That(call.Url.AbsoluteUri, Is.EqualTo(KvHarness.BaseUrl + "/kv/profile"));
            Assert.That(call.Body, Is.Null);
            Assert.That(call.Header("Authorization"), Is.EqualTo("Bearer " + KvHarness.Token));
            Assert.That(call.Header("Content-Type"), Is.Null, "no body, no content type");
            Assert.That(call.Timeout, Is.EqualTo(KvStoreClient.DefaultTimeout), "the transport is told the bound");
            Assert.That(info.Id, Is.EqualTo("kv_01h5xk4r7z3b2m9q8w6e5t4y3n"));
            Assert.That(info.Name, Is.EqualTo("profile"));
            Assert.That(info.ReadScope, Is.EqualTo(KvScope.User));
            Assert.That(info.WriteScope, Is.EqualTo(KvScope.User));
            Assert.That(info.Encrypted, Is.True);
            Assert.That(info.MaxEntries, Is.EqualTo(10000));
            Assert.That(info.MaxEntriesPerOwner, Is.EqualTo(100));
        }

        [TestCase("team", KvScope.Team)]
        [TestCase("project", KvScope.Project)]
        public void EveryScopeParses(string wire, KvScope expected)
        {
            var harness = new KvHarness();
            harness.Transport.Reply(200, "{\"id\":\"kv_01h5xk4r7z3b2m9q8w6e5t4y3n\",\"name\":\"n\",\"readScope\":\"" + wire
                + "\",\"writeScope\":\"project\",\"encrypted\":false,\"maxEntries\":1,\"maxEntriesPerOwner\":1}");

            var info = harness.Profile.InfoAsync().GetAwaiter().GetResult();

            Assert.That(info.ReadScope, Is.EqualTo(expected));
        }

        [TestCase("{\"id\":\"kv_x\",\"name\":\"n\",\"readScope\":\"owner\",\"writeScope\":\"project\",\"encrypted\":false,\"maxEntries\":1,\"maxEntriesPerOwner\":1}", Description = "unknown scope")]
        [TestCase("{\"name\":\"n\",\"readScope\":\"project\",\"writeScope\":\"project\",\"encrypted\":false,\"maxEntries\":1,\"maxEntriesPerOwner\":1}", Description = "no id")]
        [TestCase("{\"id\":\"kv_x\",\"name\":\"n\",\"readScope\":\"project\",\"writeScope\":\"project\",\"maxEntries\":1,\"maxEntriesPerOwner\":1}", Description = "no encrypted")]
        [TestCase("{\"id\":\"kv_x\",\"name\":\"n\",\"readScope\":\"project\",\"writeScope\":\"project\",\"encrypted\":false,\"maxEntries\":1.5,\"maxEntriesPerOwner\":1}", Description = "fractional cap")]
        [TestCase("{\"id\":\"kv_x\",\"name\":\"n\",\"readScope\":\"project\",\"writeScope\":\"project\",\"encrypted\":false,\"maxEntries\":4294967296,\"maxEntriesPerOwner\":1}", Description = "cap over int.MaxValue")]
        [TestCase("{\"id\":\"kv_x\",\"name\":\"n\",\"readScope\":\"project\",\"writeScope\":\"project\",\"encrypted\":false,\"maxEntries\":-1,\"maxEntriesPerOwner\":1}", Description = "negative cap")]
        [TestCase("[]", Description = "not an object")]
        [TestCase("<html>", Description = "not JSON")]
        public void InfoRefusesABodyThatIsNotTheShape(string body)
        {
            var harness = new KvHarness();
            harness.Transport.Reply(200, body);

            var error = Assert.ThrowsAsync<KvStoreException>(async () => await harness.Profile.InfoAsync());

            Assert.That(error!.Code, Is.EqualTo(KvErrorCodes.BadBody));
            Assert.That(error.Status, Is.EqualTo(200));
        }

        [Test]
        public void InfoOfATeamOnlyCollectionIsForbidden()
        {
            var harness = new KvHarness();
            harness.Transport.Error(403, "forbidden");

            var error = Assert.ThrowsAsync<KvStoreException>(async () => await harness.Profile.InfoAsync());

            Assert.That(error!.IsForbidden, Is.True);
            Assert.That(error.Code, Is.EqualTo(KvErrorCodes.Forbidden));
        }

        /// <remarks>
        /// A positive control first: the lines this test expects really were written.
        /// Then the token, a distinctive fragment of it and the word bearer are asserted
        /// separately, because a leak often prints only part of it — and the exception
        /// message is asserted against a template nothing derived from the input could
        /// satisfy.
        /// </remarks>
        [Test]
        public async Task TheTokenReachesNoLogLineNoExceptionAndNoUrl()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(200, "{\"volume\":1}").Error(401, "unauthorized");
            harness.Transport.Throws = null;

            await harness.Profile.Mine.GetAsync("settings");
            var error = Assert.ThrowsAsync<KvStoreException>(async () => await harness.Profile.Mine.GetAsync("settings"));

            Assert.That(harness.Log.Lines, Has.Count.EqualTo(2), "positive control: both requests were logged");
            Assert.That(harness.Log.Text, Does.Contain("kv request"));
            Assert.That(harness.Log.Text, Does.Contain("\"status\":401"));
            Assert.That(harness.Log.Text, Does.Not.Contain(KvHarness.Token));
            Assert.That(harness.Log.Text, Does.Not.Contain("secret-token"));
            Assert.That(harness.Log.Text.ToLowerInvariant(), Does.Not.Contain("bearer"));
            Assert.That(harness.Log.Text, Does.Not.Contain("settings"), "nor the key");
            Assert.That(harness.Log.Text, Does.Not.Contain("volume"), "nor the value");
            Assert.That(harness.Log.Text, Does.Not.Contain("/kv/"), "nor the path");
            Assert.That(error!.Message, Is.EqualTo("kv unauthorized (401)"));
            Assert.That(error.IsUnauthorized, Is.True);
            foreach (var call in harness.Transport.Calls)
            {
                Assert.That(call.Url.AbsoluteUri, Does.Not.Contain("secret-token"));
            }
        }

        [Test]
        public void TheExceptionMessageIsTheTemplateForEveryShape()
        {
            Assert.That(new KvStoreException(0, KvErrorCodes.Network).Message, Is.EqualTo("kv network (0)"));
            Assert.That(
                new KvStoreException(409, KvErrorCodes.Conflict, KvReasons.OwnerFull, null, false, null).Message,
                Is.EqualTo("kv conflict (409)"));
            Assert.That(() => new KvStoreException(1, null!), Throws.ArgumentNullException);
        }

        [Test]
        public void ChangingTheOptionsAfterCreateChangesNothing()
        {
            var transport = new FakeTransport().Reply(404);
            var options = new KvStoreClientOptions { BaseUrl = KvHarness.BaseUrl, Token = KvHarness.Token, Transport = transport };
            var client = KvStoreClient.Create(options);
            options.Token = "another";
            options.BaseUrl = "https://elsewhere.example";

            client.Collection("profile").GetAsync("k").GetAwaiter().GetResult();

            Assert.That(transport.LastCall.Header("Authorization"), Is.EqualTo("Bearer " + KvHarness.Token));
            Assert.That(transport.LastCall.Url.Host, Is.EqualTo("doc-dev.yyt.life"));
        }

        [Test]
        public void ResultTypesRefuseNulls()
        {
            Assert.That(() => new KvEntry(null!, 1, null), Throws.ArgumentNullException);
            Assert.That(() => new KvPage(null!, null), Throws.ArgumentNullException);
            Assert.That(() => new KvListEntry(null, null!, 1, 1, null, 1, null), Throws.ArgumentNullException);
            Assert.That(() => new KvCollectionInfo(null!, "n", KvScope.Team, KvScope.Team, false, 1, 1), Throws.ArgumentNullException);
            Assert.That(() => new KvCollectionInfo("id", null!, KvScope.Team, KvScope.Team, false, 1, 1), Throws.ArgumentNullException);
            Assert.That(() => new HttpCall(null!, new Uri("https://x"), KvReplyHeaders.None, null, TimeSpan.FromSeconds(1)), Throws.ArgumentNullException);
            Assert.That(() => new HttpCall("GET", null!, KvReplyHeaders.None, null, TimeSpan.FromSeconds(1)), Throws.ArgumentNullException);
            Assert.That(() => new HttpCall("GET", new Uri("https://x"), null!, null, TimeSpan.FromSeconds(1)), Throws.ArgumentNullException);
            Assert.That(() => new HttpReply(200, null!, ""), Throws.ArgumentNullException);
            Assert.That(() => new HttpReply(200, KvReplyHeaders.None, null!), Throws.ArgumentNullException);
            Assert.That(
                () => new HttpCall("GET", new Uri("https://x"), KvReplyHeaders.None, null, TimeSpan.Zero),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => new HttpCall("GET", new Uri("https://x"), KvReplyHeaders.None, null, KvStoreClient.MaxTimeout + TimeSpan.FromMilliseconds(1)),
                Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(new KvIncrResult(3, 4, null).Value, Is.EqualTo(3));
            Assert.That(new KvWriteResult(true, 2, 3).ExpiresAt, Is.EqualTo(3));
            Assert.That(JsonValue.Null.IsNull, Is.True);
        }
    }

    internal static class KvReplyHeaders
    {
        internal static readonly System.Collections.Generic.KeyValuePair<string, string>[] None =
            new System.Collections.Generic.KeyValuePair<string, string>[0];
    }
}
