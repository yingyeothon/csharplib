using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Yingyeothon.Codec;
using Yingyeothon.Logger;

namespace Yingyeothon.Tracer
{
    /// <summary>
    /// The one <see cref="ITracerClient"/>. Every field is guarded by <c>_sync</c>; sends
    /// start and are read only inside <see cref="Poll"/>, and tasks are settled after the
    /// lock is released.
    /// </summary>
    internal sealed class TracerClientImpl : ITracerClient
    {
        private const double BaseBackoffMs = 1000;
        private const double MaxBackoffMs = 5 * 60 * 1000;

        /// <summary>The ingest's quota refusal says 300 s; used when a 429 has no usable <c>Retry-After</c>.</summary>
        private const double DefaultQuotaWaitMs = 300 * 1000;

        private const double MaxRetryAfterMs = 24 * 3600 * 1000;

        /// <summary>Sends started and read in one <see cref="Poll"/>, so a fake that answers at once cannot hold a frame.</summary>
        private const int MaxSendsPerPoll = 16;

        private readonly object _sync = new object();
        private readonly ILogger _logger;
        private readonly Func<double> _nowMs;
        private readonly Func<DateTimeOffset> _utcNow;
        private readonly Func<double> _random;
        private readonly ITracerTransport? _transport;
        private readonly Dictionary<string, string> _tags = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<string> _tagOrder = new List<string>();
        private readonly string? _release;
        private readonly string? _dist;
        private readonly string? _environment;
        private readonly string? _userId;
        private readonly JsonValue? _contexts;
        private readonly string? _deviceModel;
        private readonly int _breadcrumbCapacity;
        private readonly int _logBatchSize;
        private readonly double _flushIntervalMs;
        private readonly int _maxQueuedEnvelopes;
        private readonly long _maxQueuedBytes;
        private readonly TimeSpan _timeout;
        private readonly TracerDsn? _dsn;
        private readonly string _auth = string.Empty;
        private bool _compress;

        private TracerState _state;
        private readonly Queue<JsonValue> _breadcrumbs = new Queue<JsonValue>();
        private readonly List<byte[]> _logs = new List<byte[]>();
        private long _logBytes;
        private double? _logsSince;

        private readonly LinkedList<Queued> _queue = new LinkedList<Queued>();
        private long _queuedBytes;
        private InFlight? _inFlight;
        private double? _resumeAt;
        private int _failures;
        private readonly List<TaskCompletionSource<bool>> _flushWaiters = new List<TaskCompletionSource<bool>>();
        private TaskCompletionSource<bool>? _closeWaiter;
        private double _closeDeadline;

        private int _sent;
        private readonly Dictionary<string, int> _dropped = new Dictionary<string, int>(StringComparer.Ordinal);
        private int _diagThread;
        private int _polling;

        private sealed class Queued
        {
            internal Queued(byte[] body, string kind)
            {
                Body = body;
                Kind = kind;
            }

            internal byte[] Body { get; }

            internal string Kind { get; }
        }

        private sealed class InFlight
        {
            internal InFlight(Queued item, Task<TracerHttpResponse> task, CancellationTokenSource cancel, double deadline, bool compressed)
            {
                Item = item;
                Task = task;
                Cancel = cancel;
                Deadline = deadline;
                Compressed = compressed;
            }

            internal Queued Item { get; }

            internal Task<TracerHttpResponse> Task { get; }

            internal CancellationTokenSource Cancel { get; }

            internal double Deadline { get; }

            internal bool Compressed { get; }
        }

