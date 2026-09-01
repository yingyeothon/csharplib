namespace Yingyeothon.Logger
{
    /// <summary>A filtered logger over <see cref="LogWriters.Console"/>.</summary>
    public static class ConsoleLogger
    {
        /// <summary>A console logger filtered at <see cref="LogSeverity.Info"/>.</summary>
        public static ILogger Create() => Create(LogSeverity.Info);

        /// <summary>A console logger filtered at <paramref name="severity"/>.</summary>
        public static ILogger Create(LogSeverity severity)
            => FilteredLogger.Create(new FilteredLoggerOptions { Severity = severity, Writer = LogWriters.Console });
    }
}
