using System.Threading.Tasks;
using NUnit.Framework;
using Yingyeothon.Codec;

namespace Yingyeothon.KvStore.Tests
{
    [TestFixture]
    public class KvListIncrTests
    {
        private const string TwoRows = "{\"entries\":[" +
            "{\"key\":\"2026-09-06\",\"version\":2,\"bytes\":24,\"expiresAt\":null,\"updatedAt\":1757116800,\"valueText\":\"{\\\"title\\\":\\\"maintenance\\\"}\"}," +
            "{\"key\":\"2026-09-01\",\"version\":1,\"bytes\":4,\"expiresAt\":1760000000,\"updatedAt\":1756684800,\"valueText\":\"null\"}" +
            "],\"nextCursor\":\"c3VyZWx5LW5vdC1hLWp3dA\"}";

        [Test]
        public async Task ListWithNoOptionsIsAPlainGet()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(200, "{\"entries\":[]}");

            var page = await harness.Client.Collection("announcements").ListAsync();

            var call = harness.Transport.LastCall;
            Assert.That(call.Method, Is.EqualTo("GET"));
            Assert.That(call.Url.AbsoluteUri, Is.EqualTo(KvHarness.BaseUrl + "/kv/announcements/entries"));
            Assert.That(page.Entries, Is.Empty);
            Assert.That(page.NextCursor, Is.Null);
        }

        [Test]
        public async Task ListBuildsTheQueryAndEscapesFreeText()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(200, "{\"entries\":[]}");

            await harness.Client.Collection("announcements").ListAsync(new KvListOptions
            {
                Prefix = "2026-09:a.b",
                Cursor = "c3VyZWx5LW5vdC1hLWp3dA==/&",
                Limit = 10,
                Order = KvOrder.Desc,
                Values = true,
            });

