using System;
using System.Collections.Generic;
using Yingyeothon.Codec;
using Yingyeothon.Logger;

namespace Yingyeothon.Tracer
{
    /// <summary>Severity of a breadcrumb, a log line or a report.</summary>
    /// <remarks>
    /// On the wire an event says <c>warning</c> and a log line says <c>warn</c>, with the
    /// matching OpenTelemetry <c>severity_number</c>; the client writes each.
    /// </remarks>
    public enum TracerLevel
    {
        /// <summary>Diagnostic detail.</summary>
        Debug,

        /// <summary>Milestones.</summary>
        Info,

        /// <summary>Something looked wrong; the default level of a report.</summary>
        Warning,

        /// <summary>A failure.</summary>
        Error,

        /// <summary>A failure the game cannot continue from.</summary>
        Fatal,
    }

    /// <summary>Where a client stands.</summary>
    public enum TracerState
    {
        /// <summary>Sending: the DSN parsed and the ingest has not refused the key.</summary>
        Active,

        /// <summary>Never sending: the DSN was empty or did not parse. Every call is a no-op.</summary>
        Off,

        /// <summary>
        /// The ingest answered <c>401</c>: the key is wrong or rotated. Nothing more is sent
        /// until the game builds a new client with a new DSN.
        /// </summary>
        Stopped,

        /// <summary>The client was closed or disposed.</summary>
        Closed,
    }

    /// <summary>
    /// Limits the client keeps under, from the tracer ingest (the service repository's
    /// <c>docs/tracer.md</c>, <i>Wire contract for client libraries</i> and <i>Ingest</i>).
    /// Every size is UTF-8 bytes.
    /// </summary>
    public static class TracerLimits
    {
        /// <summary>The ingest refuses a body over 20 MiB, compressed or not; the client keeps the uncompressed envelope at or under this.</summary>
        public const int MaxEnvelopeBytes = 20 * 1024 * 1024;

        /// <summary>Each JSON item (event, feedback, log container) is dropped by the ingest over 1 MiB.</summary>
        public const int MaxJsonItemBytes = 1024 * 1024;

        /// <summary>JSON items one envelope may hold until the ingest commits it.</summary>
        public const int MaxJsonBytesPerEnvelope = 2 * 1024 * 1024;

        /// <summary>Entries in one log container.</summary>
        public const int MaxLogsPerContainer = 1000;

        /// <summary>Attachments per envelope.</summary>
        public const int MaxAttachmentsPerEnvelope = 20;

        /// <summary>Breadcrumbs the ingest keeps per event; the default ring size.</summary>
        public const int MaxBreadcrumbs = 500;

        /// <summary>Text the ingest keeps of a message, a log body or a breadcrumb message.</summary>
        public const int MaxTextBytes = 8 * 1024;

        /// <summary>A log line's attributes, a breadcrumb's data or a report's extra, as JSON: the ingest keeps them only up to this.</summary>
        public const int MaxDataBytes = 16 * 1024;

        /// <summary>Tags the ingest keeps per event.</summary>
        public const int MaxTags = 64;
    }

    /// <summary>A file sent with a report, stored by tracer and readable with <c>yyt tracer attachments</c>.</summary>
    public sealed class TracerAttachment
    {
        /// <summary>A file with an explicit name, MIME type and Sentry <c>attachment_type</c>.</summary>
        public TracerAttachment(string filename, byte[] bytes, string contentType, string attachmentType)
        {
            Filename = filename ?? throw new ArgumentNullException(nameof(filename));
            Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
            ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
            AttachmentType = attachmentType ?? throw new ArgumentNullException(nameof(attachmentType));
        }

        /// <summary>A PNG screenshot, <c>screenshot.png</c>, as Sentry SDKs name it.</summary>
        public static TracerAttachment Screenshot(byte[] png)
            => new TracerAttachment("screenshot.png", png, "image/png", "event.attachment");

        /// <summary>A view-hierarchy JSON document (<c>event.view_hierarchy</c>).</summary>
        public static TracerAttachment ViewHierarchy(byte[] json)
            => new TracerAttachment("view-hierarchy.json", json, "application/json", "event.view_hierarchy");

        /// <summary>File name shown by the read API, without a directory.</summary>
        public string Filename { get; }

        /// <summary>The bytes, sent as they are. The client keeps the array; do not change it after the call.</summary>
        public byte[] Bytes { get; }

        /// <summary>MIME type.</summary>
        public string ContentType { get; }

        /// <summary><c>event.attachment</c>, <c>event.view_hierarchy</c>, <c>event.minidump</c>, …</summary>
        public string AttachmentType { get; }
    }

    /// <summary>Counters a client keeps; a snapshot, so read it again for fresh values.</summary>
    public sealed class TracerStats
    {
        /// <summary>Builds a snapshot.</summary>
        public TracerStats(int envelopesSent, int queued, IReadOnlyDictionary<string, int> dropped)
        {
            EnvelopesSent = envelopesSent;
            Queued = queued;
            Dropped = dropped ?? throw new ArgumentNullException(nameof(dropped));
        }

        /// <summary>Envelopes the ingest answered with a <c>2xx</c>.</summary>
        public int EnvelopesSent { get; }

        /// <summary>Envelopes waiting to be sent, the one in flight included.</summary>
        public int Queued { get; }

