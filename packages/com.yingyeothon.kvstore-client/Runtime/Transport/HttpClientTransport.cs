using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Yingyeothon.KvStore
{
    /// <summary>The default <see cref="IHttpTransport"/>, over <see cref="HttpClient"/>.</summary>
    /// <remarks>
    /// Does not work on Unity WebGL, where <c>HttpClient</c> cannot send; a WebGL build
    /// passes <c>UnityWebRequestTransport</c> through
    /// <see cref="KvStoreClientOptions.Transport"/> instead.
    /// </remarks>
    public static class HttpClientTransport
    {
        /// <summary>
        /// One shared client for the process, with no default headers and no redirect
        /// following: a redirect would carry the credential to whatever host it named.
        /// Its own timeout is 100 seconds; the per-call bound is
        /// <see cref="KvStoreClientOptions.Timeout"/>.
        /// </summary>
        public static IHttpTransport Default { get; } = new Transport(CreateClient(), true);

        /// <summary>
        /// A transport over a client the caller owns and configures — a custom handler,
        /// a proxy, a certificate policy. The client must set no <c>Authorization</c>
        /// header of its own, should not follow redirects, and should set
        /// <c>MaxResponseContentBufferSize</c>: the default is two gigabytes, and the
        /// store never sends more than a few megabytes.
        /// </summary>
        public static IHttpTransport Create(HttpClient client)
            => new Transport(client ?? throw new ArgumentNullException(nameof(client)), false);

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler();
            if (handler.SupportsRedirectConfiguration)
            {
                handler.AllowAutoRedirect = false;
            }

            return new HttpClient(handler)
            {
                // The largest value is 16 KiB and a page is at most a hundred of them,
                // so a body past this is not the store talking.
                MaxResponseContentBufferSize = 4 * 1024 * 1024,
            };
        }

        private sealed class Transport : IHttpTransport
        {
            private readonly HttpClient _client;
            private readonly bool _shared;

            internal Transport(HttpClient client, bool shared)
            {
                _client = client;
                _shared = shared;
            }

            public async Task<HttpReply> SendAsync(HttpCall call, CancellationToken cancellationToken)
            {
                if (call == null)
                {
                    throw new ArgumentNullException(nameof(call));
                }

                using (var request = new HttpRequestMessage(new HttpMethod(call.Method), call.Url))
                {
                    if (call.Body != null)
                    {
                        // The content type travels with the content in HttpClient's
                        // model, so it is peeled off the header list here.
                        request.Content = new StringContent(call.Body, Encoding.UTF8, "application/json");
                    }

                    foreach (var header in call.Headers)
                    {
                        if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }

                    // ConfigureAwait(false) is right here and nowhere the caller awaits:
                    // this loop never resumes a caller; KvStoreClient's own awaits do.
                    using (var response = await _client
                        .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        var body = response.Content == null
                            ? string.Empty
                            : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var headers = new List<KeyValuePair<string, string>>();
                        Collect(headers, response.Headers);
                        if (response.Content != null)
                        {
                            Collect(headers, response.Content.Headers);
                        }

                        return new HttpReply((int)response.StatusCode, headers, body);
                    }
                }
            }

            private static void Collect(
                List<KeyValuePair<string, string>> into,
                IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
            {
                foreach (var header in headers)
                {
                    foreach (var value in header.Value)
                    {
                        into.Add(new KeyValuePair<string, string>(header.Key, value));
                    }
                }
            }

            public override string ToString() => _shared ? "HttpClientTransport.Default" : "HttpClientTransport";
        }
    }
}
