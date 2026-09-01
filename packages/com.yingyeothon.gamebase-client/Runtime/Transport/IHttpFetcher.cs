using System.Threading;
using System.Threading.Tasks;

namespace Yingyeothon.Gamebase.Client
{
    /// <summary>The result of a map fetch.</summary>
    public readonly struct HttpFetchResult
    {
        public HttpFetchResult(bool ok, int status, string text)
        {
            Ok = ok;
            Status = status;
            Text = text;
        }

        /// <summary>Whether the status was a success.</summary>
        public bool Ok { get; }

        /// <summary>The HTTP status code.</summary>
        public int Status { get; }

        /// <summary>The response body. Never log it: the URL came off the wire.</summary>
        public string Text { get; }
    }

    /// <summary>A credential-free HTTP GET.</summary>
    /// <remarks>
    /// Map assets are public and immutable, so the request carries no credentials and
    /// a new map version always arrives as a different URL in a later <c>hello</c>.
    /// Injectable because Unity WebGL has no <c>HttpClient</c>.
    /// </remarks>
    public interface IHttpFetcher
    {
        /// <summary>Fetches a public URL. Bound it: a timeout, a size cap and a small redirect budget.</summary>
        Task<HttpFetchResult> GetAsync(string url, CancellationToken cancellationToken);
    }
}
