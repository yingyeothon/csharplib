using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Yingyeothon.KvStore
{
    /// <summary>One HTTP request the store client wants sent.</summary>
    /// <remarks>
    /// <b>The token crosses this seam.</b> <see cref="Headers"/> carries
    /// <c>Authorization: Bearer &lt;token&gt;</c> on every call, so an implementation
    /// must never log a header value, the URL with its query, or a body — log the
    /// method, the status and a byte count. The body is the game's own data.
    /// </remarks>
    public sealed class HttpCall
    {
        public HttpCall(
            string method,
            Uri url,
            IReadOnlyList<KeyValuePair<string, string>> headers,
            string? body,
            TimeSpan timeout)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            Url = url ?? throw new ArgumentNullException(nameof(url));
            Headers = headers ?? throw new ArgumentNullException(nameof(headers));
            Body = body;
            if (timeout <= TimeSpan.Zero || timeout > KvStoreClient.MaxTimeout)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "timeout must be positive and at most KvStoreClient.MaxTimeout");
            }

            Timeout = timeout;
        }

        /// <summary>The HTTP method, upper-case: <c>GET</c>, <c>PUT</c>, <c>PATCH</c> or <c>DELETE</c>.</summary>
        public string Method { get; }

        /// <summary>The absolute URL. Send <see cref="Uri.AbsoluteUri"/>, never <c>ToString()</c>, which unescapes.</summary>
        public Uri Url { get; }

        /// <summary>The request headers, in order. One of them is the credential.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Headers { get; }

        /// <summary>The UTF-8 body to send, or null for a request without one.</summary>
        public string? Body { get; }

        /// <summary>
        /// How long the client will wait for the reply. The cancellation token the
        /// transport receives fires at this bound too; this is for a transport whose
        /// own timer is the only one that can run — a WebGL build has no thread for
        /// the token's.
        /// </summary>
        public TimeSpan Timeout { get; }
    }

    /// <summary>What the server answered: status, headers and the body as text.</summary>
    public sealed class HttpReply
    {
        public HttpReply(int status, IReadOnlyList<KeyValuePair<string, string>> headers, string body)
        {
            Status = status;
            Headers = headers ?? throw new ArgumentNullException(nameof(headers));
            Body = body ?? throw new ArgumentNullException(nameof(body));
        }

        /// <summary>The HTTP status code.</summary>
        public int Status { get; }

        /// <summary>The response headers. Names are matched case-insensitively by the client.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Headers { get; }

        /// <summary>The body decoded as UTF-8, or an empty string. Never log it: it is a stored value.</summary>
        public string Body { get; }
    }

    /// <summary>The HTTP seam the store client sends through.</summary>
    /// <remarks>
    /// Injectable because Unity WebGL has no working <c>HttpClient</c>; on every other
    /// platform <see cref="HttpClientTransport.Default"/> is the right choice. An
    /// implementation returns every status as an <see cref="HttpReply"/> — a 4xx or a
    /// 5xx is an answer, not a failure — and throws only when no reply arrived at
    /// all. A throw becomes a <see cref="KvStoreException"/> with
    /// <see cref="KvErrorCodes.Network"/>; a cancellation of the token it was given
    /// surfaces as <see cref="System.OperationCanceledException"/>.
    /// </remarks>
    public interface IHttpTransport
    {
        /// <summary>
        /// Sends one request and returns whatever status the server answered with.
        /// <b>The credential is in <see cref="HttpCall.Headers"/></b>, and the URL
        /// carries a key: an exception thrown here becomes the
        /// <c>InnerException</c> of a <see cref="KvStoreException"/> and reaches whatever
        /// the game logs, so its message must name neither. Bound what is buffered: the largest
        /// reply the store sends is a page of a hundred 16 KiB values.
        /// </summary>
        Task<HttpReply> SendAsync(HttpCall call, CancellationToken cancellationToken);
    }
}
