using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Yingyeothon.Assets
{
    /// <summary>The default <see cref="IAssetTransport"/>, over <see cref="HttpClient"/>, streaming each body.</summary>
    /// <remarks>
    /// Does not work on Unity WebGL, where <c>HttpClient</c> cannot send; a WebGL build
    /// passes <c>AssetUnityWebRequestTransport.Instance</c> through
    /// <see cref="AssetBundleClientOptions.Transport"/> instead.
    /// </remarks>
    public static class AssetHttpClientTransport
    {
        /// <summary>
        /// One shared client for the process: no decompression (a stated length must be the
        /// bytes that arrive), no redirects, and no timeout of its own — the asset client
        /// bounds the wait for headers and for each piece of a body itself.
        /// </summary>
        public static IAssetTransport Default { get; } = new Transport(CreateClient(), true);

        /// <summary>A transport over a client the caller owns: a proxy, a certificate policy.</summary>
        public static IAssetTransport Create(HttpClient client)
            => new Transport(client ?? throw new ArgumentNullException(nameof(client)), false);

        private static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler();
            if (handler.SupportsRedirectConfiguration)
            {
                handler.AllowAutoRedirect = false;
            }

            return new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        }

        private sealed class Transport : IAssetTransport
        {
            private readonly HttpClient _client;
            private readonly bool _shared;

            internal Transport(HttpClient client, bool shared)
            {
                _client = client;
                _shared = shared;
            }

            public async Task<IAssetResponse> SendAsync(AssetHttpRequest request, CancellationToken cancellationToken)
            {
                if (request == null)
                {
                    throw new ArgumentNullException(nameof(request));
                }

                var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);
                foreach (var header in request.Headers)
                {
                    message.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                HttpResponseMessage response;
                try
                {
                    // ConfigureAwait(false) is right here: this never resumes a caller.
                    response = await _client
                        .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch
                {
                    message.Dispose();
                    throw;
                }

                try
                {
                    var body = response.Content == null
                        ? Stream.Null
                        : await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    return new Response(message, response, body);
                }
                catch
                {
                    response.Dispose();
                    message.Dispose();
                    throw;
                }
            }

            public override string ToString() => _shared ? "AssetHttpClientTransport.Default" : "AssetHttpClientTransport";
        }

        private sealed class Response : IAssetResponse
        {
            private readonly HttpRequestMessage _request;
            private readonly HttpResponseMessage _response;
            private readonly Stream _body;

            internal Response(HttpRequestMessage request, HttpResponseMessage response, Stream body)
            {
                _request = request;
                _response = response;
                _body = body;
            }

            public int Status => (int)_response.StatusCode;

            public string? GetHeader(string name)
            {
                if (_response.Headers.TryGetValues(name, out var values)
                    || (_response.Content != null && _response.Content.Headers.TryGetValues(name, out values)))
                {
                    return string.Join(", ", values.ToArray());
                }

                return null;
            }

            public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => _body.ReadAsync(buffer, offset, count, cancellationToken);

            public void Dispose()
            {
                _body.Dispose();
                _response.Dispose();
                _request.Dispose();
            }
        }
    }
}
