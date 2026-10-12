using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Yingyeothon.Codec;

namespace Yingyeothon.KvStore.Tests
{
    [TestFixture]
    public class KvServerScopeTests
    {
        [TestCase(KvScope.User, KvScope.Project, true, true)]
        [TestCase(KvScope.Project, KvScope.User, true, false)]
        [TestCase(KvScope.Project, KvScope.Server, false, false)]
        public void NamespaceDependsOnEitherScope(KvScope read, KvScope write, bool owned, bool mail)
        {
            var info = new KvCollectionInfo("id", "name", read, write, false, 10, 3);
            Assert.That(info.IsUserNamespace, Is.EqualTo(owned));
            Assert.That(info.IsPlayerMail, Is.EqualTo(mail));
            Assert.That((int)KvScope.User, Is.EqualTo(2));
        }

        [Test]
        public async Task TimeNeverSendsTheCredential()
        {
            var h = new KvHarness();
            h.Transport.Reply(200, "{\"epochMs\":1700000000123}");
            var time = await h.Client.ServerTimeAsync();
            Assert.That(time.ToUnixTimeMilliseconds(), Is.EqualTo(1700000000123L));
            Assert.That(h.Transport.LastCall.Url.AbsoluteUri, Is.EqualTo(KvHarness.BaseUrl + "/time"));
            Assert.That(h.Transport.LastCall.Header("Authorization"), Is.Null);
        }

        [TestCase("{}")]
        [TestCase("{\"epochMs\":0}")]
        [TestCase("{\"epochMs\":253402300800000}")]
        [TestCase("{\"epochMs\":1.5}")]
        public void MalformedTimeIsSafe(string body)
        {
            var h = new KvHarness();
            h.Transport.Reply(200, body);
            var error = Assert.ThrowsAsync<KvStoreException>(async () => await h.Client.ServerTimeAsync());
            Assert.That(error!.Code, Is.EqualTo(KvErrorCodes.BadBody));
        }

        [Test]
        public async Task BoundsArePerRequestAndEncodedAsIntegers()
        {
            var h = new KvHarness();
            h.Transport.Reply(200, "{\"value\":3,\"version\":2}");
            await h.Profile.Mine.IncrAsync("wallet", -2, new KvIncrOptions { Min = 0, Max = 10 });
            Assert.That(h.Transport.LastCall.Body, Is.EqualTo("{\"incr\":-2,\"min\":0,\"max\":10}"));
            h.Transport.Error(409, "conflict", "out_of_range");
            var error = Assert.ThrowsAsync<KvStoreException>(async () => await h.Profile.Mine.IncrAsync("wallet", -4, new KvIncrOptions { Min = 0 }));
            Assert.That(error!.Reason, Is.EqualTo(KvReasons.OutOfRange));
            Assert.That(error.Status, Is.EqualTo(409));
        }

        [TestCase(2L, 1L)]
        [TestCase(9007199254740992L, 9007199254740992L)]
        [TestCase(-9007199254740992L, 0L)]
        public void InvalidBoundsDoNotSend(long min, long max)
        {
            var h = new KvHarness();
            Assert.ThrowsAsync<ArgumentException>(async () => await h.Profile.Mine.IncrAsync("k", 1, new KvIncrOptions { Min = min, Max = max }));
            Assert.That(h.Transport.Calls, Is.Empty);
        }

        [TestCase("exists")]
        [TestCase("sender_full")]
        public void CrossOwnerMailUsesTheOwnedPathAndPreservesRefusals(string reason)
        {
            var h = new KvHarness();
            h.Transport.Error(409, "conflict", reason);
            var error = Assert.ThrowsAsync<KvStoreException>(async () => await h.Profile.Owner("party:recipient").PutAsync("party:sender:letter", JsonValue.Null));
            Assert.That(error!.Reason, Is.EqualTo(reason));
            Assert.That(h.Transport.LastCall.Url.AbsoluteUri, Does.Contain("/u/party:recipient/entries/party:sender:letter"));
            Assert.That(error.IsFull, Is.EqualTo(reason == "sender_full"));
        }

        [Test]
        public async Task WriterHeadersAndListStampAreRead()
        {
            var transport = new StampTransport();
            var client = KvStoreClient.Create(new KvStoreClientOptions { BaseUrl = KvHarness.BaseUrl, Token = KvHarness.Token, Transport = transport });
            var entry = await client.Collection("mail").Mine.GetEntryAsync("k");
            Assert.That(entry!.From, Is.EqualTo("server"));
            Assert.That(entry.UpdatedAt, Is.EqualTo(123));
            var page = await client.Collection("mail").Mine.ListAsync();
            Assert.That(page.Entries[0].From, Is.EqualTo("team"));
            transport.InvalidAt = true;
            Assert.That(Assert.ThrowsAsync<KvStoreException>(async () => await client.Collection("mail").Mine.GetEntryAsync("k"))!.Code, Is.EqualTo(KvErrorCodes.BadBody));
        }

        [TestCase(204, true)]
        [TestCase(404, false)]
        public async Task TryDeletePreservesExistenceForReaders(int status, bool expected)
        {
            var h = new KvHarness(); h.Transport.Reply(status);
            Assert.That(await h.Profile.Mine.TryDeleteAsync("k", new KvDeleteOptions { IfMatch = 1 }), Is.EqualTo(expected));
        }

        private sealed class StampTransport : IHttpTransport
        {
            internal bool InvalidAt;
            public Task<HttpReply> SendAsync(HttpCall call, CancellationToken cancellationToken)
            {
                var body = call.Url.AbsolutePath.EndsWith("/k", StringComparison.Ordinal) ? "null" : "{\"entries\":[{\"key\":\"k\",\"version\":1,\"bytes\":4,\"updatedAt\":123,\"from\":\"team\"}]}";
                return Task.FromResult(new HttpReply(200, new[] { new KeyValuePair<string, string>("etag", "1"), new KeyValuePair<string, string>("x-kv-from", "server"), new KeyValuePair<string, string>("x-kv-at", InvalidAt ? "bad" : "123") }, body));
            }
        }
    }
}
