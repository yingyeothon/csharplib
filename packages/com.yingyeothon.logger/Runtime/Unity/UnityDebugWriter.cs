#if UNITY_5_3_OR_NEWER
using Yingyeothon.Codec;

namespace Yingyeothon.Logger
{
    /// <summary>
    /// Routes log records to the Unity console. It lives behind a compile guard and
    /// is excluded from the plain dotnet build, so the package itself never
    /// references the engine.
    /// </summary>
    /// <remarks>
    /// Unity has no separate debug level, so <c>Debug</c> and <c>Info</c> both go to
    /// <c>UnityEngine.Debug.Log</c>.
    /// </remarks>
    public static class UnityDebugWriter
    {
        public static readonly ILogWriter Instance = LogWriters.FromAction(Write);

        /// <summary>A filtered logger writing to the Unity console.</summary>
        public static ILogger CreateLogger(LogSeverity severity)
            => FilteredLogger.Create(new FilteredLoggerOptions { Severity = severity, Writer = Instance });

        private static void Write(LogSeverity severity, string message, JsonValue? context)
        {
            var line = LogWriters.Format(severity, message, context);
            switch (severity)
            {
                case LogSeverity.Warn:
                    UnityEngine.Debug.LogWarning(line);
                    return;
                case LogSeverity.Error:
                    UnityEngine.Debug.LogError(line);
                    return;
                default:
                    UnityEngine.Debug.Log(line);
                    return;
            }
        }
    }
}
#endif
