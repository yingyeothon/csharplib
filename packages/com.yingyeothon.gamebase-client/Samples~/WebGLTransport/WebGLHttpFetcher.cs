#if UNITY_5_3_OR_NEWER
#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Yingyeothon.Gamebase.Client.Samples
{
    /// <summary>
    /// A credential-free GET over <c>UnityWebRequest</c>, for <c>MapAsync</c> in a WebGL
    /// build, where <c>HttpFetcher.Default</c> cannot run. It works natively too, but there
    /// the default parses the map off the main thread and this one does not.
    /// </summary>
    /// <remarks>
    /// The map asset is public and immutable, so the request sets no header — adding one
    /// would send the token to a CDN. (What the platform adds on its own, such as a cookie
    /// an earlier request to the same host was given, is the platform's.) The URL still
    /// comes off the wire, so it is bounded:
    /// <list type="bullet">
    /// <item>an absolute <c>http</c> or <c>https</c> URL with no user info, or the task
    /// faults with <see cref="ArgumentException"/>;</item>
    /// <item>30 seconds, as <c>UnityWebRequest.timeout</c> — the one timer a WebGL player
    /// runs;</item>
    /// <item>no redirects (<c>redirectLimit = 0</c>), the one redirect bound a browser
    /// applies. The console pins a map URL to the platform's CDN, which the asset
    /// client reads under the same limit;</item>
    /// <item>16 MB, checked once the reply has arrived — a browser buffers it whole
    /// first, so this bounds what reaches the parser, not memory. It faults with
    /// <see cref="MapFetchException"/> carrying the status.</item>
    /// </list>
    /// A status answered without a redirect comes back as it is. A connection or data
    /// failure, a timeout or a redirect faults with an <see cref="IOException"/> that names no URL —
    /// natively too, where the Runtime transports hand a refused redirect back as its
    /// <c>3xx</c>. An exception Unity throws while starting the request faults the task
    /// unchanged: <c>UnityWebRequest</c> is main-thread only, and <c>MapAsync</c> calls
    /// this on the thread you call <c>MapAsync</c> from. The task completes with
    /// <c>RunContinuationsAsynchronously</c>, as the Runtime transports' do, so await it
    /// plainly: in a WebGL player a <c>Task.WhenAny</c> over it never completes. The SDK
    /// passes no cancellation token; a token passed to <c>GetAsync</c> directly is checked
    /// before the request starts and when it ends, and never aborts it.
    /// </remarks>
    public sealed class WebGLHttpFetcher : IHttpFetcher
    {
        /// <summary>The map parser's own limit, as the default fetcher uses.</summary>
        private const long MaxBodyBytes = 16 * 1024 * 1024;

        public Task<HttpFetchResult> GetAsync(string url, CancellationToken cancellationToken)
        {
            var source = new TaskCompletionSource<HttpFetchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (cancellationToken.IsCancellationRequested)
            {
                source.TrySetCanceled(cancellationToken);
                return source.Task;
            }

            // UnityWebRequest also reads file: and jar: URLs, which a map named on the
            // wire must never reach, and sends user info as a credential.
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || uri.UserInfo.Length > 0)
            {
                source.TrySetException(new ArgumentException("map URL must be absolute http or https, with no user info"));
                return source.Task;
            }

            UnityWebRequest? request = null;
            try
            {
                request = UnityWebRequest.Get(uri.AbsoluteUri);
                request.timeout = 30;
                request.redirectLimit = 0;
                request.SendWebRequest().completed += _ => Complete(request, source, cancellationToken);
            }
            catch (Exception error)
            {
                // UnityWebRequest throws here off the main thread, among other places.
                // The caller gets a faulted task either way, never a throw.
                request?.Dispose();
                source.TrySetException(error);
            }

            return source.Task;
        }

        private static void Complete(UnityWebRequest request, TaskCompletionSource<HttpFetchResult> source, CancellationToken cancellationToken)
        {
            try
            {
                var status = (int)request.responseCode;
                if (cancellationToken.IsCancellationRequested)
                {
                    source.TrySetCanceled(cancellationToken);
                }
                else if (request.result == UnityWebRequest.Result.ConnectionError
                    || request.result == UnityWebRequest.Result.DataProcessingError)
                {
                    // Not request.error: it can quote the URL. The kind is enough.
                    source.TrySetException(new IOException("map fetch failed: " + request.result));
                }
                else if (request.downloadedBytes > MaxBodyBytes)
                {
                    source.TrySetException(new MapFetchException(status));
                }
                else
                {
                    var ok = request.result == UnityWebRequest.Result.Success && status >= 200 && status < 300;
                    source.TrySetResult(new HttpFetchResult(ok, status, request.downloadHandler.text ?? string.Empty));
                }
            }
            catch (Exception error)
            {
                source.TrySetException(error);
            }
            finally
            {
                request.Dispose();
            }
        }
    }
}
#endif
