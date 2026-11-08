using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Yingyeothon.Tracer
{
    /// <summary>One envelope <c>POST</c> the tracer client wants sent.</summary>
    /// <remarks>
    /// <b>A credential crosses this seam.</b> The <c>X-Sentry-Auth</c> header carries the
    /// resource's write key, and the body is the game's own debug data. An implementation
    /// must never log a header value or the body — log the status and a byte count.
    /// </remarks>
    public sealed class TracerHttpRequest
    {
        /// <summary>A request; the client builds these, a transport only reads them.</summary>
        public TracerHttpRequest(Uri url, IReadOnlyList<KeyValuePair<string, string>> headers, byte[] body, TimeSpan timeout)
        {
            Url = url ?? throw new ArgumentNullException(nameof(url));
            Headers = headers ?? throw new ArgumentNullException(nameof(headers));
            Body = body ?? throw new ArgumentNullException(nameof(body));
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "timeout must be positive");
            }

            Timeout = timeout;
        }

        /// <summary>The envelope endpoint. Send <see cref="Uri.AbsoluteUri"/>, never <c>ToString()</c>, which unescapes.</summary>
        public Uri Url { get; }

        /// <summary>
        /// <c>Content-Type</c>, <c>X-Sentry-Auth</c> (the credential) and, when the body is
        /// gzipped, <c>Content-Encoding</c>, in that order.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, string>> Headers { get; }

        /// <summary>The envelope bytes as they go on the wire.</summary>
        public byte[] Body { get; }

        /// <summary>
        /// How long the client will wait. The cancellation token fires at this bound too; this
        /// is for a transport whose own timer is the only one that can run (WebGL).
        /// </summary>
        public TimeSpan Timeout { get; }
    }

    /// <summary>What the ingest answered. The body (<c>{"id"}</c> or <c>{"detail"}</c>) is never needed.</summary>
    public sealed class TracerHttpResponse
    {
        /// <summary>An answer.</summary>
        public TracerHttpResponse(int status, string? retryAfter)
        {
            Status = status;
            RetryAfter = retryAfter;
        }

        /// <summary>The HTTP status code.</summary>
        public int Status { get; }

        /// <summary>The <c>Retry-After</c> header as sent, or null.</summary>
        public string? RetryAfter { get; }
    }

    /// <summary>The HTTP seam the tracer client sends through.</summary>
    /// <remarks>
    /// Return every status as a <see cref="TracerHttpResponse"/> — a 4xx is an answer, not a
    /// failure — and throw (or fault the task) only when no answer arrived at all; the
    /// client then backs off and tries again. Do not follow redirects: one would carry the
    /// write key to whatever host it named. The client calls it from
    /// <see cref="ITracerClient.Poll"/>, so a Unity transport is called on the main thread.
    /// </remarks>
    public interface ITracerTransport
    {
        /// <summary>Sends one envelope and returns whatever status the ingest answered with.</summary>
        Task<TracerHttpResponse> SendAsync(TracerHttpRequest request, CancellationToken cancellationToken);
    }
}
