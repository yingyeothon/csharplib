#if UNITY_5_3_OR_NEWER
#nullable enable
using UnityEngine;

namespace Yingyeothon.Tracer.Samples
{
    /// <summary>
    /// Starts tracer from a DSN your build writes to <c>Assets/Resources/tracer-dsn.txt</c>
    /// (git-ignored, absent in a release build), and sends a report with a screenshot on
    /// demand. Put it on one GameObject in your first scene; it survives scene loads.
    /// </summary>
    public sealed class TracerQuickstart : MonoBehaviour
    {
        private TracerSession? _session;
        private TracerRunner? _runner;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);

            // Written by the build, e.g. `yyt tracer dsn <res> > Assets/Resources/tracer-dsn.txt`;
            // list that file in .gitignore. Never type a DSN into the inspector: a scene is
            // committed. No file → empty DSN → the client is off.
            var file = Resources.Load<TextAsset>("tracer-dsn");
            var dsn = file != null ? file.text.Trim() : string.Empty;

            // UnityWebRequest works on every Unity platform; HttpClient does not on WebGL.
            _session = new TracerSession(dsn, Application.version, null, TracerUnityWebRequestTransport.Instance);
            _runner = TracerRunner.CreatePersistent(_session.Client, true);
        }

        public async void ReportThis()
        {
            if (_session == null || _runner == null)
            {
                return;
            }

            _session.Tapped("e2", false);
            var id = await _runner.ReportWithScreenshot("rejected a legal move", _session.Rejected("8/8/8/8/8/8/4P3/8"));
            if (id != null)
            {
                _session.Client.Feedback("the knight vanished", id);
            }
        }

        private void OnDestroy()
        {
            if (_runner != null)
            {
                Destroy(_runner.gameObject);
            }

            _session?.Client.Dispose();
        }
    }
}
#endif
