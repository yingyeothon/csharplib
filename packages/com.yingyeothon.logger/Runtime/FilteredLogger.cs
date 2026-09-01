using System;
using Yingyeothon.Codec;

namespace Yingyeothon.Logger
{
    /// <summary>Options for <see cref="FilteredLogger.Create"/>.</summary>
    public sealed class FilteredLoggerOptions
    {
        /// <summary>The initial threshold. It stays mutable on the created logger.</summary>
        public LogSeverity Severity { get; set; } = LogSeverity.Info;

        /// <summary>Where records that pass the threshold go.</summary>
        public ILogWriter? Writer { get; set; }
    }

    /// <summary>A logger that forwards to a writer only at or above its severity.</summary>
    public static class FilteredLogger
    {
        /// <summary>Creates a logger that forwards to the option's writer above its threshold.</summary>
        public static ILogger Create(FilteredLoggerOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.Writer == null)
            {
                throw new ArgumentException("A writer is required.", nameof(options));
            }

            return new FilteredLoggerImpl(options.Severity, options.Writer);
        }

        private sealed class FilteredLoggerImpl : ILogger
        {
            private readonly ILogWriter _writer;

            internal FilteredLoggerImpl(LogSeverity severity, ILogWriter writer)
            {
                Severity = severity;
                _writer = writer;
            }

            public LogSeverity Severity { get; set; }

            public bool IsEnabled(LogSeverity severity) => (int)severity >= (int)Severity;

            public void Debug(string message, JsonValue? context = null)
            {
                if (IsEnabled(LogSeverity.Debug))
                {
                    _writer.Debug(message, context);
                }
            }

            public void Info(string message, JsonValue? context = null)
            {
                if (IsEnabled(LogSeverity.Info))
                {
                    _writer.Info(message, context);
                }
            }

            public void Warn(string message, JsonValue? context = null)
            {
                if (IsEnabled(LogSeverity.Warn))
                {
                    _writer.Warn(message, context);
                }
            }

            public void Error(string message, JsonValue? context = null)
            {
                if (IsEnabled(LogSeverity.Error))
                {
                    _writer.Error(message, context);
                }
            }
        }
    }
}