        internal TracerClientImpl(TracerOptions options)
        {
            _logger = options.Logger ?? NullLogger.Instance;
            var stopwatch = Stopwatch.StartNew();
            _nowMs = options.NowMillis ?? (() => stopwatch.Elapsed.TotalMilliseconds);
            _utcNow = options.UtcNow ?? (() => DateTimeOffset.UtcNow);
            _random = options.Random ?? SecureRandom();
            if (options.Tags != null)
            {
                foreach (var tag in options.Tags)
                {
                    if (_tags.Count == TracerLimits.MaxTags)
                    {
                        break;
                    }

                    if (tag.Key != null && tag.Value != null)
                    {
                        PutTag(TracerWire.Clip(tag.Key, 200), TracerWire.Clip(tag.Value, 200));
                    }
                }
            }

            _release = NonEmpty(options.Release);
            _dist = NonEmpty(options.Dist);
            _environment = NonEmpty(options.Environment);
            _userId = NonEmpty(options.UserId);
            if (options.Contexts != null && options.Contexts.Kind == JsonKind.Object)
            {
                _contexts = TracerWire.Safe(options.Contexts);
                var model = _contexts.GetMemberOrNull("device")?.GetMemberOrNull("model");
                if (model != null && model.Kind == JsonKind.String && model.AsString().Length != 0)
                {
                    _deviceModel = TracerWire.Clip(model.AsString(), 200);
                }
            }

            _breadcrumbCapacity = Clamp(options.BreadcrumbCapacity ?? TracerLimits.MaxBreadcrumbs, 0, TracerLimits.MaxBreadcrumbs);
            _logBatchSize = Clamp(options.LogBatchSize ?? 100, 1, TracerLimits.MaxLogsPerContainer);
            var flush = options.FlushInterval ?? TimeSpan.FromSeconds(5);
            _flushIntervalMs = flush <= TimeSpan.Zero ? 0 : Math.Min(flush.TotalMilliseconds, 3600 * 1000);
            _maxQueuedEnvelopes = Clamp(options.MaxQueuedEnvelopes ?? 32, 1, 10000);
            _maxQueuedBytes = Math.Max(1, options.MaxQueuedBytes ?? 48L * 1024 * 1024);
            var timeout = options.Timeout ?? TimeSpan.FromSeconds(30);
            _timeout = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : timeout > TimeSpan.FromMinutes(5) ? TimeSpan.FromMinutes(5) : timeout;
            _compress = options.Compress ?? true;
            TraceId = TracerWire.Hex(_random, 16);

            if (string.IsNullOrWhiteSpace(options.Dsn))
            {
                _state = TracerState.Off;
                return;
            }

            if (!TracerDsn.TryParse(options.Dsn, options.AllowHttpLoopback, out var dsn) || dsn == null)
            {
                _state = TracerState.Off;
                Diag(LogSeverity.Warn, "tracer off: DSN did not parse", null);
                return;
            }

            try
            {
                _transport = options.Transport ?? TracerHttpClientTransport.Default;
            }
            catch (Exception)
            {
                // HttpClient cannot be built on this runtime (a WebGL player).
                _state = TracerState.Off;
                Diag(LogSeverity.Warn, "tracer off: no transport; pass TracerUnityWebRequestTransport.Instance", null);
                return;
            }

            _dsn = dsn;
            _auth = "Sentry sentry_key=" + dsn.WriteKey + ", sentry_version=7, sentry_client=yyt-csharp/" + TracerClient.SdkVersion;
            _state = TracerState.Active;
            Diag(LogSeverity.Info, "tracer active", Json.Object().Set("resource", dsn.ResourceId).Build());
        }

        public string TraceId { get; }

        public TracerState State
        {
            get
            {
                lock (_sync)
                {
                    return _state;
                }
            }
        }

        public bool Enabled => State == TracerState.Active;

        public TracerStats Stats
        {
            get
            {
                lock (_sync)
                {
                    return new TracerStats(_sent, _queue.Count + (_inFlight != null ? 1 : 0), new Dictionary<string, int>(_dropped));
                }
            }
        }

        private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

        private static Func<double> SecureRandom()
        {
            var rng = RandomNumberGenerator.Create();
            var buffer = new byte[4];
            var gate = new object();
            return () =>
            {
                lock (gate)
                {
                    rng.GetBytes(buffer);
                    return (BitConverter.ToUInt32(buffer, 0) >> 8) / 16777216.0;
                }
            };
        }

        private void PutTag(string key, string value)
        {
            if (!_tags.ContainsKey(key))
            {
                _tagOrder.Add(key);
            }

            _tags[key] = value;
        }

        private void Drop(string reason, int count = 1)
        {
            if (count <= 0)
            {
                return;
            }

            _dropped.TryGetValue(reason, out var n);
            _dropped[reason] = n + count;
        }

        /// <summary>Writes a diagnostic; whatever the writer sends back into this client meanwhile (a Unity log hook) is ignored.</summary>
        private void Diag(LogSeverity severity, string message, JsonValue? context)
        {
            if (!_logger.IsEnabled(severity))
            {
                return;
            }

            var thread = Thread.CurrentThread.ManagedThreadId;
            if (Interlocked.CompareExchange(ref _diagThread, thread, 0) != 0)
            {
                return;
            }

            try
            {
                switch (severity)
                {
                    case LogSeverity.Debug:
                        _logger.Debug(message, context);
                        break;
                    case LogSeverity.Info:
                        _logger.Info(message, context);
                        break;
                    case LogSeverity.Warn:
                        _logger.Warn(message, context);
                        break;
                    default:
                        _logger.Error(message, context);
                        break;
                }
            }
            catch (Exception)
            {
                // A broken writer is not the tracer's failure to report.
            }
            finally
            {
                Volatile.Write(ref _diagThread, 0);
            }
        }

        private bool InDiag => Volatile.Read(ref _diagThread) == Thread.CurrentThread.ManagedThreadId;

