using System.Threading.Tasks;
using NUnit.Framework;
using Yingyeothon.Codec;

namespace Yingyeothon.KvStore.Tests
{
    [TestFixture]
    public class KvEntryTests
    {
        [Test]
        public async Task GetParsesTheBody()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(200, "{\"volume\":0.5,\"name\":\"yeti\"}", etag: "\"3\"");

            var value = await harness.Profile.Mine.GetAsync("settings");

            var call = harness.Transport.LastCall;
            Assert.That(call.Method, Is.EqualTo("GET"));
            Assert.That(call.Body, Is.Null);
            Assert.That(value!.GetNumber("volume"), Is.EqualTo(0.5));
            Assert.That(value.GetString("name"), Is.EqualTo("yeti"));
        }

        [Test]
        public async Task GetOfAMissingKeyIsANull()
        {
            var harness = new KvHarness();
            harness.Transport.Error(404, "not_found");

            var value = await harness.Profile.Mine.GetAsync("settings");

            Assert.That(value, Is.Null);
        }

        [Test]
        public async Task AStoredJsonNullIsNotAMissingKey()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(200, "null", etag: "\"1\"");

            var value = await harness.Profile.Mine.GetAsync("settings");

            Assert.That(value, Is.Not.Null);
            Assert.That(value!.IsNull, Is.True);
        }

        [Test]
        public void GetRefusesABodyThatIsNotJson()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(200, "{not json", etag: "\"1\"");

            var error = Assert.ThrowsAsync<KvStoreException>(async () => await harness.Profile.Mine.GetAsync("settings"));

            Assert.That(error!.Code, Is.EqualTo(KvErrorCodes.BadBody));
            Assert.That(error.Status, Is.EqualTo(200));
        }

        [Test]
        public void GetSurfacesEveryOtherStatus()
        {
            var harness = new KvHarness();
            harness.Transport.Error(403, "forbidden");

            var error = Assert.ThrowsAsync<KvStoreException>(async () => await harness.Profile.Mine.GetAsync("settings"));

            Assert.That(error!.Status, Is.EqualTo(403));
        }

        [TestCase("\"3\"", "1700000000", 3L, 1700000000L)]
        [TestCase("W/\"7\"", null, 7L, null)]
        [TestCase("12", null, 12L, null)]
        public async Task GetEntryReadsTheVersionAndTheExpiry(string etag, string? expiresAt, long version, long? expected)
        {
            var harness = new KvHarness();
            harness.Transport.Reply(200, "[1,2]", etag: etag, expiresAt: expiresAt);

            var entry = await harness.Profile.Mine.GetEntryAsync("settings");

            Assert.That(entry!.Version, Is.EqualTo(version));
            Assert.That(entry.ExpiresAt, Is.EqualTo(expected));
            Assert.That(entry.Value.AsArray(), Has.Count.EqualTo(2));
        }

        [Test]
        public async Task GetEntryOfAMissingKeyIsANull()
        {
            var harness = new KvHarness();
            harness.Transport.Error(404, "not_found");

            Assert.That(await harness.Profile.Mine.GetEntryAsync("settings"), Is.Null);
        }

        [TestCase(null, "1")]
        [TestCase("\"abc\"", "1")]
        [TestCase("\"\"", "1")]
        [TestCase("\"1234567890123456\"", "1", Description = "sixteen digits is not a version")]
        [TestCase("\"3\"", "soon")]
        public void GetEntryRefusesAReplyWithoutAReadableVersionOrExpiry(string? etag, string expiresAt)
        {
            var harness = new KvHarness();
            harness.Transport.Reply(200, "1", etag: etag, expiresAt: expiresAt);

            var error = Assert.ThrowsAsync<KvStoreException>(async () => await harness.Profile.Mine.GetEntryAsync("settings"));

            Assert.That(error!.Code, Is.EqualTo(KvErrorCodes.BadBody));
        }

        [Test]
        public async Task PutSendsTheCompactJsonAndReadsBackACreate()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(201, etag: "\"1\"", expiresAt: "1700003600");

            var result = await harness.Profile.Mine.PutAsync(
                "settings",
                Json.Object().Set("volume", 0.5).Set("name", "yeti").Build(),
                new KvPutOptions { Ttl = 3600 });

            var call = harness.Transport.LastCall;
            Assert.That(call.Method, Is.EqualTo("PUT"));
            Assert.That(call.Url.AbsoluteUri, Is.EqualTo(KvHarness.BaseUrl + "/kv/profile/u/me/entries/settings?ttl=3600"));
            Assert.That(call.Body, Is.EqualTo("{\"volume\":0.5,\"name\":\"yeti\"}"));
            Assert.That(call.Header("Content-Type"), Is.EqualTo("application/json"));
            Assert.That(call.Header("Authorization"), Is.EqualTo("Bearer " + KvHarness.Token));
            Assert.That(call.Header("If-Match"), Is.Null);
            Assert.That(call.Header("If-None-Match"), Is.Null);
            Assert.That(result.Created, Is.True);
            Assert.That(result.Version, Is.EqualTo(1));
            Assert.That(result.ExpiresAt, Is.EqualTo(1700003600));
        }

        [Test]
        public async Task PutReadsBackAnUpdate()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(204, etag: "\"4\"");

            var result = await harness.Profile.Mine.PutAsync("settings", JsonValue.Of(1));

            Assert.That(harness.Transport.LastCall.Url.Query, Is.Empty, "no ttl means keep");
            Assert.That(result.Created, Is.False);
            Assert.That(result.Version, Is.EqualTo(4));
            Assert.That(result.ExpiresAt, Is.Null);
        }

        [Test]
        public async Task AWriteOnlyCallerGetsAllNulls()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(204);

            var result = await harness.Client.Collection("inbox").PutAsync("k", JsonValue.Of("hi"));

            Assert.That(result.Created, Is.Null);
            Assert.That(result.Version, Is.Null);
            Assert.That(result.ExpiresAt, Is.Null);
        }

        [Test]
        public async Task TtlZeroClearsTheExpiry()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(204, etag: "\"2\"");

            await harness.Profile.Mine.PutAsync("settings", JsonValue.Of(1), new KvPutOptions { Ttl = 0 });

            Assert.That(harness.Transport.LastCall.Url.Query, Is.EqualTo("?ttl=0"));
        }

        [Test]
        public async Task PutSendsIfMatchQuoted()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(204, etag: "\"4\"");

            await harness.Profile.Mine.PutAsync("settings", JsonValue.Of(1), new KvPutOptions { IfMatch = 3 });

            Assert.That(harness.Transport.LastCall.Header("If-Match"), Is.EqualTo("\"3\""));
            Assert.That(harness.Transport.LastCall.Header("If-None-Match"), Is.Null);
        }

        [Test]
        public async Task PutSendsIfNoneMatchStar()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(201, etag: "\"1\"");

            await harness.Profile.Mine.PutAsync("settings", JsonValue.Of(1), new KvPutOptions { IfNoneMatch = true });

            Assert.That(harness.Transport.LastCall.Header("If-None-Match"), Is.EqualTo("*"));
            Assert.That(harness.Transport.LastCall.Header("If-Match"), Is.Null);
        }

        [Test]
        public void PutRefusesBothConditionsBeforeAnyRequest()
        {
            var harness = new KvHarness();

            Assert.That(
                async () => await harness.Profile.Mine.PutAsync("k", JsonValue.Of(1), new KvPutOptions { IfMatch = 1, IfNoneMatch = true }),
                Throws.ArgumentException);
            Assert.That(harness.Transport.Calls, Is.Empty);
        }

        [TestCase(0L)]
        [TestCase(-1L)]
        public void PutRefusesAnIfMatchBelowOne(long version)
        {
            var harness = new KvHarness();

            Assert.That(
                async () => await harness.Profile.Mine.PutAsync("k", JsonValue.Of(1), new KvPutOptions { IfMatch = version }),
                Throws.ArgumentException);
            Assert.That(harness.Transport.Calls, Is.Empty);
        }

        [TestCase(-1)]
        [TestCase(31622401)]
        public void PutRefusesATtlOutOfRange(int ttl)
        {
            var harness = new KvHarness();

            Assert.That(
                async () => await harness.Profile.Mine.PutAsync("k", JsonValue.Of(1), new KvPutOptions { Ttl = ttl }),
                Throws.ArgumentException);
            Assert.That(harness.Transport.Calls, Is.Empty);
        }

        [Test]
        public async Task TtlBoundsAreInclusive()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(204).Reply(204);

            await harness.Profile.Mine.PutAsync("k", JsonValue.Of(1), new KvPutOptions { Ttl = 1 });
            await harness.Profile.Mine.PutAsync("k", JsonValue.Of(1), new KvPutOptions { Ttl = 31622400 });

            Assert.That(harness.Transport.Calls[0].Url.Query, Is.EqualTo("?ttl=1"));
            Assert.That(harness.Transport.Calls[1].Url.Query, Is.EqualTo("?ttl=31622400"));
        }

        [Test]
        public void PutRefusesAValueOverSixteenKiBOfUtf8()
        {
            var harness = new KvHarness();
            // 5462 three-byte characters is 16386 bytes and only 5464 chars: a
            // character count would let it through.
            var big = JsonValue.Of(new string('한', 5462));

            Assert.That(async () => await harness.Profile.Mine.PutAsync("k", big), Throws.ArgumentException);
            Assert.That(harness.Transport.Calls, Is.Empty);
        }

        [Test]
        public async Task PutAcceptsExactlySixteenKiB()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(204);
            var exact = JsonValue.Of(new string('x', 16384 - 2));

            await harness.Profile.Mine.PutAsync("k", exact);

            Assert.That(harness.Transport.LastCall.Body!.Length, Is.EqualTo(16384));
        }

        [Test]
        public void PutRefusesANullValueButNotAJsonNull()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(204);

            Assert.That(async () => await harness.Profile.Mine.PutAsync("k", null!), Throws.ArgumentNullException);
            Assert.DoesNotThrowAsync(async () => await harness.Profile.Mine.PutAsync("k", JsonValue.Null));
            Assert.That(harness.Transport.LastCall.Body, Is.EqualTo("null"));
        }

        [TestCase("")]
        [TestCase(".dot")]
        [TestCase("a/b")]
        [TestCase("a b")]
        public void EveryOperationRefusesABadKeyBeforeAnyRequest(string key)
        {
            var harness = new KvHarness();
            var ns = harness.Profile.Mine;

            Assert.That(async () => await ns.GetAsync(key), Throws.ArgumentException);
            Assert.That(async () => await ns.GetEntryAsync(key), Throws.ArgumentException);
            Assert.That(async () => await ns.PutAsync(key, JsonValue.Of(1)), Throws.ArgumentException);
            Assert.That(async () => await ns.DeleteAsync(key), Throws.ArgumentException);
            Assert.That(async () => await ns.IncrAsync(key, 1), Throws.ArgumentException);
            Assert.That(harness.Transport.Calls, Is.Empty);
        }

        [Test]
        public void ALostCompareAndSetCarriesTheLiveVersion()
        {
            var harness = new KvHarness();
            harness.Transport.Error(409, "conflict", currentJson: "5");

            var error = Assert.ThrowsAsync<KvStoreException>(
                async () => await harness.Profile.Mine.PutAsync("k", JsonValue.Of(1), new KvPutOptions { IfMatch = 3 }));

            Assert.That(error!.IsConflict, Is.True);
            Assert.That(error.HasCurrentVersion, Is.True);
            Assert.That(error.CurrentVersion, Is.EqualTo(5));
            Assert.That(error.IsFull, Is.False);
            Assert.That(error.Reason, Is.Null);
        }

        [Test]
        public void AConflictOverAnAbsentEntryCarriesANullVersionThatIsPresent()
        {
            var harness = new KvHarness();
            harness.Transport.Error(409, "conflict", currentJson: "null");

            var error = Assert.ThrowsAsync<KvStoreException>(
                async () => await harness.Profile.Mine.PutAsync("k", JsonValue.Of(1), new KvPutOptions { IfMatch = 3 }));

            Assert.That(error!.HasCurrentVersion, Is.True);
            Assert.That(error.CurrentVersion, Is.Null);
        }

        [Test]
        public void AConflictToAWriteOnlyCallerCarriesNoVersionAtAll()
        {
            var harness = new KvHarness();
            harness.Transport.Error(409, "conflict");

            var error = Assert.ThrowsAsync<KvStoreException>(
                async () => await harness.Client.Collection("inbox").PutAsync("k", JsonValue.Of(1)));

            Assert.That(error!.HasCurrentVersion, Is.False);
            Assert.That(error.CurrentVersion, Is.Null);
        }

        [TestCase(KvReasons.CollectionFull)]
        [TestCase(KvReasons.OwnerFull)]
        public void AFullCollectionIsAConflictWithAReason(string reason)
        {
            var harness = new KvHarness();
            harness.Transport.Error(409, "conflict", reason: reason);

            var error = Assert.ThrowsAsync<KvStoreException>(
                async () => await harness.Profile.Mine.PutAsync("k", JsonValue.Of(1)));

            Assert.That(error!.IsFull, Is.True);
            Assert.That(error.Reason, Is.EqualTo(reason));
        }

        [Test]
        public void AConditionalWriteWithoutTheReadRightIsForbidden()
        {
            var harness = new KvHarness();
            harness.Transport.Error(403, "forbidden");

            var error = Assert.ThrowsAsync<KvStoreException>(
                async () => await harness.Client.Collection("inbox").PutAsync("k", JsonValue.Of(1), new KvPutOptions { IfNoneMatch = true }));

            Assert.That(error!.IsForbidden, Is.True);
        }

        [Test]
        public void TheWrongNamespaceIsABadRequestWithAReason()
        {
            var harness = new KvHarness();
            harness.Transport.Error(400, "bad_request", reason: KvReasons.WrongNamespace);

            var error = Assert.ThrowsAsync<KvStoreException>(
                async () => await harness.Profile.PutAsync("k", JsonValue.Of(1)));

            Assert.That(error!.Status, Is.EqualTo(400));
            Assert.That(error.Code, Is.EqualTo(KvErrorCodes.BadRequest));
            Assert.That(error.Reason, Is.EqualTo(KvReasons.WrongNamespace));
        }

        [Test]
        public async Task DeleteIsAPlainDelete()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(204);

            await harness.Profile.Mine.DeleteAsync("settings");

            var call = harness.Transport.LastCall;
            Assert.That(call.Method, Is.EqualTo("DELETE"));
            Assert.That(call.Url.AbsoluteUri, Is.EqualTo(KvHarness.BaseUrl + "/kv/profile/u/me/entries/settings"));
            Assert.That(call.Body, Is.Null);
            Assert.That(call.Header("If-Match"), Is.Null);
        }

        [Test]
        public async Task DeleteOfAMissingKeyIsDone()
        {
            var harness = new KvHarness();
            harness.Transport.Error(404, "not_found");

            await harness.Profile.Mine.DeleteAsync("settings");

            Assert.That(harness.Transport.Calls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task DeleteSendsIfMatch()
        {
            var harness = new KvHarness();
            harness.Transport.Reply(204);

            await harness.Profile.Mine.DeleteAsync("settings", new KvDeleteOptions { IfMatch = 9 });

            Assert.That(harness.Transport.LastCall.Header("If-Match"), Is.EqualTo("\"9\""));
        }

        [Test]
        public void DeleteRefusesAnIfMatchBelowOne()
        {
            var harness = new KvHarness();

            Assert.That(
                async () => await harness.Profile.Mine.DeleteAsync("k", new KvDeleteOptions { IfMatch = 0 }),
                Throws.ArgumentException);
            Assert.That(harness.Transport.Calls, Is.Empty);
        }

        [Test]
        public void DeleteSurfacesAConflict()
        {
            var harness = new KvHarness();
            harness.Transport.Error(409, "conflict", currentJson: "10");

            var error = Assert.ThrowsAsync<KvStoreException>(
                async () => await harness.Profile.Mine.DeleteAsync("k", new KvDeleteOptions { IfMatch = 9 }));

            Assert.That(error!.IsConflict, Is.True);
            Assert.That(error.CurrentVersion, Is.EqualTo(10));
        }
    }
}
