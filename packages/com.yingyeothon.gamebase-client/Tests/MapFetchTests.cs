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

    /// <summary>A fetcher the test completes by hand, so a fetch can be in flight.</summary>
    internal sealed class GatedHttpFetcher : IHttpFetcher
    {
        private readonly TaskCompletionSource<HttpFetchResult> _pending =
            new TaskCompletionSource<HttpFetchResult>();

        internal int Calls { get; private set; }

        internal void Complete(HttpFetchResult response) => _pending.TrySetResult(response);

        public Task<HttpFetchResult> GetAsync(string url, CancellationToken cancellationToken)
        {
            Calls++;
            return _pending.Task;
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

        /// <remarks>
        /// FetchAsync returned the cached task and discarded the second caller's
        /// token, so only the first caller's token was wired into the fetch: a scene
        /// loader cancelling its own MapAsync threw OperationCanceledException at a
        /// HUD that had passed no token at all — and the entry was already evicted, so
        /// the failure was not even reproducible.
        /// </remarks>
        [Test]
        public async Task OneCallersCancellationDoesNotCancelAnothersSharedFetch()
        {
            var fetcher = new GatedHttpFetcher();
            var harness = new LobbyHarness(o => o.HttpFetcher = fetcher);
            await harness.ConnectAsync();

            using var scene = new CancellationTokenSource();
            var sceneMap = harness.Client.MapAsync(scene.Token);
            var hudMap = harness.Client.MapAsync();

            Assert.That(fetcher.Calls, Is.EqualTo(1), "the fetch is shared");

            scene.Cancel();

            Assert.That(async () => await sceneMap, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(hudMap.IsCompleted, Is.False);

            fetcher.Complete(new HttpFetchResult(true, 200, "{\"zones\":[\"town\"]}"));

            var map = await hudMap;

            Assert.That(map.GetMemberOrNull("zones"), Is.Not.Null);
            Assert.That(fetcher.Calls, Is.EqualTo(1));
        }

        /// <remarks>
        /// A body too large to parse used to fall through to JsonValue.Of(text) — one
        /// enormous string pretending to be a map, which no caller notices until it
        /// reads a field. That is the exact breakage the limit exists to prevent.
        /// </remarks>
        [Test]
        public async Task ABodyTooLargeToParseFailsInsteadOfDegradingToAString()
        {
            var fetcher = new FakeHttpFetcher();
            fetcher.Enqueue(new HttpFetchResult(true, 200, "[" + new string('0', 17 * 1024 * 1024) + "]"));
            var harness = new LobbyHarness(o => o.HttpFetcher = fetcher);
            await harness.ConnectAsync();

            Assert.That(async () => await harness.Client.MapAsync(), Throws.InstanceOf<MapFetchException>());

            // Positive control: a body that is merely not JSON is still handed back as
            // text, because the asset is the game's and the SDK only transports it.
            fetcher.Enqueue(new HttpFetchResult(true, 200, "not json at all"));
            var text = await harness.Client.MapAsync();

            Assert.That(text.Kind, Is.EqualTo(JsonKind.String));
        }

        // ---- no thread but the caller's --------------------------------------------------

        /// <summary>
        /// A context the test pumps itself, counting every post that arrives from another
        /// thread: dotnet has a thread pool to finish work the caller never pumps, a WebGL
        /// player has none, so work that leaves the caller's thread is exactly what hangs there.
        /// </summary>
        private sealed class PumpedContext : SynchronizationContext
        {
            private readonly System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback, object?)> _queue =
                new System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback, object?)>();

            private readonly int _thread = Environment.CurrentManagedThreadId;

            internal int ForeignPosts;

            public override void Post(SendOrPostCallback d, object? state)
            {
                if (Environment.CurrentManagedThreadId != _thread)
                {
                    Interlocked.Increment(ref ForeignPosts);
                }

                _queue.Enqueue((d, state));
            }

            internal void Pump()
            {
                while (_queue.TryDequeue(out var item))
                {
                    item.Item1(item.Item2);
                }
            }
        }

        private static void WithContext(PumpedContext context, Action body)
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                body();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        /// <summary>
        /// A fetcher the test completes by hand on the caller's thread, either plainly or with
        /// <c>RunContinuationsAsynchronously</c> as the Unity transports complete.
        /// </summary>
        private sealed class AsynchronousHttpFetcher : IHttpFetcher
        {
            private readonly TaskCompletionSource<HttpFetchResult> _pending;

            internal AsynchronousHttpFetcher(bool runContinuationsAsynchronously)
                => _pending = new TaskCompletionSource<HttpFetchResult>(
                    runContinuationsAsynchronously ? TaskCreationOptions.RunContinuationsAsynchronously : TaskCreationOptions.None);

            internal void Complete(HttpFetchResult response) => _pending.SetResult(response);

            public Task<HttpFetchResult> GetAsync(string url, CancellationToken cancellationToken) => _pending.Task;
        }

        /// <remarks>
        /// With <c>ConfigureAwait(false)</c> the rest of the fetch went to the thread pool
        /// either way — the runtime does not inline such a continuation under a
        /// synchronization context — and a WebGL player has no pool, so <c>MapAsync</c>
        /// never finished there.
        /// </remarks>
        [TestCase(false)]
        [TestCase(true)]
        public void AFetchAnsweredLaterFinishesOnTheCallersContext(bool runContinuationsAsynchronously)
        {
            var fetcher = new AsynchronousHttpFetcher(runContinuationsAsynchronously);
            var harness = new LobbyHarness(o => o.HttpFetcher = fetcher);
            Assert.That(harness.ConnectAsync().IsCompleted, Is.True);
            var context = new PumpedContext();
            var caller = Environment.CurrentManagedThreadId;
            var settledOn = 0;

            WithContext(context, () =>
            {
                var map = harness.Client.MapAsync();
                map.ContinueWith(
                    _ => settledOn = Environment.CurrentManagedThreadId,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

                fetcher.Complete(new HttpFetchResult(true, 200, "{\"ok\":true}"));
                for (var i = 0; i < 200 && !map.IsCompleted; i++)
                {
                    context.Pump();
                    Thread.Sleep(1); // time for a thread pool, if the fetch went to one
                }

                Assert.That(map.IsCompleted, Is.True);
                Assert.That(map.Result.GetBool("ok"), Is.True);
            });

            Assert.That(settledOn, Is.EqualTo(caller));
            Assert.That(context.ForeignPosts, Is.EqualTo(0));
        }
    }
}