        /// <summary>Runs <paramref name="body"/> under the lock unless the client is not active or the call came from its own diagnostics, and turns any failure into a count.</summary>
        private T Guard<T>(Func<T> body, T fallback)
        {
            if (InDiag)
            {
                return fallback;
            }

            lock (_sync)
            {
                if (_state != TracerState.Active)
                {
                    return fallback;
                }

                try
                {
                    return body();
                }
                catch (Exception error)
                {
                    Drop("internal");
                    Diag(LogSeverity.Warn, "tracer internal error", Json.Object().Set("type", error.GetType().Name).Build());
                    return fallback;
                }
            }
        }

        public void SetTag(string key, string? value)
        {
            Guard(() =>
            {
                if (key == null)
                {
                    return false;
                }

                var k = TracerWire.Clip(key, 200);
                if (value == null)
                {
                    if (_tags.Remove(k))
                    {
                        _tagOrder.Remove(k);
                    }
                }
                else if (_tags.ContainsKey(k) || _tags.Count < TracerLimits.MaxTags)
                {
                    PutTag(k, TracerWire.Clip(value, 200));
                }

                return true;
            }, false);
        }

        public void Breadcrumb(string category, string message) => Breadcrumb(category, message, null, TracerLevel.Info);

        public void Breadcrumb(string category, string message, JsonValue? data, TracerLevel level)
        {
            Guard(() =>
            {
                if (_breadcrumbCapacity == 0)
                {
                    return false;
                }

                var crumb = Json.Object()
                    .Set("timestamp", TracerWire.Rfc3339(_utcNow()))
                    .Set("type", "default")
                    .Set("category", TracerWire.Clip(category, 200))
                    .Set("level", TracerWire.EventLevel(level))
                    .Set("message", TracerWire.Clip(message, TracerLimits.MaxTextBytes));
                var safe = CapData(data);
                if (safe != null && safe.Kind == JsonKind.Object && safe.AsObject().Count != 0)
                {
                    crumb.Set("data", safe);
                }

                _breadcrumbs.Enqueue(crumb.Build());
                while (_breadcrumbs.Count > _breadcrumbCapacity)
                {
                    _breadcrumbs.Dequeue();
                }

                return true;
            }, false);
        }

        public void Log(TracerLevel level, string message) => Log(level, message, null);

        public void Log(TracerLevel level, string message, JsonValue? attributes)
        {
            Guard(() =>
            {
                var attrs = BaseAttributes();
                // The ingest keeps a line's attributes only up to 16 KiB of JSON, all or
                // nothing: the base ones first, then the caller's while they fit.
                var size = TracerWire.GoJsonLength(Json.Stringify(JsonValue.Object(attrs)));
                var reserved = new HashSet<string>(StringComparer.Ordinal);
                foreach (var a in attrs)
                {
                    reserved.Add(a.Key);
                }

                var cut = false;
                if (attributes != null && attributes.Kind == JsonKind.Object)
                {
                    foreach (var member in attributes.AsObject())
                    {
                        var typed = TracerWire.Typed(member.Value);
                        if (typed == null)
                        {
                            continue;
                        }

                        var key = TracerWire.Clip(member.Key, 200);
                        // The base attributes are what tracer fills its correlation columns from.
                        if (!reserved.Add(key))
                        {
                            continue;
                        }

                        var add = TracerWire.GoJsonLength(Json.Stringify(Json.Object().Set(key, typed).Build()));
                        if (size + add > TracerLimits.MaxDataBytes - 64)
                        {
                            cut = true;
                            continue;
                        }

                        attrs.Add(new KeyValuePair<string, JsonValue>(key, typed));
                        size += add;
                    }
                }

                if (cut)
                {
                    attrs.Add(new KeyValuePair<string, JsonValue>("tracer.truncated", TracerWire.Attr(JsonValue.Of(true), "boolean")));
                }

                var entry = TracerWire.JsonBytes(Json.Object()
                    .Set("timestamp", TracerWire.EpochSeconds(_utcNow()))
                    .Set("trace_id", TraceId)
                    .Set("level", TracerWire.LogLevel(level))
                    .Set("body", TracerWire.Clip(message, TracerLimits.MaxTextBytes))
                    .Set("severity_number", JsonValue.Of(TracerWire.SeverityNumber(level)))
                    .Set("attributes", JsonValue.Object(attrs))
                    .Build());
                _logs.Add(entry);
                _logBytes += entry.Length + 1;
                if (_logs.Count >= _logBatchSize || _logBytes >= TracerLimits.MaxJsonItemBytes / 2)
                {
                    FlushLogs();
                }
                else if (_logsSince == null)
                {
                    _logsSince = _nowMs();
                }

                return true;
            }, false);
        }

        public void Trace(string category, string message) => Trace(category, message, null, TracerLevel.Info);

