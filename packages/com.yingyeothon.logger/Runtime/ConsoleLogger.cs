namespace Yingyeothon.Logger
{
    /// <summary>A filtered logger over <see cref="LogWriters.Console"/>.</summary>
    public static class ConsoleLogger
    {
        public static ILogger Create() => Create(LogSeverity.Info);

        public static ILogger Create(LogSeverity severity)
            => FilteredLogger.Create(new FilteredLoggerOptions { Severity = severity, Writer = LogWriters.Console });
    }
}
