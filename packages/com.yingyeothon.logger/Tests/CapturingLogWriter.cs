using System.Collections.Generic;
using Yingyeothon.Codec;

namespace Yingyeothon.Logger.Tests
{
    /// <summary>
    /// Records everything written so a test can assert on it. Assertions about what
    /// was NOT logged read this, which is why the whole record — severity, message
    /// and rendered context — is kept rather than just the message.
    /// </summary>
    public sealed class CapturingLogWriter : ILogWriter
    {
        public List<string> Lines { get; } = new List<string>();

        public void Debug(string message, JsonValue? context = null) => Add(LogSeverity.Debug, message, context);

        public void Info(string message, JsonValue? context = null) => Add(LogSeverity.Info, message, context);

        public void Warn(string message, JsonValue? context = null) => Add(LogSeverity.Warn, message, context);

        public void Error(string message, JsonValue? context = null) => Add(LogSeverity.Error, message, context);

        public string Text => string.Join("\n", Lines);

        private void Add(LogSeverity severity, string message, JsonValue? context)
            => Lines.Add(LogWriters.Format(severity, message, context));
    }
}
