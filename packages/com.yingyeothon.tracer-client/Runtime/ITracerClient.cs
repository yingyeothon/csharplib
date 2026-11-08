using System;
using System.Threading.Tasks;
using Yingyeothon.Codec;

namespace Yingyeothon.Tracer
{
    /// <summary>
    /// A tracer client: a breadcrumb ring, buffered log lines and a bounded send queue in
    /// front of tracer's envelope ingest.
    /// </summary>
    /// <remarks>
    /// It never throws into the game and never blocks it: every call returns at once and a
    /// failure is a count in <see cref="Stats"/>. Calls may come from any thread.
    /// <b>Nothing is sent until <see cref="Poll"/> runs</b>: sends start, answers are read
    /// and timers advance only there, so call it from <c>Update()</c> (<c>TracerRunner</c>
    /// does) — it is what lets the same code run on a WebGL player, which has no timer
    /// thread.
    /// </remarks>
    public interface ITracerClient : IDisposable
    {
        /// <summary>Where the client stands.</summary>
        TracerState State { get; }

        /// <summary>Whether calls do anything: <see cref="State"/> is <see cref="TracerState.Active"/>.</summary>
        bool Enabled { get; }

        /// <summary>The <c>trace_id</c> on every event and log line of this client, 32 hex digits.</summary>
        string TraceId { get; }

        /// <summary>Counters since construction.</summary>
        TracerStats Stats { get; }

        /// <summary>
        /// Adds a tag to later events and log lines; a null <paramref name="value"/> removes
        /// it. Past <see cref="TracerLimits.MaxTags"/> a new key is ignored.
        /// </summary>
        void SetTag(string key, string? value);

        /// <summary>Remembers a step for the next report, at <see cref="TracerLevel.Info"/>. Breadcrumbs leave the device only inside an event.</summary>
        void Breadcrumb(string category, string message);

        /// <summary>Remembers a step for the next report with a JSON object of <paramref name="data"/>.</summary>
        void Breadcrumb(string category, string message, JsonValue? data, TracerLevel level);

        /// <summary>Buffers a log line. It is queued by size, after the flush interval, or with the next report.</summary>
        void Log(TracerLevel level, string message);

        /// <summary>
        /// Buffers a log line whose <paramref name="attributes"/> (a JSON object) become typed
        /// attributes: a whole number within ±2^53 is <c>integer</c>, any other number
        /// <c>double</c>, and an array or object its JSON text as a <c>string</c>.
        /// </summary>
        void Log(TracerLevel level, string message, JsonValue? attributes);

        /// <summary>A breadcrumb and a log line in one call, at <see cref="TracerLevel.Info"/>.</summary>
        void Trace(string category, string message);

        /// <summary>A breadcrumb and a log line in one call: the trail before something looks wrong, in the report and in the timeline.</summary>
        void Trace(string category, string message, JsonValue? data, TracerLevel level);

        /// <summary>
        /// Queues an event with the breadcrumb ring and the buffered log lines, sent at the
        /// next <see cref="Poll"/>. Returns its event id (32 hex digits) for
        /// <see cref="Feedback"/>, or null when the client is not active or the event could
        /// not be built.
        /// </summary>
        string? Report(string message);

        /// <summary><see cref="Report(string)"/> with data, a screenshot, other files, an exception and a level.</summary>
        string? Report(string message, TracerReportOptions options);

        /// <summary>
        /// Queues a player's note, linked to the report <paramref name="eventId"/> when it is
        /// one. Returns the feedback's own id, or null when the client is not active.
        /// </summary>
        string? Feedback(string note, string? eventId);

        /// <summary>
        /// Queues the buffered log lines. The task completes inside <see cref="Poll"/> once the
        /// queue is empty or waiting out a back-off, and at once when the client is not active.
        /// It never faults.
        /// </summary>
        Task FlushAsync();

        /// <summary>
        /// Flushes for at most the request timeout, then closes; the task completes inside
        /// <see cref="Poll"/>. Whatever is left is counted as <c>closed</c>.
        /// </summary>
        Task CloseAsync();

        /// <summary>
        /// Starts sends, reads their answers and advances the flush and back-off timers. Call
        /// it every frame. A call made while another thread is polling returns at once.
        /// </summary>
        void Poll();
    }

    /// <summary>Creates tracer clients.</summary>
    public static class TracerClient
    {
        /// <summary>
        /// The version this library reports in the envelope header (<c>sdk.version</c>) and in
        /// <c>X-Sentry-Auth</c> (<c>sentry_client=yyt-csharp/&lt;version&gt;</c>); the package version.
        /// </summary>
        public const string SdkVersion = "0.3.0";

        /// <summary>
        /// Builds a client. A DSN that is empty or does not parse gives a client in
        /// <see cref="TracerState.Off"/>: nothing throws, and the bad DSN is logged as one
        /// fixed line that never quotes it. Out-of-range numeric options are clamped.
        /// </summary>
        public static ITracerClient Create(TracerOptions options)
            => new TracerClientImpl(options ?? throw new ArgumentNullException(nameof(options)));
    }
}