        public void Trace(string category, string message, JsonValue? data, TracerLevel level)
        {
            Breadcrumb(category, message, data, level);
            var attributes = new List<KeyValuePair<string, JsonValue>>
            {
                new KeyValuePair<string, JsonValue>("category", JsonValue.Of(category ?? string.Empty)),
            };
            if (data != null && data.Kind == JsonKind.Object)
            {
                attributes.AddRange(data.AsObject());
            }

            Log(level, message, JsonValue.Object(attributes));
        }

        private List<KeyValuePair<string, JsonValue>> BaseAttributes()
        {
            var attrs = new List<KeyValuePair<string, JsonValue>>();
            void Str(string key, string value) => attrs.Add(new KeyValuePair<string, JsonValue>(key, TracerWire.Attr(JsonValue.Of(value), "string")));
            Str("sentry.sdk.name", "yyt.csharp");
            Str("sentry.sdk.version", TracerClient.SdkVersion);
            if (_release != null)
            {
                Str("sentry.release", _release);
            }

            if (_environment != null)
            {
                Str("sentry.environment", _environment);
            }

            if (_dist != null)
            {
                Str("sentry.dist", _dist);
            }

            if (_userId != null)
            {
                Str("user.id", _userId);
            }

            if (_deviceModel != null)
            {
                Str("device.model", _deviceModel);
            }

            foreach (var key in _tagOrder)
            {
                Str(key, _tags[key]);
            }

            return attrs;
        }

        /// <summary>A safe copy of <paramref name="data"/> when its JSON fits what the ingest keeps, else a marker.</summary>
        private static JsonValue? CapData(JsonValue? data)
        {
            if (data == null || data.Kind != JsonKind.Object)
            {
                return null;
            }

            var safe = TracerWire.Safe(data);
            return TracerWire.JsonBytes(safe).Length > TracerLimits.MaxDataBytes
                ? Json.Object().Set("tracer.truncated", true).Build()
                : safe;
        }

        private JsonObjectBuilder EventBase(string eventId, TracerLevel level)
        {
            var builder = Json.Object()
                .Set("event_id", eventId)
                .Set("timestamp", TracerWire.Rfc3339(_utcNow()))
                .Set("platform", "csharp")
                .Set("level", TracerWire.EventLevel(level))
                .Set("release", _release)
                .Set("dist", _dist)
                .Set("environment", _environment);
            if (_tags.Count != 0)
            {
                var tags = Json.Object();
                foreach (var key in _tagOrder)
                {
                    tags.Set(key, _tags[key]);
                }

                builder.Set("tags", tags.Build());
            }

            if (_userId != null)
            {
                builder.Set("user", Json.Object().Set("id", _userId).Build());
            }

            return builder;
        }

        private JsonValue Contexts(string? ownKey, JsonValue? own)
        {
            var builder = Json.Object();
            if (_contexts != null)
            {
                foreach (var member in _contexts.AsObject())
                {
                    if (member.Key != "trace" && member.Key != "feedback")
                    {
                        builder.Set(member.Key, member.Value);
                    }
                }
            }

            builder.Set("trace", Json.Object().Set("trace_id", TraceId).Set("span_id", TracerWire.Hex(_random, 8)).Build());
            if (ownKey != null)
            {
                builder.Set(ownKey, own);
            }

            return builder.Build();
        }

        /// <summary>
        /// The event payload under the JSON item cap: the oldest breadcrumbs go first, then
        /// <c>extra</c>, then the exception. Null when it still does not fit (its
        /// attachments would be stored without an event).
        /// </summary>
        private static byte[]? EventPayload(List<KeyValuePair<string, JsonValue>> members, List<JsonValue>? crumbs)
        {
            const int cap = TracerLimits.MaxJsonItemBytes - TracerWire.ItemOverhead;
            byte[] Encode()
            {
                var copy = new List<KeyValuePair<string, JsonValue>>(members.Count);
                foreach (var m in members)
                {
                    copy.Add(m.Key == "breadcrumbs" && crumbs != null
                        ? new KeyValuePair<string, JsonValue>("breadcrumbs", Json.Object().Set("values", JsonValue.Array(crumbs)).Build())
                        : m);
                }

                return TracerWire.JsonBytes(JsonValue.Object(copy));
            }

            var payload = Encode();
            while (payload.Length > cap && crumbs != null && crumbs.Count != 0)
            {
                crumbs.RemoveRange(0, (crumbs.Count + 1) / 2);
                payload = Encode();
            }

            foreach (var key in new[] { "extra", "exception" })
            {
                if (payload.Length <= cap)
                {
                    break;
                }

                members.RemoveAll(m => m.Key == key);
                payload = Encode();
            }

            return payload.Length <= cap ? payload : null;
        }

        public string? Report(string message) => Report(message, new TracerReportOptions());

