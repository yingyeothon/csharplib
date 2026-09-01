using Yingyeothon.Codec;

namespace Yingyeothon.Logger
{
    /// <summary>
    /// Where log records go. The call style is message first, structured context
    /// after: <c>writer.Info("lobby connected", Json.Object().Set("userId", id).Build())</c>.
    /// </summary>
    /// <remarks>
    /// The context is a <see cref="JsonValue"/> rather than an arbitrary object so a
    /// writer can render it without reflection, which IL2CPP's managed stripper
    /// would otherwise break.
    /// </remarks>
    public interface ILogWriter
    {
        /// <summary>Writes at <see cref="LogSeverity.Debug"/>. Log routing facts — ids, codes, counts — never the thing being routed.</summary>
        void Debug(string message, JsonValue? context = null);

        /// <summary>Writes at <see cref="LogSeverity.Info"/>.</summary>
        void Info(string message, JsonValue? context = null);

        /// <summary>Writes at <see cref="LogSeverity.Warn"/>.</summary>
        void Warn(string message, JsonValue? context = null);

        /// <summary>Writes at <see cref="LogSeverity.Error"/>.</summary>
        void Error(string message, JsonValue? context = null);
    }

    /// <summary>A <see cref="ILogWriter"/> with a severity threshold of its own.</summary>
    public interface ILogger : ILogWriter
    {
        /// <summary>
        /// The threshold. It is read on every call, so raising or lowering it at
        /// runtime takes effect immediately.
        /// </summary>
        LogSeverity Severity { get; set; }

        /// <summary>
        /// Whether a record at this severity would be written. Guard an expensive
        /// context with this rather than building it and having it dropped.
        /// </summary>
        bool IsEnabled(LogSeverity severity);
    }
}
