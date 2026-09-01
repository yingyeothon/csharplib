using System;
using System.Collections.Generic;
using System.Globalization;
using Yingyeothon.Codec;

namespace Yingyeothon.Logger
{
    /// <summary>Ready-made <see cref="ILogWriter"/> implementations and combinators.</summary>
    public static class LogWriters
    {
        /// <summary>Writes to <see cref="System.Console"/>. Unity callers want <c>FromAction</c> instead.</summary>
        public static readonly ILogWriter Console = new ConsoleWriter();

        /// <summary>A writer that discards everything.</summary>
        public static readonly ILogWriter Null = new NullWriter();

        /// <summary>
        /// Fans a record out to every writer, in order. <see cref="NullLogger.Instance"/>
        /// is skipped by reference, matching tslib's <c>combine</c>; a different
        /// writer that happens to discard everything is still called.
        /// </summary>
        public static ILogWriter Combine(params ILogWriter[] writers)
        {
            if (writers == null)
            {
                throw new ArgumentNullException(nameof(writers));
            }

            var targets = new List<ILogWriter>(writers.Length);
            foreach (var writer in writers)
            {
                if (writer == null)
                {
                    throw new ArgumentException("A writer cannot be null.", nameof(writers));
                }

                if (!ReferenceEquals(writer, NullLogger.Instance))
                {
                    targets.Add(writer);
                }
            }

            return new CombinedWriter(targets.ToArray());
        }

        /// <summary>
        /// Adapts a callback into a writer. This is the seam a Unity game uses to
        /// reach <c>UnityEngine.Debug</c> without the package referencing the engine.
        /// </summary>
        public static ILogWriter FromAction(Action<LogSeverity, string, JsonValue?> sink)
        {
            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            return new ActionWriter(sink);
        }

        /// <summary>Renders a record the way the console writer does.</summary>
        public static string Format(LogSeverity severity, string message, JsonValue? context)
        {
            var head = "[" + severity.ToString().ToUpperInvariant() + "] " + (message ?? string.Empty);
            return context == null ? head : head + " " + Json.Stringify(context);
        }

        private sealed class ConsoleWriter : ILogWriter
        {
            public void Debug(string message, JsonValue? context = null)
                => System.Console.Out.WriteLine(Format(LogSeverity.Debug, message, context));

            public void Info(string message, JsonValue? context = null)
                => System.Console.Out.WriteLine(Format(LogSeverity.Info, message, context));

            public void Warn(string message, JsonValue? context = null)
                => System.Console.Error.WriteLine(Format(LogSeverity.Warn, message, context));

            public void Error(string message, JsonValue? context = null)
                => System.Console.Error.WriteLine(Format(LogSeverity.Error, message, context));
        }

        private sealed class NullWriter : ILogWriter
        {
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

        private sealed class CombinedWriter : ILogWriter
        {
            private readonly ILogWriter[] _targets;

            internal CombinedWriter(ILogWriter[] targets)
            {
                _targets = targets;
            }

            public void Debug(string message, JsonValue? context = null)
            {
                foreach (var target in _targets)
                {
                    target.Debug(message, context);
                }
            }

            public void Info(string message, JsonValue? context = null)
            {
                foreach (var target in _targets)
                {
                    target.Info(message, context);
                }
            }

            public void Warn(string message, JsonValue? context = null)
            {
                foreach (var target in _targets)
                {
                    target.Warn(message, context);
                }
            }

            public void Error(string message, JsonValue? context = null)
            {
                foreach (var target in _targets)
                {
                    target.Error(message, context);
                }
            }
        }

        private sealed class ActionWriter : ILogWriter
        {
            private readonly Action<LogSeverity, string, JsonValue?> _sink;

            internal ActionWriter(Action<LogSeverity, string, JsonValue?> sink)
            {
                _sink = sink;
            }

            public void Debug(string message, JsonValue? context = null) => _sink(LogSeverity.Debug, message, context);

            public void Info(string message, JsonValue? context = null) => _sink(LogSeverity.Info, message, context);

            public void Warn(string message, JsonValue? context = null) => _sink(LogSeverity.Warn, message, context);

            public void Error(string message, JsonValue? context = null) => _sink(LogSeverity.Error, message, context);
        }
    }
}
