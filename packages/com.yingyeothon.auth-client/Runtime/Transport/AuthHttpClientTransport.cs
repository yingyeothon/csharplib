using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Yingyeothon.Auth
{
    /// <summary>The default <see cref="IAuthTransport"/>, over <see cref="HttpClient"/>.</summary>
    /// <remarks>
    /// Does not work on Unity WebGL, where <c>HttpClient</c> cannot send; a WebGL build
    /// passes <c>AuthUnityWebRequestTransport.Instance</c> through
    /// <see cref="AuthClientOptions.Transport"/> instead.
    /// </remarks>
    public static class AuthHttpClientTransport
    {
        /// <summary>
        /// One shared client for the process, with no default headers, no redirect
        /// following — a redirect would carry the credential to whatever host it named — and a
        /// timeout of its own of five minutes, a backstop behind
        /// <see cref="AuthClientOptions.Timeout"/> for a stream that does not honour
        /// cancellation. A reply over 1 MiB is refused as it streams, and the call fails as
        /// <c>network</c>.
        /// </summary>
        public static IAuthTransport Default { get; } = new Transport(CreateClient(), true);

        /// <summary>
        /// A transport over a client the caller owns: a proxy, a certificate policy. It should
        /// not follow redirects, and its own <c>Timeout</c> (100 seconds unless changed) also
        /// bounds every call. Replies are read with the same 1 MiB cap.
        /// </summary>
        public static IAuthTransport Create(HttpClient client)
            => new Transport(client ?? throw new ArgumentNullException(nameof(client)), false);

        private const int MaxReplyBytes = 1024 * 1024;

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler();
            if (handler.SupportsRedirectConfiguration)
            {
                handler.AllowAutoRedirect = false;
            }

            return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        }

        private sealed class Transport : IAuthTransport
        {
            private readonly HttpClient _client;
            private readonly bool _shared;

            internal Transport(HttpClient client, bool shared)
            {
                _client = client;
                _shared = shared;
            }

            public async Task<AuthHttpResponse> SendAsync(AuthHttpRequest request, CancellationToken cancellationToken)
            {
                if (request == null)
                {
                    throw new ArgumentNullException(nameof(request));
                }

                using (var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url))
                {
                    if (request.Body != null)
                    {
                        message.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
                    }

                    foreach (var header in request.Headers)
                    {
                        if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        message.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }

                    // ConfigureAwait(false) is right here: this never resumes a caller.
                    using (var response = await _client
                        .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        var body = response.Content == null
                            ? string.Empty
                            : await ReadBoundedAsync(response.Content, cancellationToken).ConfigureAwait(false);
                        return new AuthHttpResponse((int)response.StatusCode, body);
                    }
                }
            }

            /// <summary>Reads the body as UTF-8, refusing it once it passes the cap rather than after buffering it.</summary>
            private static async Task<string> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
            {
                using (var stream = await content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var buffer = new MemoryStream())
                {
                    var chunk = new byte[16 * 1024];
                    int read;
                    while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        if (buffer.Length + read > MaxReplyBytes)
                        {
                            throw new IOException("auth transport refused a reply over " + MaxReplyBytes + " bytes");
                        }

                        buffer.Write(chunk, 0, read);
                    }

                    return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
                }
            }

            public override string ToString() => _shared ? "AuthHttpClientTransport.Default" : "AuthHttpClientTransport";
        }
    }
}
