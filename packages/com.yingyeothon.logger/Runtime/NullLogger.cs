using Yingyeothon.Codec;

namespace Yingyeothon.Logger
{
    /// <summary>The logger every package defaults to: it writes nothing.</summary>
    public static class NullLogger
    {
        /// <summary>The shared instance. <see cref="LogWriters.Combine"/> recognises it by reference.</summary>
        public static readonly ILogger Instance = new NullLoggerImpl();

        private sealed class NullLoggerImpl : ILogger
        {
            public LogSeverity Severity
            {
                get => LogSeverity.None;
                set { }
            }

            public bool IsEnabled(LogSeverity severity) => false;

            public void Debug(string message, JsonValue? context = null)
            {
            }

            public void Info(string message, JsonValue? context = null)
            {
            }

            public void Warn(string message, JsonValue? context = null)
            {
            }

            public void Error(string message, JsonValue? context = null)
            {
            }
        }
    }
}