        public string? Report(string message, TracerReportOptions options)
        {
            return Guard<string?>(() =>
            {
                var o = options ?? new TracerReportOptions();
                var eventId = TracerWire.Hex(_random, 16);
                var members = new List<KeyValuePair<string, JsonValue>>(EventBase(eventId, o.Level).Build().AsObject())
                {
                    new KeyValuePair<string, JsonValue>("message", JsonValue.Of(TracerWire.Clip(message, TracerLimits.MaxTextBytes))),
                    new KeyValuePair<string, JsonValue>("contexts", Contexts(null, null)),
                };
                var extra = CapData(o.Data);
                if (extra != null && extra.AsObject().Count != 0)
                {
                    members.Add(new KeyValuePair<string, JsonValue>("extra", extra));
                }

                if (o.Exception != null || o.StackTrace != null)
                {
                    members.Add(new KeyValuePair<string, JsonValue>("exception", ExceptionValue(o.Exception, o.StackTrace)));
                }

                var crumbs = new List<JsonValue>(_breadcrumbs);
                members.Add(new KeyValuePair<string, JsonValue>("breadcrumbs", JsonValue.Null));
                members.Add(new KeyValuePair<string, JsonValue>("sdk", TracerWire.Sdk));
                var payload = EventPayload(members, crumbs);
                if (payload == null)
                {
                    Drop("too_large");
                    return null;
                }

                var items = new List<WireItem> { new WireItem(Json.Object().Set("type", "event").Build(), payload) };
                var json = payload.Length + TracerWire.ItemOverhead;
                var logs = TakeLogs(TracerLimits.MaxJsonBytesPerEnvelope - json);
                if (logs != null)
                {
                    items.Add(logs);
                    json += logs.Payload.Length + TracerWire.ItemOverhead;
                }

                // A margin for gzip framing: a PNG does not compress, and the ingest caps the
                // body as sent too.
                long room = TracerLimits.MaxEnvelopeBytes - 64 * 1024 - json;
                var files = new List<TracerAttachment>();
                if (o.Screenshot != null)
                {
                    files.Add(TracerAttachment.Screenshot(o.Screenshot));
                }

                if (o.Attachments != null)
                {
                    foreach (var file in o.Attachments)
                    {
                        if (file != null)
                        {
                            files.Add(file);
                        }
                    }
                }

                var count = 0;
                foreach (var file in files)
                {
                    var size = (long)file.Bytes.Length + TracerWire.ItemOverhead;
                    if (count == TracerLimits.MaxAttachmentsPerEnvelope || size > room)
                    {
                        Drop("too_large");
                        continue;
                    }

                    items.Add(new WireItem(Json.Object()
                        .Set("type", "attachment")
                        .Set("filename", TracerWire.Clip(file.Filename, 200))
                        .Set("content_type", TracerWire.Clip(file.ContentType, 100))
                        .Set("attachment_type", TracerWire.Clip(file.AttachmentType, 64))
                        .Build(), file.Bytes));
                    room -= size;
                    count++;
                }

                Enqueue(Envelope(eventId, items), "event");
                FlushLogs();
                return eventId;
            }, null);
        }

        private static JsonValue ExceptionValue(Exception? error, string? stackTrace)
        {
            string type = "Exception";
            string value = string.Empty;
            if (error != null)
            {
                type = error.GetType().FullName ?? error.GetType().Name;
                try
                {
                    value = error.Message ?? string.Empty;
                }
                catch (Exception)
                {
                    value = "<unprintable>";
                }

                if (stackTrace == null)
                {
                    try
                    {
                        stackTrace = error.StackTrace;
                    }
                    catch (Exception)
                    {
                        stackTrace = null;
                    }
                }
            }

            var entry = Json.Object()
                .Set("type", TracerWire.Clip(type, 200))
                .Set("value", TracerWire.Clip(value, TracerLimits.MaxTextBytes));
            var frames = TracerWire.Frames(stackTrace);
            if (frames.Count != 0)
            {
                entry.Set("stacktrace", Json.Object().Set("frames", JsonValue.Array(frames)).Build());
            }

            return Json.Object().Set("values", JsonValue.ArrayOf(entry.Build())).Build();
        }

        public string? Feedback(string note, string? eventId)
        {
            return Guard<string?>(() =>
            {
                var id = TracerWire.Hex(_random, 16);
                var feedback = Json.Object().Set("message", TracerWire.Clip(note, TracerLimits.MaxTextBytes));
                var associated = eventId?.Replace("-", string.Empty).ToLowerInvariant();
                if (associated != null && IsEventId(associated))
                {
                    feedback.Set("associated_event_id", associated);
                }

                var members = new List<KeyValuePair<string, JsonValue>>(EventBase(id, TracerLevel.Info).Build().AsObject())
                {
                    new KeyValuePair<string, JsonValue>("contexts", Contexts("feedback", feedback.Build())),
                    new KeyValuePair<string, JsonValue>("sdk", TracerWire.Sdk),
                };
                var payload = EventPayload(members, null);
                if (payload == null)
                {
                    Drop("too_large");
                    return null;
                }

                Enqueue(Envelope(id, new List<WireItem> { new WireItem(Json.Object().Set("type", "feedback").Build(), payload) }), "feedback");
                return id;
            }, null);
        }

