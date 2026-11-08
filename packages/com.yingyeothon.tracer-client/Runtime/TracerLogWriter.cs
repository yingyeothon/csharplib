using System;
using Yingyeothon.Codec;
using Yingyeothon.Logger;

namespace Yingyeothon.Tracer
{
    /// <summary>
    /// A <see cref="ILogWriter"/> that forwards every record to a tracer client as a log line
    /// with the context as attributes and, optionally, also as a breadcrumb in category
    /// <c>log</c>. Compose it with <see cref="LogWriters.Combine"/>.
    /// </summary>
    /// <remarks>
    /// Do not hand the logger it feeds to the same client's <see cref="TracerOptions.Logger"/>:
    /// the client ignores what it hears from its own diagnostics, but the loop is pointless.
    /// </remarks>
    public static class TracerLogWriter
    {
        /// <summary>A writer into <paramref name="client"/>; with <paramref name="breadcrumbs"/> each record is also a breadcrumb.</summary>
        public static ILogWriter Create(ITracerClient client, bool breadcrumbs)
            => new Writer(client ?? throw new ArgumentNullException(nameof(client)), breadcrumbs);

        private sealed class Writer : ILogWriter
        {
            private readonly ITracerClient _client;
            private readonly bool _breadcrumbs;

            internal Writer(ITracerClient client, bool breadcrumbs)
            {
                _client = client;
                _breadcrumbs = breadcrumbs;
            }

            public void Debug(string message, JsonValue? context = null) => Write(TracerLevel.Debug, message, context);

            public void Info(string message, JsonValue? context = null) => Write(TracerLevel.Info, message, context);

            public void Warn(string message, JsonValue? context = null) => Write(TracerLevel.Warning, message, context);

            public void Error(string message, JsonValue? context = null) => Write(TracerLevel.Error, message, context);

            private void Write(TracerLevel level, string message, JsonValue? context)
            {
                if (_breadcrumbs)
                {
                    _client.Breadcrumb("log", message, context, level);
                }

                _client.Log(level, message, context);
            }
        }
    }
}
