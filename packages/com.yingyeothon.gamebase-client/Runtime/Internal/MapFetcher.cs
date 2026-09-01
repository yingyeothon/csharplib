using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;
using Yingyeothon.Logger;

namespace Yingyeothon.Gamebase.Client
{
    /// <summary>A map fetch that did not return 2xx.</summary>
    public sealed class MapFetchException : Exception
    {
        public MapFetchException(int status)
            : base("Map fetch failed with status " + status)
        {
            Status = status;
        }

        public int Status { get; }
    }

    /// <summary>The default <see cref="IHttpFetcher"/>, over one shared <see cref="HttpClient"/>.</summary>
    public static class HttpFetcher
    {
        public static IHttpFetcher Default { get; } = new HttpClientFetcher();

        private sealed class HttpClientFetcher : IHttpFetcher
        {
            /// <summary>
            /// The largest body this fetcher will buffer. It matches the map parser's
            /// own limit, so a body too large to parse is refused before it is a
            /// string rather than after.
            /// </summary>
            private const int MaxBodyBytes = 16 * 1024 * 1024;

            // One client for the process: a new HttpClient per request exhausts
            // sockets, and this one never carries credentials by construction.
            private static readonly HttpClient Client = CreateClient();

            public async Task<HttpFetchResult> GetAsync(string url, CancellationToken cancellationToken)
            {
                using (var response = await Client.GetAsync(url, cancellationToken).ConfigureAwait(false))
                {
                    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return new HttpFetchResult(response.IsSuccessStatusCode, (int)response.StatusCode, text);
                }
            }

            private static HttpClient CreateClient()
            {
                var handler = new HttpClientHandler();
                if (handler.SupportsRedirectConfiguration)
                {
                    // hello.mapUrl comes off the wire, so its redirect chain is
                    // chosen by whoever set the channel's map. A handful of hops is
                    // a CDN; the default fifty is a traversal budget.
                    handler.AllowAutoRedirect = true;
                    handler.MaxAutomaticRedirections = 5;
                }

                return new HttpClient(handler)
                {
                    // The defaults here are 100 seconds and two gigabytes, on a URL
                    // this SDK did not choose. A map is a small immutable public
                    // asset; a fetch that needs more than this is not one, and
                    // without a timeout a slow-loris host stalls MapAsync for a
                    // minute and a half with nothing the caller can do.
                    Timeout = TimeSpan.FromSeconds(30),
                    MaxResponseContentBufferSize = MaxBodyBytes,
                };
            }
        }
    }

    /// <summary>
    /// Fetches the immutable, public map asset named by <c>hello.mapUrl</c>.
    /// </summary>
    /// <remarks>
    /// The request carries no credentials, and a new map version always arrives as a
    /// different URL in a later <c>hello</c>, so a successful result is cached for
    /// the life of the fetcher. A failure is evicted so the next call retries.
    /// </remarks>
    internal sealed class MapFetcher
    {
        /// <summary>
        /// How much JSON a map asset may be. A map is a download rather than a frame,
        /// so it gets its own limit instead of the frame-sized default — but it still
        /// gets one, because the body comes from a URL the channel named.
        /// </summary>
        private const int MaxMapJsonLength = 16 * 1024 * 1024;

        private readonly Dictionary<string, Task<JsonValue>> _cache = new Dictionary<string, Task<JsonValue>>(StringComparer.Ordinal);
        private readonly object _gate = new object();
        private readonly IHttpFetcher _fetcher;
        private readonly ILogger _logger;

        internal MapFetcher(IHttpFetcher fetcher, ILogger logger)
        {
            _fetcher = fetcher;
            _logger = logger;
        }

        internal Task<JsonValue> FetchAsync(string mapUrl, CancellationToken cancellationToken)
        {
            Task<JsonValue> shared;
            TaskCompletionSource<JsonValue>? started = null;
            lock (_gate)
            {
                if (_cache.TryGetValue(mapUrl, out var cached))
                {
                    shared = cached;
                }
                else
                {
                    // Publish the entry before the load starts. A fetcher that answers
                    // synchronously would otherwise finish — and evict — before the
                    // assignment ran, putting the failed task back into the cache.
                    started = new TaskCompletionSource<JsonValue>();
                    _cache[mapUrl] = started.Task;
                    shared = started.Task;
                    _logger.Debug(
                        "fetching map",
                        Json.Object().Set("mapUrlLength", (double)mapUrl.Length).Build());
                }
            }

            if (started != null)
            {
                // No caller's token drives the shared work: whoever asked second would
                // otherwise be cancelled by whoever asked first, for a fetch it never
                // asked to cancel. The HttpClient timeout is what bounds it instead.
                _ = CompleteAsync(mapUrl, started);
            }

            return Observe(shared, cancellationToken);
        }

        /// <summary>
        /// Hands one caller its own view of a shared fetch, so its
        /// <see cref="CancellationToken"/> cancels its own await and nobody else's.
        /// </summary>
        private static Task<JsonValue> Observe(Task<JsonValue> shared, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled || shared.IsCompleted)
            {
                return shared;
            }

            var observer = new TaskCompletionSource<JsonValue>();
            var registration = cancellationToken.Register(() => observer.TrySetCanceled());
            shared.ContinueWith(
                task =>
                {
                    registration.Dispose();
                    if (task.IsFaulted)
                    {
                        observer.TrySetException(task.Exception!.InnerExceptions);
                    }
                    else if (task.IsCanceled)
                    {
                        observer.TrySetCanceled();
                    }
                    else
                    {
                        observer.TrySetResult(task.Result);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return observer.Task;
        }

        private async Task CompleteAsync(string mapUrl, TaskCompletionSource<JsonValue> source)
        {
            JsonValue result;
            try
            {
                var response = await _fetcher.GetAsync(mapUrl, CancellationToken.None).ConfigureAwait(false);
                if (!response.Ok)
                {
                    throw new MapFetchException(response.Status);
                }

                // A body that is not JSON is handed back as text rather than refused:
                // the asset is the game's, and the SDK only transports it.
                //
                // ParseBig, not Parse: a map asset is a download, not a frame, and
                // routinely runs to several megabytes. Under the frame-sized default
                // limit a large map would parse "unsuccessfully" and be handed back as
                // one enormous string, which no caller would notice until it tried to
                // read a field.
                if (Json.TryParseBig(response.Text, MaxMapJsonLength, out var parsed, out var failure))
                {
                    result = parsed;
                }
                else if (failure.Error == JsonParseError.InputTooLong)
                {
                    // Too big to parse must fail, not degrade. Handing back one
                    // enormous string is exactly the silent breakage this limit
                    // exists to prevent: no caller notices until it reads a field.
                    throw new MapFetchException(response.Status);
                }
                else
                {
                    result = JsonValue.Of(response.Text);
                }
            }
            catch (Exception error)
            {
                lock (_gate)
                {
                    _cache.Remove(mapUrl);
                }

                // Evict first, then fail: a handler that retries immediately must not
                // be handed the failure it just saw.
                source.TrySetException(error);
                return;
            }

            // Outside the try: settlement runs continuations inline, and a handler
            // that threw back into this frame used to evict a successful fetch and
            // then complete the source twice.
            source.TrySetResult(result);
        }
    }
}
