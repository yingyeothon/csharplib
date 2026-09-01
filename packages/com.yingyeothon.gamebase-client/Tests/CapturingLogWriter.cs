using System.Collections.Generic;
using Yingyeothon.Codec;
using Yingyeothon.Logger;

namespace Yingyeothon.Gamebase.Client.Tests
{
    /// <summary>
    /// Records every record in full so a test can assert on what was written and,
    /// just as importantly, on what was not.
    /// </summary>
    /// <remarks>
    /// Kept local to this assembly rather than shared from the logger's tests: an
    /// assertion that the token never reaches a log has to read the same rendering a
    /// real writer would see, and that is this package's concern.
    /// </remarks>
    internal sealed class CapturingLogWriter : ILogWriter
    {
        internal List<string> Lines { get; } = new List<string>();

        internal string Text => string.Join("\n", Lines);

        public void Debug(string message, JsonValue? context = null) => Add(LogSeverity.Debug, message, context);

        public void Info(string message, JsonValue? context = null) => Add(LogSeverity.Info, message, context);

        public void Warn(string message, JsonValue? context = null) => Add(LogSeverity.Warn, message, context);

        public void Error(string message, JsonValue? context = null) => Add(LogSeverity.Error, message, context);

        private void Add(LogSeverity severity, string message, JsonValue? context)
            => Lines.Add(LogWriters.Format(severity, message, context));
    }
}
