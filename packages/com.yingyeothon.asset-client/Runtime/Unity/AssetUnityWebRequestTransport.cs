#if UNITY_5_3_OR_NEWER
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Yingyeothon.Assets
{
    /// <summary>
    /// An <see cref="IAssetTransport"/> over <c>UnityWebRequest</c>, for Unity WebGL where
    /// <c>HttpClient</c> cannot send. It works on every Unity platform.
    /// </summary>
    /// <remarks>
    /// Call it from the main thread: <c>UnityWebRequest</c> is main-thread only. It buffers
    /// each response whole before handing it back — a browser's fetch does the same — so a
    /// whole-file read holds the file twice, and a download the whole of each ranged answer
    /// rather than one segment; pass <see cref="AssetHttpClientTransport.Default"/> off WebGL
    /// for large downloads. It follows no redirects, applies the response timeout as
    /// <c>UnityWebRequest.timeout</c> (the only timer WebGL can run) over the whole
    /// transfer, and a connection or data failure throws a message naming
    /// <c>UnityWebRequest.result</c>, never the URL.
    /// </remarks>
    public sealed class AssetUnityWebRequestTransport : IAssetTransport
    {
        /// <summary>The shared instance; it holds no state.</summary>
        public static readonly IAssetTransport Instance = new AssetUnityWebRequestTransport();

        private AssetUnityWebRequestTransport()
        {
        }

        public Task<IAssetResponse> SendAsync(AssetHttpRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var source = new TaskCompletionSource<IAssetResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (cancellationToken.IsCancellationRequested)
            {
                source.TrySetCanceled(cancellationToken);
                return source.Task;
            }

            var web = new UnityWebRequest(request.Url.AbsoluteUri, request.Method);
            var finished = false;
            try
            {
                web.downloadHandler = new DownloadHandlerBuffer();
                web.redirectLimit = 0;
                foreach (var header in request.Headers)
                {
                    web.SetRequestHeader(header.Key, header.Value);
                }

                web.timeout = (int)Math.Max(1, Math.Ceiling(request.ResponseTimeout.TotalSeconds));
            }
            catch (Exception)
            {
                web.Dispose();
                throw;
            }

            // The token fires on a timer thread; Abort is main-thread only, so the abort is
            // posted back to the context captured here.
            var clock = Stopwatch.StartNew();
            var timeoutSeconds = web.timeout;
            var context = SynchronizationContext.Current;
            var registration = default(CancellationTokenRegistration);
            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(() =>
                {
                    if (context != null)
                    {
                        context.Post(_ => AbortIfPending(web, ref finished), null);
                    }
                    else
                    {
                        AbortIfPending(web, ref finished);
                    }
                });
            }

            web.SendWebRequest().completed += _ =>
            {
                finished = true;
                registration.Dispose();
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        web.Dispose();
                        source.TrySetCanceled(cancellationToken);
                        return;
                    }

                    // A redirect the limit refused is a ConnectionError that still carries
                    // the 3xx (observed on Mono and IL2CPP players): hand it back as the
                    // answer it is, as HttpClient would — unless the time ran out, which a
                    // stalled 3xx also reports as a ConnectionError.
                    var redirected = web.result == UnityWebRequest.Result.ConnectionError
                        && web.responseCode >= 300 && web.responseCode < 400
                        && clock.Elapsed.TotalSeconds < timeoutSeconds - 0.5;
                    if (!redirected
                        && (web.result == UnityWebRequest.Result.ConnectionError
                            || web.result == UnityWebRequest.Result.DataProcessingError))
                    {
                        var kind = web.result;
                        web.Dispose();

                        // UnityWebRequest reports its own timeout as a connection error; the
                        // elapsed time is how to tell, without reading `.error` (which can
                        // quote the URL). The client must not take a timeout for a refusal.
                        source.TrySetException(clock.Elapsed.TotalSeconds >= timeoutSeconds - 0.5
                            ? new TimeoutException("asset transport timed out")
                            : (Exception)new IOException("asset transport failed: " + kind));
                        return;
                    }

                    source.TrySetResult(new Response(web));
                }
                catch (Exception error)
                {
                    web.Dispose();
                    source.TrySetException(error);
                }
            };

            return source.Task;
        }

        private static void AbortIfPending(UnityWebRequest web, ref bool finished)
        {
            if (!finished)
            {
                web.Abort();
            }
        }

        private sealed class Response : IAssetResponse
        {
            private readonly UnityWebRequest _web;
            private readonly byte[] _body;
            private int _at;

            internal Response(UnityWebRequest web)
            {
                _web = web;
                _body = web.downloadHandler?.data ?? new byte[0];
                Status = (int)web.responseCode;
            }

            public int Status { get; }

            public string? GetHeader(string name) => _web.GetResponseHeader(name);

            public Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                var n = Math.Min(count, _body.Length - _at);
                Buffer.BlockCopy(_body, _at, buffer, offset, n);
                _at += n;
                return Task.FromResult(n);
            }

            public void Dispose() => _web.Dispose();
        }
    }
}
#endif