        private static bool IsEventId(string id)
        {
            if (id.Length != 32)
            {
                return false;
            }

            foreach (var c in id)
            {
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f'))
                {
                    return false;
                }
            }

            return true;
        }

        private byte[] Envelope(string? eventId, IReadOnlyList<WireItem> items)
        {
            var header = Json.Object();
            if (eventId != null)
            {
                header.Set("event_id", eventId);
            }

            header.Set("sent_at", TracerWire.Rfc3339(_utcNow())).Set("sdk", TracerWire.Sdk);
            return TracerWire.Envelope(header.Build(), items);
        }

        /// <summary>One log container from the oldest buffered lines that fit <paramref name="budget"/> bytes; null when none is buffered or fits.</summary>
        private WireItem? TakeLogs(int budget)
        {
            var cap = Math.Min(budget, TracerLimits.MaxJsonItemBytes) - TracerWire.ItemOverhead;
            var size = "{\"items\":[]}".Length;
            var n = 0;
            while (n < _logs.Count && n < TracerLimits.MaxLogsPerContainer && size + _logs[n].Length + 1 <= cap)
            {
                size += _logs[n].Length + 1;
                n++;
            }

            if (n == 0)
            {
                return null;
            }

            var taken = _logs.GetRange(0, n);
            _logs.RemoveRange(0, n);
            _logBytes = 0;
            foreach (var entry in _logs)
            {
                _logBytes += entry.Length + 1;
            }

            if (_logs.Count == 0)
            {
                _logsSince = null;
            }

            var payload = new byte[size - 1];
            var open = TracerWire.Text("{\"items\":[");
            Buffer.BlockCopy(open, 0, payload, 0, open.Length);
            var at = open.Length;
            for (var i = 0; i < taken.Count; i++)
            {
                if (i > 0)
                {
                    payload[at++] = (byte)',';
                }

                Buffer.BlockCopy(taken[i], 0, payload, at, taken[i].Length);
                at += taken[i].Length;
            }

            payload[at++] = (byte)']';
            payload[at] = (byte)'}';
            return new WireItem(Json.Object()
                .Set("type", "log")
                .Set("item_count", JsonValue.Of(taken.Count))
                .Set("content_type", TracerWire.LogContentType)
                .Build(), payload);
        }

        /// <summary>Moves every buffered log line into envelopes of their own.</summary>
        private void FlushLogs()
        {
            _logsSince = null;
            while (_logs.Count != 0)
            {
                var item = TakeLogs(TracerLimits.MaxJsonItemBytes);
                if (item == null)
                {
                    // A single line over the cap cannot be sent.
                    _logBytes -= _logs[0].Length + 1;
                    _logs.RemoveAt(0);
                    Drop("too_large");
                    continue;
                }

                Enqueue(Envelope(null, new List<WireItem> { item }), "log");
            }
        }

        private void Enqueue(byte[] body, string kind)
        {
            _queue.AddLast(new Queued(body, kind));
            _queuedBytes += body.Length;
            TrimQueue();
        }

        /// <summary>Drops the oldest queued envelopes past either bound, keeping the newest.</summary>
        private void TrimQueue()
        {
            while (_queue.Count > 1 && (_queue.Count > _maxQueuedEnvelopes || _queuedBytes > _maxQueuedBytes))
            {
                _queuedBytes -= _queue.First!.Value.Body.Length;
                _queue.RemoveFirst();
                Drop("queue_full");
            }
        }

        public Task FlushAsync()
        {
            if (InDiag)
            {
                return Task.CompletedTask;
            }

            lock (_sync)
            {
                if (_state != TracerState.Active)
                {
                    return Task.CompletedTask;
                }

                try
                {
                    FlushLogs();
                }
                catch (Exception)
                {
                    Drop("internal");
                }

                if (Idle || _resumeAt != null)
                {
                    return Task.CompletedTask;
                }

                var waiter = new TaskCompletionSource<bool>();
                _flushWaiters.Add(waiter);
                return waiter.Task;
            }
        }

        private bool Idle => _queue.Count == 0 && _inFlight == null;

