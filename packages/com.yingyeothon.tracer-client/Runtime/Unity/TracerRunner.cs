#if UNITY_5_3_OR_NEWER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Yingyeothon.Codec;

namespace Yingyeothon.Tracer
{
    /// <summary>
    /// The Unity side of a tracer client: polls it every frame, turns Unity's log messages
    /// and scene loads into breadcrumbs and log lines, flushes when the app is paused or
    /// quits, and captures a screenshot for a report.
    /// </summary>
    /// <remarks>
    /// It is debug tooling, not crash reporting: an exception Unity logs becomes a
    /// breadcrumb and an <c>error</c> log line, never an event. Send an event with
    /// <see cref="ReportWithScreenshot"/> when the game notices something wrong.
    /// The runner does not own the client: dispose it yourself, and destroy the runner's
    /// GameObject when you replace the client (after a <c>401</c>, say).
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TracerRunner : MonoBehaviour
    {
        private readonly List<Pending> _pending = new List<Pending>();
        private ITracerClient? _client;
        private bool _hooked;

        private sealed class Pending
        {
            internal Pending(string message, TracerReportOptions options, TaskCompletionSource<string?> source)
            {
                Message = message;
                Options = options;
                Source = source;
            }

            internal string Message { get; }

            internal TracerReportOptions Options { get; }

            internal TaskCompletionSource<string?> Source { get; }
        }

        /// <summary>The client this runner drives.</summary>
        public ITracerClient? Client => _client;

        /// <summary>
        /// Creates a runner that survives scene loads, polls <paramref name="client"/> and,
        /// with <paramref name="captureUnityLogs"/>, forwards <c>Application.logMessageReceived</c>
        /// and <c>SceneManager.sceneLoaded</c> to it.
        /// </summary>
        public static TracerRunner CreatePersistent(ITracerClient client, bool captureUnityLogs)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            var host = new GameObject("TracerRunner");
            DontDestroyOnLoad(host);
            var runner = host.AddComponent<TracerRunner>();
            runner._client = client;
            if (captureUnityLogs)
            {
                Application.logMessageReceived += runner.OnLog;
                SceneManager.sceneLoaded += runner.OnSceneLoaded;
                runner._hooked = true;
            }

            return runner;
        }

        /// <summary>
        /// <c>os</c> and <c>device</c> contexts from <c>SystemInfo</c>, for
        /// <see cref="TracerOptions.Contexts"/>: tracer's OS and device columns read them.
        /// </summary>
        public static JsonValue DeviceContexts()
            => Json.Object()
                .Set("os", Json.Object().Set("name", SystemInfo.operatingSystemFamily.ToString()).Set("version", SystemInfo.operatingSystem).Build())
                .Set("device", Json.Object().Set("model", SystemInfo.deviceModel).Build())
                .Build();

        /// <summary>
        /// Captures the screen at the end of this frame as a PNG and sends a report with it;
        /// the capture shows whatever is on screen then, and encoding it costs a frame on a
        /// phone. The task completes with the event id once the report is queued, or null
        /// when the client is not active. A failed capture, or a runner disabled or destroyed
        /// before the frame ends, sends the report without a screenshot. Your
        /// <paramref name="options"/> object is not changed. Call it from the main thread.
        /// </summary>
        public Task<string?> ReportWithScreenshot(string message, TracerReportOptions? options)
        {
            var source = new TaskCompletionSource<string?>();
            if (_client == null || !_client.Enabled)
            {
                source.SetResult(null);
                return source.Task;
            }

            var o = options ?? new TracerReportOptions();
            var copy = new TracerReportOptions
            {
                Data = o.Data,
                Screenshot = o.Screenshot,
                Attachments = o.Attachments,
                Exception = o.Exception,
                StackTrace = o.StackTrace,
                Level = o.Level,
            };
            var pending = new Pending(message, copy, source);
            if (!isActiveAndEnabled)
            {
                Send(pending, null);
                return source.Task;
            }

            _pending.Add(pending);
            StartCoroutine(Capture(pending));
            return source.Task;
        }

        private IEnumerator Capture(Pending pending)
        {
            yield return new WaitForEndOfFrame();
            byte[]? png = null;
            Texture2D? texture = null;
            try
            {
                texture = ScreenCapture.CaptureScreenshotAsTexture();
                png = texture.EncodeToPNG();
            }
            catch (Exception)
            {
                png = null;
            }
            finally
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }

            Send(pending, png);
        }

        private void Send(Pending pending, byte[]? png)
        {
            _pending.Remove(pending);
            if (png != null)
            {
                pending.Options.Screenshot = png;
            }

            pending.Source.TrySetResult(_client?.Report(pending.Message, pending.Options));
        }

        private void Update() => _client?.Poll();

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                FlushNow();
            }
        }

        private void OnApplicationQuit() => FlushNow();

        /// <summary>Queues the buffered lines and starts a send now: no Update runs after this.</summary>
        private void FlushNow()
        {
            if (_client != null)
            {
                _client.FlushAsync();
                _client.Poll();
            }
        }

        private void OnDisable()
        {
            // A coroutine stops with its object; settle what it would have sent.
            foreach (var pending in _pending.ToArray())
            {
                Send(pending, null);
            }
        }

        private void OnDestroy()
        {
            if (_hooked)
            {
                Application.logMessageReceived -= OnLog;
                SceneManager.sceneLoaded -= OnSceneLoaded;
                _hooked = false;
            }
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            var client = _client;
            if (client == null || !client.Enabled)
            {
                return;
            }

            var level = type switch
            {
                LogType.Warning => TracerLevel.Warning,
                LogType.Error => TracerLevel.Error,
                LogType.Assert => TracerLevel.Error,
                LogType.Exception => TracerLevel.Error,
                _ => TracerLevel.Info,
            };
            client.Breadcrumb("unity.log", condition, null, level);
            var attributes = Json.Object().Set("unity.log_type", type.ToString());
            if (type == LogType.Exception && !string.IsNullOrEmpty(stackTrace))
            {
                attributes.Set("stacktrace", stackTrace);
            }

            client.Log(level, condition, attributes.Build());
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var client = _client;
            if (client != null && client.Enabled)
            {
                client.Breadcrumb("scene", "loaded " + scene.name, Json.Object().Set("mode", mode.ToString()).Build(), TracerLevel.Info);
            }
        }
    }
}
#endif
