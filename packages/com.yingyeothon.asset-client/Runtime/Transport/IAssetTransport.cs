using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Yingyeothon.Assets
{
    /// <summary>One request the asset client wants sent: a <c>GET</c> or a <c>HEAD</c>.</summary>
    /// <remarks>
    /// Nothing secret crosses this seam — the bundle is public ciphertext and no request
    /// carries a credential — but the body of a plain bundle is the game's own data and the
    /// path may be chosen by a manifest, so an implementation logs neither.
    /// </remarks>
    public sealed class AssetHttpRequest
    {
        public AssetHttpRequest(string method, Uri url, IReadOnlyList<KeyValuePair<string, string>> headers, TimeSpan responseTimeout)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            Url = url ?? throw new ArgumentNullException(nameof(url));
            Headers = headers ?? throw new ArgumentNullException(nameof(headers));
            ResponseTimeout = responseTimeout;
        }

        /// <summary><c>GET</c> or <c>HEAD</c>.</summary>
        public string Method { get; }

        /// <summary>The absolute URL. Send <see cref="Uri.AbsoluteUri"/>, never <c>ToString()</c>, which unescapes.</summary>
        public Uri Url { get; }

        /// <summary>
        /// The request headers, in order: <c>Range</c>, and outside CORS-safe mode
        /// <c>If-Range</c> and <c>Cache-Control</c>.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, string>> Headers { get; }

        /// <summary>
        /// How long the client waits for the response headers. The token the transport
        /// receives fires at this bound too; this is for a transport whose own timer is the
        /// only one that can run — a WebGL build has no thread for the token's.
        /// </summary>
        public TimeSpan ResponseTimeout { get; }
    }

    /// <summary>An answer whose body is read in pieces, at the caller's pace.</summary>
    public interface IAssetResponse : IDisposable
    {
        /// <summary>The HTTP status code.</summary>
        int Status { get; }

        /// <summary>
        /// A response header by name, matched case-insensitively, or null when it is absent
        /// or the platform cannot read it (in a browser only <c>ETag</c> and
        /// <c>Content-Length</c> are exposed by the yyt CDN).
        /// </summary>
        string? GetHeader(string name);

        /// <summary>
        /// Reads up to <paramref name="count"/> body bytes; 0 means the body ended. Throw when
        /// the transfer failed — the message must not name the URL.
        /// </summary>
        Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken);
    }

    /// <summary>The HTTP seam the asset client reads through.</summary>
    /// <remarks>
    /// Injectable because Unity WebGL has no working <c>HttpClient</c>; elsewhere
    /// <see cref="AssetHttpClientTransport.Default"/> is the right choice. Return every
    /// status as an <see cref="IAssetResponse"/> — a 403 is an answer — and throw only when
    /// no answer arrived; the throw becomes an <see cref="AssetClientException"/> with
    /// <see cref="AssetErrorCodes.Network"/>. Throw a <see cref="TimeoutException"/> when a
    /// timer of the transport's own ran out, so the client can tell a slow host from a
    /// refused request (which, in CORS-safe mode, it answers by reading the whole file). The client disposes every response, read to
    /// the end or not, and disposing one must abort its transfer.
    /// </remarks>
    public interface IAssetTransport
    {
        /// <summary>Sends one request and returns once its headers arrived.</summary>
        Task<IAssetResponse> SendAsync(AssetHttpRequest request, CancellationToken cancellationToken);
    }
}
