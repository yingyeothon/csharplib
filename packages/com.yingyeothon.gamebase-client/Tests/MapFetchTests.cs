using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Yingyeothon.Codec;

namespace Yingyeothon.Gamebase.Client.Tests
{
    internal sealed class FakeHttpFetcher : IHttpFetcher
    {
        private readonly Queue<HttpFetchResult> _responses = new Queue<HttpFetchResult>();

        internal List<string> Requested { get; } = new List<string>();

        internal void Enqueue(HttpFetchResult response) => _responses.Enqueue(response);

        public Task<HttpFetchResult> GetAsync(string url, CancellationToken cancellationToken)
        {
            Requested.Add(url);
            return Task.FromResult(_responses.Count > 0
                ? _responses.Dequeue()
                : new HttpFetchResult(true, 200, "{}"));
        }
    }

    [TestFixture]
    public class MapFetchTests
    {
        [Test]
        public async Task FetchesTheMapFromHelloOnceAndCachesIt()
        {
            var fetcher = new FakeHttpFetcher();
            fetcher.Enqueue(new HttpFetchResult(true, 200, "{\"zones\":[\"town\"]}"));
            var harness = new LobbyHarness(o => o.HttpFetcher = fetcher);
            await harness.ConnectAsync();

            var first = await harness.Client.MapAsync();
            var second = await harness.Client.MapAsync();

            Assert.That(fetcher.Requested, Is.EqualTo(new[] { "https://cdn/map/v1.json" }));
            Assert.That(first.GetArrayOrEmpty("zones")[0].AsString(), Is.EqualTo("town"));
            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public async Task ABodyThatIsNotJsonComesBackAsText()
        {
            var fetcher = new FakeHttpFetcher();
            fetcher.Enqueue(new HttpFetchResult(true, 200, "plain text map"));
            var harness = new LobbyHarness(o => o.HttpFetcher = fetcher);
            await harness.ConnectAsync();

            var map = await harness.Client.MapAsync();

            Assert.That(map.Kind, Is.EqualTo(JsonKind.String));
            Assert.That(map.AsString(), Is.EqualTo("plain text map"));
        }

        [Test]
        public async Task ANonSuccessStatusFailsAndTheNextCallRetries()
        {
            var fetcher = new FakeHttpFetcher();
            fetcher.Enqueue(new HttpFetchResult(false, 503, string.Empty));
            fetcher.Enqueue(new HttpFetchResult(true, 200, "{\"ok\":true}"));
            var harness = new LobbyHarness(o => o.HttpFetcher = fetcher);
            await harness.ConnectAsync();

            var error = Assert.ThrowsAsync<MapFetchException>(async () => await harness.Client.MapAsync());
            Assert.That(error!.Status, Is.EqualTo(503));

            // The failure was evicted from the cache, so this is a fresh attempt.
            var map = await harness.Client.MapAsync();

            Assert.That(map.GetBool("ok"), Is.True);
            Assert.That(fetcher.Requested, Has.Count.EqualTo(2));
        }

        [Test]
        public void MapAsyncFailsBeforeHello()
        {
            var harness = new LobbyHarness(o => o.HttpFetcher = new FakeHttpFetcher());

            Assert.ThrowsAsync<InvalidOperationException>(async () => await harness.Client.MapAsync());
        }

        [Test]
        public async Task ConcurrentCallsShareOneRequest()
        {
            var fetcher = new FakeHttpFetcher();
            fetcher.Enqueue(new HttpFetchResult(true, 200, "{}"));
            var harness = new LobbyHarness(o => o.HttpFetcher = fetcher);
            await harness.ConnectAsync();

            var first = harness.Client.MapAsync();
            var second = harness.Client.MapAsync();
            await Task.WhenAll(first, second);

            Assert.That(fetcher.Requested, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task TheMapCacheSurvivesAReconnect()
        {
            // Map assets are immutable and a new version is a new URL, so the cache is
            // not something a reconnect should throw away.
            var fetcher = new FakeHttpFetcher();
            var harness = new LobbyHarness(o => o.HttpFetcher = fetcher);
            await harness.ConnectAsync();
            await harness.Client.MapAsync();

            harness.Socket.ServerClose(4002);
            harness.Poll();
            harness.Advance(500);
            harness.Socket.ServerOpen();
            harness.Socket.ServerSend(Frames.Hello());
            harness.Poll();
            await harness.Client.MapAsync();

            Assert.That(fetcher.Requested, Has.Count.EqualTo(1));
        }
    }
}