        /// <summary>
        /// Things the client gave up on, by reason: <c>too_large</c> (<c>413</c>, or past a
        /// limit), <c>invalid</c> (<c>400</c>), <c>encoding</c> (<c>415</c>),
        /// <c>unauthorized</c> (<c>401</c>), <c>queue_full</c> (the oldest envelope made
        /// room), <c>http_&lt;status&gt;</c>, <c>internal</c>, <c>closed</c>.
        /// </summary>
        public IReadOnlyDictionary<string, int> Dropped { get; }
    }

    /// <summary>Options of one tracer client. <see cref="TracerClient.Create"/> copies them.</summary>
    public sealed class TracerOptions
    {
        /// <summary>
        /// <c>https://{writeKey}@{host}/{trc_id}</c>, from <c>yyt tracer dsn</c> through the
        /// game's build configuration; never committed. Null or empty means off.
        /// </summary>
        public string? Dsn { get; set; }

        /// <summary><c>version+build</c> of the game.</summary>
        public string? Release { get; set; }

        /// <summary>A build discriminator; a git sha lets a reader check out the code.</summary>
        public string? Dist { get; set; }

        /// <summary><c>debug</c>, <c>dev</c>, …</summary>
        public string? Environment { get; set; }

        /// <summary>Tags on every event and attributes on every log line; tracer keeps 64.</summary>
        public IReadOnlyDictionary<string, string>? Tags { get; set; }

        /// <summary><c>user.id</c> on events and logs: an installation id in a debug build, never a person's name or email.</summary>
        public string? UserId { get; set; }

        /// <summary>
        /// Extra event <c>contexts</c> as a JSON object (<c>device</c>, <c>os</c>, <c>app</c>, …);
        /// <c>trace</c> and <c>feedback</c> are the client's own and are ignored here.
        /// <c>device.model</c> is also put on every log line.
        /// </summary>
        public JsonValue? Contexts { get; set; }

        /// <summary>The HTTP seam, default <see cref="TracerHttpClientTransport.Default"/>; a Unity build passes <c>TracerUnityWebRequestTransport.Instance</c>.</summary>
        public ITracerTransport? Transport { get; set; }

        /// <summary>The client's own diagnostics (ids, statuses, counts), default <see cref="NullLogger.Instance"/>.</summary>
        public ILogger? Logger { get; set; }

        /// <summary>Monotonic milliseconds for deadlines and back-off, default a <c>Stopwatch</c>.</summary>
        public Func<double>? NowMillis { get; set; }

        /// <summary>Wall clock for event, breadcrumb and log timestamps, default <see cref="DateTimeOffset.UtcNow"/>.</summary>
        public Func<DateTimeOffset>? UtcNow { get; set; }

        /// <summary>Source of ids and back-off jitter in [0, 1), default a cryptographic generator.</summary>
        public Func<double>? Random { get; set; }

        /// <summary>Breadcrumbs kept for the next report, oldest dropped first; default and maximum 500.</summary>
        public int? BreadcrumbCapacity { get; set; }

        /// <summary>Buffered log lines that are moved to the send queue, default 100, at most 1000.</summary>
        public int? LogBatchSize { get; set; }

        /// <summary>The longest a buffered log line waits before it is queued, default 5 seconds.</summary>
        public TimeSpan? FlushInterval { get; set; }

        /// <summary>Envelopes kept while the ingest is unreachable, default 32; the oldest is dropped past this.</summary>
        public int? MaxQueuedEnvelopes { get; set; }

        /// <summary>
        /// Bytes kept while the ingest is unreachable, default 48 MiB; the oldest is dropped
        /// past this. A soft bound: the newest envelope and the one being sent stay.
        /// </summary>
        public long? MaxQueuedBytes { get; set; }

        /// <summary>Deadline of one request, default 30 seconds, plus one second per 64 KiB of body.</summary>
        public TimeSpan? Timeout { get; set; }

        /// <summary>Whether bodies are gzipped, default true. A runtime without gzip sends them plain.</summary>
        public bool? Compress { get; set; }

        /// <summary>
        /// Allows an <c>http</c> DSN to <c>127.0.0.1</c>, <c>localhost</c>, <c>::1</c> or
        /// <c>10.0.2.2</c>, for a local <c>tracer serve</c>; off by default. Turn it on only in
        /// a debug build: <c>10.0.2.2</c> is an emulator's host alias but an ordinary LAN
        /// address on a phone.
        /// </summary>
        public bool AllowHttpLoopback { get; set; }
    }

    /// <summary>What a report carries besides its message.</summary>
    public sealed class TracerReportOptions
    {
        /// <summary>The event's <c>extra</c>, a JSON object; over 16 KiB it becomes a marker.</summary>
        public JsonValue? Data { get; set; }

        /// <summary>A PNG screenshot; <c>TracerRunner</c> captures one in a Unity build.</summary>
        public byte[]? Screenshot { get; set; }

        /// <summary>Other files, sent after the screenshot.</summary>
        public IReadOnlyList<TracerAttachment>? Attachments { get; set; }

        /// <summary>Becomes the event's exception: its type name, message and stack frames.</summary>
        public Exception? Exception { get; set; }

        /// <summary>
        /// A stack trace text to use instead of <see cref="System.Exception.StackTrace"/> —
        /// Unity's log callback hands one over as a string. Recognised lines become frames.
        /// </summary>
        public string? StackTrace { get; set; }

        /// <summary>The event level, default <see cref="TracerLevel.Warning"/>.</summary>
        public TracerLevel Level { get; set; } = TracerLevel.Warning;
    }
}