            Assert.That(
                harness.Transport.LastCall.Url.AbsoluteUri,
                Is.EqualTo(KvHarness.BaseUrl + "/kv/announcements/entries?prefix=2026-09%3Aa.b&cursor=c3VyZWx5LW5vdC1hLWp3dA%3D%3D%2F%26&limit=10&order=desc&values=1"));
        }

        [TestCase("2026-09/a")]
        [TestCase("a b")]
        [TestCase(".x")]
        public void ListRefusesAPrefixOutsideTheKeyGrammar(string prefix)
        {
            var harness = new KvHarness();

            Assert.That(async () => await harness.Profile.ListAsync(new KvListOptions { Prefix = prefix }), Throws.ArgumentException);
            Assert.That(harness.Transport.Calls, Is.Empty);
        }

        [Test]
        public async Task AnEmptyPrefixIsSentAsIs()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(200, "{\"entries\":[]}");

            await harness.Profile.ListAsync(new KvListOptions { Prefix = string.Empty });

            Assert.That(harness.Transport.LastCall.Url.Query, Is.EqualTo("?prefix="));
        }

        [Test]
        public async Task ListSendsOnlyWhatWasSet()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(200, "{\"entries\":[]}");

            await harness.Client.Collection("announcements").ListAsync(new KvListOptions { Order = KvOrder.Asc });

            Assert.That(harness.Transport.LastCall.Url.Query, Is.EqualTo("?order=asc"));
        }

        [Test]
        public async Task ListParsesRowsAndTheirValues()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(200, TwoRows);

            var page = await harness.Client.Collection("announcements").ListAsync(new KvListOptions { Values = true, Order = KvOrder.Desc });

            // Entries is an array behind IReadOnlyList: NUnit 3.5 in Unity has no Count constraint for it.
            Assert.That(page.Entries.Count, Is.EqualTo(2));
            var first = page.Entries[0];
            Assert.That(first.Owner, Is.Null);
            Assert.That(first.Key, Is.EqualTo("2026-09-06"));
            Assert.That(first.Version, Is.EqualTo(2));
            Assert.That(first.Bytes, Is.EqualTo(24));
            Assert.That(first.ExpiresAt, Is.Null);
            Assert.That(first.UpdatedAt, Is.EqualTo(1757116800));
            Assert.That(first.Value!.GetString("title"), Is.EqualTo("maintenance"));
            var second = page.Entries[1];
            Assert.That(second.ExpiresAt, Is.EqualTo(1760000000));
            Assert.That(second.Value, Is.Not.Null, "a stored null is a value");
            Assert.That(second.Value!.IsNull, Is.True);
            Assert.That(page.NextCursor, Is.EqualTo("c3VyZWx5LW5vdC1hLWp3dA"));
        }

        [Test]
        public async Task AListWithoutValuesHasNullValuesAndCarriesTheOwner()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(200, "{\"entries\":[{\"owner\":\"0123456789abcdef0123456789abcdef\",\"key\":\"settings\",\"version\":1,\"bytes\":2,\"expiresAt\":null,\"updatedAt\":1}]}");

            var page = await harness.Profile.ListAsync();

            Assert.That(page.Entries[0].Owner, Is.EqualTo("0123456789abcdef0123456789abcdef"));
            Assert.That(page.Entries[0].Value, Is.Null);
        }

        [TestCase("{\"entries\":[{\"key\":\"k\",\"version\":1,\"bytes\":2,\"updatedAt\":1,\"valueText\":\"{oops\"}]}", Description = "valueText not JSON")]
        [TestCase("{\"entries\":[{\"version\":1,\"bytes\":2,\"updatedAt\":1}]}", Description = "no key")]
        [TestCase("{\"entries\":[{\"key\":\"k\",\"version\":\"1\",\"bytes\":2,\"updatedAt\":1}]}", Description = "version as a string")]
        [TestCase("{\"entries\":[{\"key\":\"k\",\"version\":1,\"bytes\":2,\"updatedAt\":1,\"expiresAt\":1.5}]}", Description = "fractional expiry")]
        [TestCase("{\"entries\":[1]}", Description = "row not an object")]
        [TestCase("[]", Description = "not an object")]
        [TestCase("", Description = "empty")]
        public void ListRefusesARowThatIsNotTheShape(string body)
        {
            var harness = new KvHarness();
            harness.Transport.Reply(200, body);

            var error = Assert.ThrowsAsync<KvStoreException>(async () => await harness.Profile.ListAsync());

            Assert.That(error!.Code, Is.EqualTo(KvErrorCodes.BadBody));
        }

        [TestCase(0)]
        [TestCase(101)]
        public void ListRefusesALimitOutOfRange(int limit)
        {
            var harness = new KvHarness();

            Assert.That(async () => await harness.Profile.ListAsync(new KvListOptions { Limit = limit }), Throws.ArgumentException);
            Assert.That(harness.Transport.Calls, Is.Empty);
        }

        [Test]
        public async Task ListLimitBoundsAreInclusive()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(200, "{\"entries\":[]}").Reply(200, "{\"entries\":[]}");

            await harness.Profile.ListAsync(new KvListOptions { Limit = 1 });
            await harness.Profile.ListAsync(new KvListOptions { Limit = 100 });

            Assert.That(harness.Transport.Calls[0].Url.Query, Is.EqualTo("?limit=1"));
            Assert.That(harness.Transport.Calls[1].Url.Query, Is.EqualTo("?limit=100"));
        }

        [Test]
        public void ListSurfacesARefusal()
        {
            var harness = new KvHarness();
            harness.Transport.Error(403, "forbidden");

            var error = Assert.ThrowsAsync<KvStoreException>(async () => await harness.Profile.ListAsync());

            Assert.That(error!.Status, Is.EqualTo(403), "not the network failure an unscripted fake produces");
        }

        [Test]
        public async Task IncrPatchesAnIntegerBody()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(200, "{\"value\":42,\"version\":7}", etag: "\"7\"", expiresAt: "1700000060");

            var result = await harness.Profile.Mine.IncrAsync("plays", 5, new KvIncrOptions { Ttl = 60 });

            var call = harness.Transport.LastCall;
            Assert.That(call.Method, Is.EqualTo("PATCH"));
            Assert.That(call.Url.AbsoluteUri, Is.EqualTo(KvHarness.BaseUrl + "/kv/profile/u/me/entries/plays?ttl=60"));
            Assert.That(call.Body, Is.EqualTo("{\"incr\":5}"), "an integer, never 5.0");
            Assert.That(call.Header("Content-Type"), Is.EqualTo("application/json"));
            Assert.That(call.Header("If-Match"), Is.Null);
            Assert.That(result.Value, Is.EqualTo(42));
            Assert.That(result.Version, Is.EqualTo(7));
            Assert.That(result.ExpiresAt, Is.EqualTo(1700000060));
        }

        [Test]
        public async Task IncrByANegativeDelta()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(200, "{\"value\":-3,\"version\":1}");

            var result = await harness.Profile.Mine.IncrAsync("plays", -3);

            Assert.That(harness.Transport.LastCall.Body, Is.EqualTo("{\"incr\":-3}"));
            Assert.That(harness.Transport.LastCall.Url.Query, Is.Empty);
            Assert.That(result.Value, Is.EqualTo(-3));
            Assert.That(result.ExpiresAt, Is.Null);
        }

        [Test]
        public async Task IncrCarriesTheLargestSafeInteger()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(200, "{\"value\":9007199254740991,\"version\":1}");

            var result = await harness.Profile.Mine.IncrAsync("plays", KvRules.MaxSafeInteger);

            Assert.That(harness.Transport.LastCall.Body, Is.EqualTo("{\"incr\":9007199254740991}"));
            Assert.That(result.Value, Is.EqualTo(KvRules.MaxSafeInteger));
        }

        [TestCase(9007199254740992L)]
        [TestCase(-9007199254740992L)]
        public void IncrRefusesADeltaADoubleCannotCarry(long delta)
        {
            var harness = new KvHarness();

            Assert.That(async () => await harness.Profile.Mine.IncrAsync("plays", delta), Throws.InstanceOf<System.ArgumentOutOfRangeException>());
            Assert.That(harness.Transport.Calls, Is.Empty);
        }

        [Test]
        public void IncrRefusesATtlOutOfRange()
        {
            var harness = new KvHarness();

            Assert.That(async () => await harness.Profile.Mine.IncrAsync("plays", 1, new KvIncrOptions { Ttl = -5 }), Throws.ArgumentException);
            Assert.That(harness.Transport.Calls, Is.Empty);
        }

        [TestCase(KvReasons.NotANumber)]
        [TestCase(KvReasons.Overflow)]
        public void IncrSurfacesTheCounterConflicts(string reason)
        {
            var harness = new KvHarness();
            harness.Transport.Error(409, "conflict", reason: reason);

            var error = Assert.ThrowsAsync<KvStoreException>(async () => await harness.Profile.Mine.IncrAsync("plays", 1));

            Assert.That(error!.IsConflict, Is.True);
            Assert.That(error.IsFull, Is.False);
            Assert.That(error.Reason, Is.EqualTo(reason));
        }

        [TestCase("{\"value\":1}")]
        [TestCase("{\"value\":1.5,\"version\":1}")]
        [TestCase("{\"value\":9007199254740992,\"version\":1}")]
        [TestCase("7")]
        public void IncrRefusesABodyThatIsNotTheShape(string body)
        {
            var harness = new KvHarness();
            harness.Transport.Reply(200, body);

            var error = Assert.ThrowsAsync<KvStoreException>(async () => await harness.Profile.Mine.IncrAsync("plays", 1));

            Assert.That(error!.Code, Is.EqualTo(KvErrorCodes.BadBody));
        }
    }
}