        public Task CloseAsync()
        {
            if (InDiag)
            {
                return Task.CompletedTask;
            }

            List<TaskCompletionSource<bool>>? settle = null;
            lock (_sync)
            {
                if (_closeWaiter != null)
                {
                    return _closeWaiter.Task;
                }

                if (_state == TracerState.Active)
                {
                    try
                    {
                        FlushLogs();
                    }
                    catch (Exception)
                    {
                        Drop("internal");
                    }

                    _closeWaiter = new TaskCompletionSource<bool>();
                    _closeDeadline = _nowMs() + _timeout.TotalMilliseconds;
                    return _closeWaiter.Task;
                }

                if (_state != TracerState.Closed)
                {
                    settle = CloseLocked();
                }
            }

            Settle(settle);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            List<TaskCompletionSource<bool>>? settle;
            lock (_sync)
            {
                if (_state == TracerState.Closed)
                {
                    return;
                }

                settle = CloseLocked();
            }

            Settle(settle);
        }

        /// <summary>Stops for good; returns the waiters to settle once the lock is released.</summary>
        private List<TaskCompletionSource<bool>> CloseLocked()
        {
            _state = TracerState.Closed;
            Shutdown();
            if (_inFlight != null)
            {
                Drop("closed");
                AbortInFlight();
            }

            Drop("closed", _queue.Count);
            _queue.Clear();
            _queuedBytes = 0;
            var settle = new List<TaskCompletionSource<bool>>(_flushWaiters);
            _flushWaiters.Clear();
            if (_closeWaiter != null)
            {
                settle.Add(_closeWaiter);
            }

            return settle;
        }

        private void AbortInFlight()
        {
            var flight = _inFlight;
            _inFlight = null;
            if (flight == null)
            {
                return;
            }

            try
            {
                flight.Cancel.Cancel();
            }
            catch (Exception)
            {
                // A transport's cancellation callback failed; the request is abandoned anyway.
            }

            // Observe a late fault so it never reaches the unobserved-task handler.
            flight.Task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            flight.Cancel.Dispose();
        }

        private void Shutdown()
        {
            _resumeAt = null;
            _logsSince = null;
            Drop("closed", _logs.Count);
            _logs.Clear();
            _logBytes = 0;
            _breadcrumbs.Clear();
        }

        private static void Settle(List<TaskCompletionSource<bool>>? waiters)
        {
            if (waiters == null)
            {
                return;
            }

            foreach (var waiter in waiters)
            {
                waiter.TrySetResult(true);
            }
        }

        public void Poll()
        {
            if (InDiag || Interlocked.Exchange(ref _polling, 1) != 0)
            {
                return;
            }

            List<TaskCompletionSource<bool>>? settle = null;
            try
            {
                lock (_sync)
                {
                    try
                    {
                        settle = PollLocked();
                    }
                    catch (Exception error)
                    {
                        Drop("internal");
                        Diag(LogSeverity.Warn, "tracer poll failed", Json.Object().Set("type", error.GetType().Name).Build());
                    }
                }
            }
            finally
            {
                Volatile.Write(ref _polling, 0);
            }

            Settle(settle);
        }

        private List<TaskCompletionSource<bool>>? PollLocked()
        {
            if (_state != TracerState.Active)
            {
                return null;
            }

            var now = _nowMs();
            if (_logsSince != null && now - _logsSince.Value >= _flushIntervalMs)
            {
                FlushLogs();
            }

            if (_resumeAt != null && now >= _resumeAt.Value)
            {
                _resumeAt = null;
            }

            for (var i = 0; i < MaxSendsPerPoll && _state == TracerState.Active; i++)
            {
                if (_inFlight != null && !ReadAnswer(now))
                {
                    break;
                }

                if (_inFlight != null || _resumeAt != null || _queue.Count == 0)
                {
                    break;
                }

                StartSend(now);
            }

            List<TaskCompletionSource<bool>>? settle = null;
            if (_state != TracerState.Active)
            {
                // A 401 inside this pass stopped the client.
                settle = new List<TaskCompletionSource<bool>>(_flushWaiters);
                _flushWaiters.Clear();
                if (_closeWaiter != null)
                {
                    settle.AddRange(CloseLocked());
                }

                return settle;
            }

            if (_flushWaiters.Count != 0 && (Idle || _resumeAt != null))
            {
                settle = new List<TaskCompletionSource<bool>>(_flushWaiters);
                _flushWaiters.Clear();
            }

            if (_closeWaiter != null && (Idle || _resumeAt != null || now >= _closeDeadline))
            {
                settle ??= new List<TaskCompletionSource<bool>>();
                settle.AddRange(CloseLocked());
            }

            return settle;
        }

