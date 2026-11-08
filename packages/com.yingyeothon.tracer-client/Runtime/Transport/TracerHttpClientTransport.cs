using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Yingyeothon.Tracer
{
    /// <summary>The default <see cref="ITracerTransport"/>, over <see cref="HttpClient"/>.</summary>
    /// <remarks>
    /// Does not work on Unity WebGL, where <c>HttpClient</c> cannot send; a Unity build may
    /// pass <c>TracerUnityWebRequestTransport.Instance</c> through
    /// <see cref="TracerOptions.Transport"/> on every platform.
    /// </remarks>
    public static class TracerHttpClientTransport
    {
        /// <summary>
        /// One shared client for the process, with no default headers, no redirect following
        /// — a redirect would carry the write key to whatever host it named — and a timeout of
        /// its own of five minutes, a backstop behind the client's per-request deadline. The
        /// reply body is not read.
        /// </summary>
        public static ITracerTransport Default { get; } = new Transport(CreateClient(), true);

        /// <summary>
        /// A transport over a client the caller owns (a proxy, a certificate policy). Build its
        /// handler with <c>AllowAutoRedirect = false</c>: on a redirect to another host .NET
        /// drops <c>Authorization</c> but not <c>X-Sentry-Auth</c>, so the write key would
        /// follow. Its own <c>Timeout</c> also bounds every call.
        /// </summary>
        public static ITracerTransport Create(HttpClient client)
            => new Transport(client ?? throw new ArgumentNullException(nameof(client)), false);

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler();
            if (handler.SupportsRedirectConfiguration)
            {
                handler.AllowAutoRedirect = false;
            }

            return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        }

        private sealed class Transport : ITracerTransport
        {
            private readonly HttpClient _client;
            private readonly bool _shared;

            internal Transport(HttpClient client, bool shared)
            {
                _client = client;
                _shared = shared;
            }

            public async Task<TracerHttpResponse> SendAsync(TracerHttpRequest request, CancellationToken cancellationToken)
            {
                if (request == null)
                {
                    throw new ArgumentNullException(nameof(request));
                }

                using (var message = new HttpRequestMessage(HttpMethod.Post, request.Url))
                {
                    var content = new ByteArrayContent(request.Body);
                    message.Content = content;
                    foreach (var header in request.Headers)
                    {
                        if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                        {
                            content.Headers.ContentType = MediaTypeHeaderValue.Parse(header.Value);
                        }
                        else if (string.Equals(header.Key, "Content-Encoding", StringComparison.OrdinalIgnoreCase))
                        {
                            content.Headers.ContentEncoding.Add(header.Value);
                        }
                        else
                        {
                            message.Headers.TryAddWithoutValidation(header.Key, header.Value);
                        }
                    }

                    // ConfigureAwait(false) is right here: this never resumes a caller, and
                    // HttpClient never runs on WebGL.
                    using (var response = await _client
                        .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        string? retryAfter = null;
                        if (response.Headers.TryGetValues("Retry-After", out var values))
                        {
                            retryAfter = values.FirstOrDefault();
                        }

                        return new TracerHttpResponse((int)response.StatusCode, retryAfter);
                    }
                }
            }

            public override string ToString() => _shared ? "TracerHttpClientTransport.Default" : "TracerHttpClientTransport";
        }
    }
}
