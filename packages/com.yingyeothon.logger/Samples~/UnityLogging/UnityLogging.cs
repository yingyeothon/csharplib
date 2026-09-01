using Yingyeothon.Codec;
using Yingyeothon.Logger;

namespace Yingyeothon.Logger.Samples
{
    /// <summary>Wiring the logger to a host, and what may go in a log line.</summary>
    public static class UnityLogging
    {
        /// <summary>
        /// Routes records to any sink without the package referencing an engine —
        /// in Unity, pass <c>UnityEngine.Debug.Log</c> as the action.
        /// </summary>
        public static ILogger ForHost(System.Action<string> write, LogSeverity severity = LogSeverity.Info)
            => FilteredLogger.Create(new FilteredLoggerOptions
            {
                Severity = severity,
                Writer = LogWriters.FromAction((level, message, context) =>
                    write(LogWriters.Format(level, message, context))),
            });

        /// <summary>
        /// Log the routing facts — ids, codes, counts, lengths — and never the thing
        /// being routed. A token, a payload or a frame body in a log line is a leak,
        /// and <c>Debug</c> is not an exemption: a consumer may install a writer that
        /// persists forever.
        /// </summary>
        public static void RecordConnected(ILogger logger, string channelId, string userId, int peerCount)
            => logger.Info("lobby connected", Json.Object()
                .Set("channelId", channelId)
                .Set("userId", userId)
                .Set("peers", peerCount)
                .Build());

        /// <summary>Guard an expensive context rather than building one that gets dropped.</summary>
        public static void RecordFrame(ILogger logger, string type, int byteCount)
        {
            if (!logger.IsEnabled(LogSeverity.Debug))
            {
                return;
            }

            // The type and the size, never the body.
            logger.Debug("frame", Json.Object().Set("type", type).Set("bytes", byteCount).Build());
        }
    }
}