        private void StartSend(double now)
        {
            var item = _queue.First!.Value;
            _queue.RemoveFirst();
            _queuedBytes -= item.Body.Length;
            var body = item.Body;
            var compressed = false;
            if (_compress)
            {
                try
                {
                    body = TracerWire.Gzip(item.Body);
                    compressed = true;
                }
                catch (Exception)
                {
                    // No zlib on this runtime (a WebGL player): send plain from now on.
                    _compress = false;
                    Diag(LogSeverity.Info, "tracer sends uncompressed: gzip unavailable", null);
                    if (_state != TracerState.Active)
                    {
                        // The logger closed the client from inside that line.
                        Drop("closed");
                        return;
                    }
                }
            }

            var headers = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Content-Type", "application/x-sentry-envelope"),
                new KeyValuePair<string, string>("X-Sentry-Auth", _auth),
            };
            if (compressed)
            {
                headers.Add(new KeyValuePair<string, string>("Content-Encoding", "gzip"));
            }

            var timeout = _timeout + TimeSpan.FromSeconds(body.Length >> 16);
            var cancel = new CancellationTokenSource();
            Task<TracerHttpResponse> task;
            try
            {
                cancel.CancelAfter(timeout);
                task = _transport!.SendAsync(new TracerHttpRequest(_dsn!.EnvelopeUri, headers, body, timeout), cancel.Token)
                    ?? throw new InvalidOperationException("transport returned no task");
            }
            catch (Exception error)
            {
                var source = new TaskCompletionSource<TracerHttpResponse>();
                source.SetException(error);
                task = source.Task;
            }

            _inFlight = new InFlight(item, task, cancel, now + timeout.TotalMilliseconds, compressed);
        }

        /// <summary>Reads the in-flight answer when there is one; false while it is still pending.</summary>
        private bool ReadAnswer(double now)
        {
            var flight = _inFlight!;
            if (!flight.Task.IsCompleted && now < flight.Deadline)
            {
                return false;
            }

            if (!flight.Task.IsCompleted)
            {
                AbortInFlight();
                Retry(flight.Item, Backoff(), "timeout");
                return true;
            }

            _inFlight = null;
            flight.Cancel.Dispose();
            if (flight.Task.Status != TaskStatus.RanToCompletion || flight.Task.Result == null)
            {
                var type = flight.Task.Exception?.GetBaseException().GetType().Name ?? "cancelled";
                Retry(flight.Item, Backoff(), type);
                return true;
            }

            var response = flight.Task.Result;
            var status = response.Status;
            if (_logger.IsEnabled(LogSeverity.Debug))
            {
                Diag(LogSeverity.Debug, "tracer envelope", Json.Object()
                    .Set("kind", flight.Item.Kind)
                    .Set("status", status)
                    .Set("bytes", flight.Item.Body.Length)
                    .Build());
            }

            if (status >= 200 && status < 300)
            {
                _sent++;
                _failures = 0;
                return true;
            }

            switch (status)
            {
                case 401:
                    _state = TracerState.Stopped;
                    Diag(LogSeverity.Warn, "tracer stopped: the ingest refused the key", Json.Object().Set("resource", _dsn!.ResourceId).Build());
                    Drop("unauthorized", 1 + _queue.Count);
                    _queue.Clear();
                    _queuedBytes = 0;
                    Shutdown();
                    return true;
                case 429:
                    Requeue(flight.Item, RetryAfter(response.RetryAfter) ?? DefaultQuotaWaitMs);
                    return true;
                case 400:
                    Drop("invalid");
                    return true;
                case 413:
                    Drop("too_large");
                    return true;
                case 415 when flight.Compressed:
                    _compress = false;
                    Requeue(flight.Item, 0);
                    return true;
                case 415:
                    Drop("encoding");
                    return true;
                default:
                    if (status >= 500)
                    {
                        Requeue(flight.Item, RetryAfter(response.RetryAfter) ?? Backoff());
                    }
                    else
                    {
                        Drop("http_" + status.ToString(CultureInfo.InvariantCulture));
                    }

                    return true;
            }
        }

        private void Retry(Queued item, double waitMs, string type)
        {
            Diag(LogSeverity.Debug, "tracer send failed", Json.Object()
                .Set("type", type)
                .Set("kind", item.Kind)
                .Set("retryMs", Math.Round(waitMs))
                .Build());
            Requeue(item, waitMs);
        }

        private void Requeue(Queued item, double waitMs)
        {
            _queue.AddFirst(item);
            _queuedBytes += item.Body.Length;
            TrimQueue();
            if (waitMs > 0)
            {
                _resumeAt = _nowMs() + waitMs;
            }
        }

        private double Backoff()
        {
            var shift = Math.Min(_failures, 9);
            _failures++;
            var ms = Math.Min(BaseBackoffMs * (1 << shift), MaxBackoffMs);
            return Math.Round(ms * (0.5 + 0.5 * _random()));
        }

        /// <summary>Whole seconds, at most a day. Zero, negative or unparsable is absent: zero would resend at once, forever.</summary>
        private static double? RetryAfter(string? header)
        {
            if (header == null || !long.TryParse(header.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
            {
                return null;
            }

            return Math.Min(seconds * 1000.0, MaxRetryAfterMs);
        }
    }
}
