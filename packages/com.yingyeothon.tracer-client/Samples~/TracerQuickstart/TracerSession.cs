#nullable enable
using System.Collections.Generic;
using Yingyeothon.Codec;

namespace Yingyeothon.Tracer.Samples
{
    /// <summary>
    /// Builds a tracer client from a DSN your build configuration supplies, and the calls a
    /// game makes around a move it rejected. Engine-free, so it compiles anywhere.
    /// </summary>
    public sealed class TracerSession
    {
        /// <summary>
        /// <paramref name="dsn"/> comes from <c>yyt tracer dsn &lt;res&gt;</c> through the build,
        /// never from source; empty turns the client off. Pass
        /// <c>TracerUnityWebRequestTransport.Instance</c> as <paramref name="transport"/> in a
        /// Unity build.
        /// </summary>
        public TracerSession(string dsn, string release, string? gitSha, ITracerTransport? transport)
        {
            Client = TracerClient.Create(new TracerOptions
            {
                Dsn = dsn,
                Release = release,
                Dist = gitSha,
                Environment = "debug",
                Tags = new Dictionary<string, string> { ["engine"] = "unity" },
                Transport = transport,
            });
        }

        public ITracerClient Client { get; }

        /// <summary>The trail before something looks wrong: a breadcrumb and a log line each.</summary>
        public void Tapped(string square, bool legal)
            => Client.Trace("ui", "tap square", Json.Object().Set("square", square).Set("legal", legal).Build(), TracerLevel.Info);

        /// <summary>The options of a "report this": the board as extra data, at warning level.</summary>
        public TracerReportOptions Rejected(string board)
            => new TracerReportOptions { Data = Json.Object().Set("board", board).Build(), Level = TracerLevel.Warning };
    }
}
