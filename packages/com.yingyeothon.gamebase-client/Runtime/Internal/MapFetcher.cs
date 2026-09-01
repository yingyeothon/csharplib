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
            // One client for the process: a new HttpClient per request exhausts
            // sockets, and this one never carries credentials by construction.
            private static readonly HttpClient Client = new HttpClient();

            public async Task<HttpFetchResult> GetAsync(string url, CancellationToken cancellationToken)
            {
                using (var response = await Client.GetAsync(url, cancellationToken).ConfigureAwait(false))
                {
                    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return new HttpFetchResult(response.IsSuccessStatusCode, (int)response.StatusCode, text);
                }
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
            var source = new TaskCompletionSource<JsonValue>();
            lock (_gate)
            {
                if (_cache.TryGetValue(mapUrl, out var cached))
                {
                    return cached;
                }

                // Publish the entry before the load starts. A fetcher that answers
                // synchronously would otherwise finish — and evict — before the
                // assignment ran, putting the failed task back into the cache.
                _cache[mapUrl] = source.Task;
                _logger.Debug("fetching map", Json.Object().Set("mapUrl", mapUrl).Build());
            }

            _ = CompleteAsync(mapUrl, source, cancellationToken);
            return source.Task;
        }

        private async Task CompleteAsync(
            string mapUrl,
            TaskCompletionSource<JsonValue> source,
            CancellationToken cancellationToken)
        {
            try
            {
                var response = await _fetcher.GetAsync(mapUrl, cancellationToken).ConfigureAwait(false);
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
                source.SetResult(Json.TryParseBig(response.Text, MaxMapJsonLength, out var parsed, out _)
                    ? parsed
                    : JsonValue.Of(response.Text));
            }
            catch (Exception error)
            {
                lock (_gate)
                {
                    _cache.Remove(mapUrl);
                }

                // Evict first, then fail: a handler that retries immediately must not
                // be handed the failure it just saw.
                source.SetException(error);
            }
        }
    }
}
