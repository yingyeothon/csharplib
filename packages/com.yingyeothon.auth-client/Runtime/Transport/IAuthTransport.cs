using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Yingyeothon.Auth
{
    /// <summary>One HTTP request the auth client wants sent.</summary>
    /// <remarks>
    /// <b>Credentials cross this seam.</b> A <c>POST /token</c> body carries the provider's
    /// access or id token, and a <c>/verify</c> call an <c>Authorization: Bearer</c> header
    /// with the channel JWT. An implementation must never log a header value, a body or a
    /// reply body — log the method, the status and a byte count.
    /// </remarks>
    public sealed class AuthHttpRequest
    {
        public AuthHttpRequest(
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
            if (timeout <= TimeSpan.Zero || timeout > AuthClient.MaxTimeout)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "timeout must be positive and at most AuthClient.MaxTimeout");
            }

            Timeout = timeout;
        }

        /// <summary><c>GET</c> or <c>POST</c>.</summary>
        public string Method { get; }

        /// <summary>The absolute URL. Send <see cref="Uri.AbsoluteUri"/>, never <c>ToString()</c>, which unescapes.</summary>
        public Uri Url { get; }

        /// <summary>The request headers, in order. On <c>/verify</c> one of them is the credential.</summary>
        public IReadOnlyList<KeyValuePair<string, string>> Headers { get; }

        /// <summary>The UTF-8 JSON body, or null. On <c>/token</c> it holds the provider credential.</summary>
        public string? Body { get; }

        /// <summary>
        /// How long the client will wait. The cancellation token the transport receives fires
        /// at this bound too; this is for a transport whose own timer is the only one that can
        /// run — a WebGL build has no thread for the token's.
        /// </summary>
        public TimeSpan Timeout { get; }
    }

    /// <summary>What the service answered: the status and the body as text.</summary>
    public sealed class AuthHttpResponse
    {
        public AuthHttpResponse(int status, string body)
        {
            Status = status;
            Body = body ?? throw new ArgumentNullException(nameof(body));
        }

        /// <summary>The HTTP status code.</summary>
        public int Status { get; }

        /// <summary>The body decoded as UTF-8, or an empty string. It may hold a token; never log it.</summary>
        public string Body { get; }
    }

    /// <summary>The HTTP seam the auth client sends through.</summary>
    /// <remarks>
    /// Injectable because Unity WebGL has no working <c>HttpClient</c>; elsewhere
    /// <see cref="AuthHttpClientTransport.Default"/> is the right choice. Return every
    /// status as an <see cref="AuthHttpResponse"/> — a 4xx is an answer, not a failure —
    /// and throw only when no reply arrived at all; the throw becomes an
    /// <see cref="AuthException"/> with <see cref="AuthErrorCodes.Network"/>. Its message
    /// must name neither the URL nor a credential, because it becomes that exception's
    /// <c>InnerException</c>. Bound what is buffered: an auth answer is a few hundred bytes.
    /// </remarks>
    public interface IAuthTransport
    {
        /// <summary>Sends one request and returns whatever status the service answered with.</summary>
        Task<AuthHttpResponse> SendAsync(AuthHttpRequest request, CancellationToken cancellationToken);
    }
}
