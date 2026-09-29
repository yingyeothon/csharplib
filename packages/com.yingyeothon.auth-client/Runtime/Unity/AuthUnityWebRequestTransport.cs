#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Yingyeothon.Auth
{
    /// <summary>
    /// An <see cref="IAuthTransport"/> over <c>UnityWebRequest</c>, for Unity WebGL where
    /// <c>HttpClient</c> cannot send. It works on every Unity platform, so a build may
    /// use it unconditionally; on the others <see cref="AuthHttpClientTransport.Default"/>
    /// is equally fine.
    /// </summary>
    /// <remarks>
    /// Call it from the main thread: <c>UnityWebRequest</c> is main-thread only. The
    /// call's bound is applied twice — as <c>UnityWebRequest.timeout</c>, the only timer
    /// WebGL can run, and by the cancellation token, whose abort is marshalled back to
    /// the main thread. Every status comes back as an <see cref="AuthHttpResponse"/>; a
    /// connection or data failure, or a reply over 1 MiB, throws a message that names
    /// neither the URL nor a header. The task's continuations are not run inside the
    /// request's callback, so a throwing <c>await</c> in the game surfaces at the game's
    /// own frame.
    /// </remarks>
    public sealed class AuthUnityWebRequestTransport : IAuthTransport
    {
        /// <summary>The shared instance; it holds no state.</summary>
        public static readonly IAuthTransport Instance = new AuthUnityWebRequestTransport();

        /// <summary>The largest reply accepted: an auth answer is a few hundred bytes.</summary>
        private const long MaxReplyBytes = 1024 * 1024;

        private AuthUnityWebRequestTransport()
        {
        }

        public Task<AuthHttpResponse> SendAsync(AuthHttpRequest call, CancellationToken cancellationToken)
        {
            if (call == null)
            {
                throw new ArgumentNullException(nameof(call));
            }

            var source = new TaskCompletionSource<AuthHttpResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (cancellationToken.IsCancellationRequested)
            {
                source.TrySetCanceled(cancellationToken);
                return source.Task;
            }

            var request = new UnityWebRequest(call.Url.AbsoluteUri, call.Method);
            var finished = false;
            try
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                if (call.Body != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(call.Body));
                }

                foreach (var header in call.Headers)
                {
                    request.SetRequestHeader(header.Key, header.Value);
                }

                // No redirects: a 307 or 308 would re-send the provider credential in the body
                // to whatever host it named.
                request.redirectLimit = 0;

                // Seconds, rounded up, never zero: zero means no timeout to Unity.
                request.timeout = (int)Math.Max(1, Math.Ceiling(call.Timeout.TotalSeconds));
            }
            catch (Exception)
            {
                request.Dispose();
                throw;
            }

            // The token fires on a timer thread; Abort is main-thread only. The
            // context captured here is the main thread's, so the abort is posted back
            // to it, and `finished` is only ever touched there.
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

                    if (request.result == UnityWebRequest.Result.ConnectionError
                        || request.result == UnityWebRequest.Result.DataProcessingError)
                    {
                        // Not request.error: it can quote the URL. The kind is enough.
                        source.TrySetException(new IOException("auth transport failed: " + request.result));
                        return;
                    }

                    if (request.downloadedBytes > MaxReplyBytes)
                    {
                        source.TrySetException(new IOException("auth transport refused a reply over " + MaxReplyBytes + " bytes"));
                        return;
                    }

                    var body = request.downloadHandler != null ? request.downloadHandler.text : null;
                    source.TrySetResult(new AuthHttpResponse((int)request.responseCode, body ?? string.Empty));
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
