#if UNITY_5_3_OR_NEWER
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Yingyeothon.Tracer
{
    /// <summary>
    /// An <see cref="ITracerTransport"/> over <c>UnityWebRequest</c>. It works on every Unity
    /// platform, so a build may use it unconditionally; it is the only one a WebGL player
    /// can construct.
    /// </summary>
    /// <remarks>
    /// The client calls it from <c>Poll()</c>, which is the main thread in a Unity game, as
    /// <c>UnityWebRequest</c> requires. The call's bound is applied as
    /// <c>UnityWebRequest.timeout</c> (the only timer WebGL can run) and by the cancellation
    /// token, whose abort is posted back to the main thread. Native players refuse
    /// redirects (<c>redirectLimit = 0</c>); a browser may follow one regardless, with the
    /// header. A connection or data failure throws a message naming neither the URL nor a
    /// header.
    /// A browser player cannot reach tracer today: the ingest answers no CORS preflight,
    /// and <c>X-Sentry-Auth</c> forces one.
    /// </remarks>
    public sealed class TracerUnityWebRequestTransport : ITracerTransport
    {
        /// <summary>The shared instance; it holds no state.</summary>
        public static readonly ITracerTransport Instance = new TracerUnityWebRequestTransport();

        private TracerUnityWebRequestTransport()
        {
        }

        public Task<TracerHttpResponse> SendAsync(TracerHttpRequest call, CancellationToken cancellationToken)
        {
            if (call == null)
            {
                throw new ArgumentNullException(nameof(call));
            }

            var source = new TaskCompletionSource<TracerHttpResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (cancellationToken.IsCancellationRequested)
            {
                source.TrySetCanceled(cancellationToken);
                return source.Task;
            }

            var request = new UnityWebRequest(call.Url.AbsoluteUri, "POST");
            var finished = false;
            try
            {
                // The answer's body ({"id"} or {"detail"}) is never needed, so none is buffered.
                request.downloadHandler = null;
                request.uploadHandler = new UploadHandlerRaw(call.Body);
                foreach (var header in call.Headers)
                {
                    request.SetRequestHeader(header.Key, header.Value);
                }

                // A redirect would carry the write key to whatever host it named.
                request.redirectLimit = 0;

                // Seconds, rounded up, never zero: zero means no timeout to Unity.
                request.timeout = (int)Math.Max(1, Math.Ceiling(call.Timeout.TotalSeconds));
            }
            catch (Exception)
            {
                request.Dispose();
                throw;
            }

            var context = SynchronizationContext.Current;
            var registration = default(CancellationTokenRegistration);
            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(() =>
                {
                    if (context != null)
                    {
                        context.Post(_ => AbortIfPending(request, ref finished), null);
                    }
                    else
                    {
                        AbortIfPending(request, ref finished);
                    }
                });
            }

            request.SendWebRequest().completed += _ =>
            {
                finished = true;
                registration.Dispose();
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        source.TrySetCanceled(cancellationToken);
                        return;
                    }

                    // A refused redirect is a ConnectionError that still carries its 3xx;
                    // hand it back as the answer it is.
                    var redirected = request.result == UnityWebRequest.Result.ConnectionError
                        && request.responseCode >= 300 && request.responseCode < 400;
                    if (!redirected
                        && (request.result == UnityWebRequest.Result.ConnectionError
                            || request.result == UnityWebRequest.Result.DataProcessingError))
                    {
                        // Not request.error: it can quote the URL. The kind is enough.
                        source.TrySetException(new IOException("tracer transport failed: " + request.result));
                        return;
                    }

                    source.TrySetResult(new TracerHttpResponse((int)request.responseCode, request.GetResponseHeader("Retry-After")));
                }
                catch (Exception error)
                {
                    source.TrySetException(error);
                }
                finally
                {
                    request.Dispose();
                }
            };

            return source.Task;
        }

        private static void AbortIfPending(UnityWebRequest request, ref bool finished)
        {
            if (!finished)
            {
                request.Abort();
            }
        }
    }
}
#endif
