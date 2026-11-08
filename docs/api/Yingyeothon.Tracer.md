# Yingyeothon.Tracer

<!-- Generated from the assembly by tests/Yingyeothon.PublicApi.Tests.
     Do not edit by hand: the test rewrites it and CI compares it. -->

Every public type and member, with its documentation comment — the same text
your IDE shows. For what the package is *for*, read
[the guide](../README.md) and
[`packages/com.yingyeothon.tracer-client/README.md`](../../packages/com.yingyeothon.tracer-client/README.md).

## Contents

- [`ITracerClient`](#interface-itracerclient)
- [`ITracerTransport`](#interface-itracertransport)
- [`TracerAttachment`](#class-tracerattachment)
- [`TracerClient`](#static-class-tracerclient)
- [`TracerDsn`](#class-tracerdsn)
- [`TracerHttpClientTransport`](#static-class-tracerhttpclienttransport)
- [`TracerHttpRequest`](#class-tracerhttprequest)
- [`TracerHttpResponse`](#class-tracerhttpresponse)
- [`TracerLevel`](#enum-tracerlevel)
- [`TracerLimits`](#static-class-tracerlimits)
- [`TracerLogWriter`](#static-class-tracerlogwriter)
- [`TracerOptions`](#class-traceroptions)
- [`TracerReportOptions`](#class-tracerreportoptions)
- [`TracerState`](#enum-tracerstate)
- [`TracerStats`](#class-tracerstats)

## interface ITracerClient

A tracer client: a breadcrumb ring, buffered log lines and a bounded send queue in front of tracer's envelope ingest.

| Member | Summary |
| --- | --- |
| `Breadcrumb(String, String) : Void` | Remembers a step for the next report, at `Info` . Breadcrumbs leave the device only inside an event. |
| `Breadcrumb(String, String, JsonValue, TracerLevel) : Void` | Remembers a step for the next report with a JSON object of `data` . |
| `CloseAsync() : Task` | Flushes for at most the request timeout, then closes; the task completes inside `Poll` . Whatever is left is counted as `closed` . |
| `Enabled : Boolean get` | Whether calls do anything: `State` is `Active` . |
| `Feedback(String, String) : String` | Queues a player's note, linked to the report `eventId` when it is one. Returns the feedback's own id, or null when the client is not active. |
| `FlushAsync() : Task` | Queues the buffered log lines. The task completes inside `Poll` once the queue is empty or waiting out a back-off, and at once when the client is not active. It never faults. |
| `Log(TracerLevel, String) : Void` | Buffers a log line. It is queued by size, after the flush interval, or with the next report. |
| `Log(TracerLevel, String, JsonValue) : Void` | Buffers a log line whose `attributes` (a JSON object) become typed attributes: a whole number within ±2^53 is `integer` , any other number `double` , and an array or object its JSON text as a `string` . |
| `Poll() : Void` | Starts sends, reads their answers and advances the flush and back-off timers. Call it every frame. A call made while another thread is polling returns at once. |
| `Report(String) : String` | Queues an event with the breadcrumb ring and the buffered log lines, sent at the next `Poll` . Returns its event id (32 hex digits) for `Feedback` , or null when the client is not active or the event could not be built. |
| `Report(String, TracerReportOptions) : String` | `Report` with data, a screenshot, other files, an exception and a level. |
| `SetTag(String, String) : Void` | Adds a tag to later events and log lines; a null `value` removes it. Past `MaxTags` a new key is ignored. |
| `State : TracerState get` | Where the client stands. |
| `Stats : TracerStats get` | Counters since construction. |
| `Trace(String, String) : Void` | A breadcrumb and a log line in one call, at `Info` . |
| `Trace(String, String, JsonValue, TracerLevel) : Void` | A breadcrumb and a log line in one call: the trail before something looks wrong, in the report and in the timeline. |
| `TraceId : String get` | The `trace_id` on every event and log line of this client, 32 hex digits. |

## interface ITracerTransport

The HTTP seam the tracer client sends through.

| Member | Summary |
| --- | --- |
| `SendAsync(TracerHttpRequest, CancellationToken) : Task<TracerHttpResponse>` | Sends one envelope and returns whatever status the ingest answered with. |

## class TracerAttachment

A file sent with a report, stored by tracer and readable with `yyt tracer attachments` .

| Member | Summary |
| --- | --- |
| `AttachmentType : String get` | `event.attachment` , `event.view_hierarchy` , `event.minidump` , … |
| `Bytes : Byte[] get` | The bytes, sent as they are. The client keeps the array; do not change it after the call. |
| `ContentType : String get` | MIME type. |
| `Filename : String get` | File name shown by the read API, without a directory. |
| `Screenshot(Byte[]) : TracerAttachment` | A PNG screenshot, `screenshot.png` , as Sentry SDKs name it. |
| `ViewHierarchy(Byte[]) : TracerAttachment` | A view-hierarchy JSON document ( `event.view_hierarchy` ). |
| `ctor(String, Byte[], String, String)` | A file with an explicit name, MIME type and Sentry `attachment_type` . |

## static class TracerClient

Creates tracer clients.

| Member | Summary |
| --- | --- |
| `Create(TracerOptions) : ITracerClient` | Builds a client. A DSN that is empty or does not parse gives a client in `Off` : nothing throws, and the bad DSN is logged as one fixed line that never quotes it. Out-of-range numeric options are clamped. |
| `SdkVersion : String` | The version this library reports in the envelope header ( `sdk.version` ) and in `X-Sentry-Auth` ( `sentry_client=yyt-csharp/<version>` ); the package version. |

## class TracerDsn

A parsed tracer DSN, `https://{writeKey}@{host}/{trc_id}` , as `yyt tracer dsn <res>` prints it.

| Member | Summary |
| --- | --- |
| `EnvelopeUri : Uri get` | The `POST` target, `{base}/api/{trc_id}/envelope/` , trailing slash included. |
| `ResourceId : String get` | The resource id, `trc_…` . |
| `ToString() : String` | The resource id only; the key stays out of every log line. |
| `TryParse(String, Boolean, TracerDsn&) : Boolean` | Parses `dsn` . False when it is not `https://{key}@{host}[:port][/prefix]/{trc_id}` , carries a query or a fragment, the key is shorter than 16 characters, is not printable ASCII or holds one of `, " = \` , or the id is not the console's `trc_` shape. `http` is accepted only when `allowHttpLoopback` is true and the host is `127.0.0.1` , `localhost` , `::1` or `10.0.2.2` . |
| `WriteKey : String get` | The resource's write key; a credential, never logged. |

## static class TracerHttpClientTransport

The default `ITracerTransport` , over `HttpClient` .

| Member | Summary |
| --- | --- |
| `Create(HttpClient) : ITracerTransport` | A transport over a client the caller owns (a proxy, a certificate policy). Build its handler with `AllowAutoRedirect = false` : on a redirect to another host .NET drops `Authorization` but not `X-Sentry-Auth` , so the write key would follow. Its own `Timeout` also bounds every call. |
| `Default : ITracerTransport get` | One shared client for the process, with no default headers, no redirect following — a redirect would carry the write key to whatever host it named — and a timeout of its own of five minutes, a backstop behind the client's per-request deadline. The reply body is not read. |

## class TracerHttpRequest

One envelope `POST` the tracer client wants sent.

| Member | Summary |
| --- | --- |
| `Body : Byte[] get` | The envelope bytes as they go on the wire. |
| `Headers : IReadOnlyList<KeyValuePair<String, String>> get` | `Content-Type` , `X-Sentry-Auth` (the credential) and, when the body is gzipped, `Content-Encoding` , in that order. |
| `Timeout : TimeSpan get` | How long the client will wait. The cancellation token fires at this bound too; this is for a transport whose own timer is the only one that can run (WebGL). |
| `Url : Uri get` | The envelope endpoint. Send `AbsoluteUri` , never `ToString()` , which unescapes. |
| `ctor(Uri, IReadOnlyList<KeyValuePair<String, String>>, Byte[], TimeSpan)` | A request; the client builds these, a transport only reads them. |

## class TracerHttpResponse

What the ingest answered. The body ( `{"id"}` or `{"detail"}` ) is never needed.

| Member | Summary |
| --- | --- |
| `RetryAfter : String get` | The `Retry-After` header as sent, or null. |
| `Status : Int32 get` | The HTTP status code. |
| `ctor(Int32, String)` | An answer. |

## enum TracerLevel

Severity of a breadcrumb, a log line or a report.

- `Debug` — Diagnostic detail.
- `Error` — A failure.
- `Fatal` — A failure the game cannot continue from.
- `Info` — Milestones.
- `Warning` — Something looked wrong; the default level of a report.

## static class TracerLimits

Limits the client keeps under, from the tracer ingest (the service repository's `docs/tracer.md` , and ). Every size is UTF-8 bytes.

| Member | Summary |
| --- | --- |
| `MaxAttachmentsPerEnvelope : Int32` | Attachments per envelope. |
| `MaxBreadcrumbs : Int32` | Breadcrumbs the ingest keeps per event; the default ring size. |
| `MaxDataBytes : Int32` | A log line's attributes, a breadcrumb's data or a report's extra, as JSON: the ingest keeps them only up to this. |
| `MaxEnvelopeBytes : Int32` | The ingest refuses a body over 20 MiB, compressed or not; the client keeps the uncompressed envelope at or under this. |
| `MaxJsonBytesPerEnvelope : Int32` | JSON items one envelope may hold until the ingest commits it. |
| `MaxJsonItemBytes : Int32` | Each JSON item (event, feedback, log container) is dropped by the ingest over 1 MiB. |
| `MaxLogsPerContainer : Int32` | Entries in one log container. |
| `MaxTags : Int32` | Tags the ingest keeps per event. |
| `MaxTextBytes : Int32` | Text the ingest keeps of a message, a log body or a breadcrumb message. |

## static class TracerLogWriter

A `ILogWriter` that forwards every record to a tracer client as a log line with the context as attributes and, optionally, also as a breadcrumb in category `log` . Compose it with `Combine` .

| Member | Summary |
| --- | --- |
| `Create(ITracerClient, Boolean) : ILogWriter` | A writer into `client` ; with `breadcrumbs` each record is also a breadcrumb. |

## class TracerOptions

Options of one tracer client. `Create` copies them.

| Member | Summary |
| --- | --- |
| `AllowHttpLoopback : Boolean get set` | Allows an `http` DSN to `127.0.0.1` , `localhost` , `::1` or `10.0.2.2` , for a local `tracer serve` ; off by default. Turn it on only in a debug build: `10.0.2.2` is an emulator's host alias but an ordinary LAN address on a phone. |
| `BreadcrumbCapacity : Nullable<Int32> get set` | Breadcrumbs kept for the next report, oldest dropped first; default and maximum 500. |
| `Compress : Nullable<Boolean> get set` | Whether bodies are gzipped, default true. A runtime without gzip sends them plain. |
| `Contexts : JsonValue get set` | Extra event `contexts` as a JSON object ( `device` , `os` , `app` , …); `trace` and `feedback` are the client's own and are ignored here. `device.model` is also put on every log line. |
| `Dist : String get set` | A build discriminator; a git sha lets a reader check out the code. |
| `Dsn : String get set` | `https://{writeKey}@{host}/{trc_id}` , from `yyt tracer dsn` through the game's build configuration; never committed. Null or empty means off. |
| `Environment : String get set` | `debug` , `dev` , … |
| `FlushInterval : Nullable<TimeSpan> get set` | The longest a buffered log line waits before it is queued, default 5 seconds. |
| `LogBatchSize : Nullable<Int32> get set` | Buffered log lines that are moved to the send queue, default 100, at most 1000. |
| `Logger : ILogger get set` | The client's own diagnostics (ids, statuses, counts), default `Instance` . |
| `MaxQueuedBytes : Nullable<Int64> get set` | Bytes kept while the ingest is unreachable, default 48 MiB; the oldest is dropped past this. A soft bound: the newest envelope and the one being sent stay. |
| `MaxQueuedEnvelopes : Nullable<Int32> get set` | Envelopes kept while the ingest is unreachable, default 32; the oldest is dropped past this. |
| `NowMillis : Func<Double> get set` | Monotonic milliseconds for deadlines and back-off, default a `Stopwatch` . |
| `Random : Func<Double> get set` | Source of ids and back-off jitter in [0, 1), default a cryptographic generator. |
| `Release : String get set` | `version+build` of the game. |
| `Tags : IReadOnlyDictionary<String, String> get set` | Tags on every event and attributes on every log line; tracer keeps 64. |
| `Timeout : Nullable<TimeSpan> get set` | Deadline of one request, default 30 seconds, plus one second per 64 KiB of body. |
| `Transport : ITracerTransport get set` | The HTTP seam, default `Default` ; a Unity build passes `TracerUnityWebRequestTransport.Instance` . |
| `UserId : String get set` | `user.id` on events and logs: an installation id in a debug build, never a person's name or email. |
| `UtcNow : Func<DateTimeOffset> get set` | Wall clock for event, breadcrumb and log timestamps, default `UtcNow` . |
| `ctor()` |  |

## class TracerReportOptions

What a report carries besides its message.

| Member | Summary |
| --- | --- |
| `Attachments : IReadOnlyList<TracerAttachment> get set` | Other files, sent after the screenshot. |
| `Data : JsonValue get set` | The event's `extra` , a JSON object; over 16 KiB it becomes a marker. |
| `Exception : Exception get set` | Becomes the event's exception: its type name, message and stack frames. |
| `Level : TracerLevel get set` | The event level, default `Warning` . |
| `Screenshot : Byte[] get set` | A PNG screenshot; `TracerRunner` captures one in a Unity build. |
| `StackTrace : String get set` | A stack trace text to use instead of `StackTrace` — Unity's log callback hands one over as a string. Recognised lines become frames. |
| `ctor()` |  |

## enum TracerState

Where a client stands.

- `Active` — Sending: the DSN parsed and the ingest has not refused the key.
- `Closed` — The client was closed or disposed.
- `Off` — Never sending: the DSN was empty or did not parse. Every call is a no-op.
- `Stopped` — The ingest answered `401` : the key is wrong or rotated. Nothing more is sent until the game builds a new client with a new DSN.

## class TracerStats

Counters a client keeps; a snapshot, so read it again for fresh values.

| Member | Summary |
| --- | --- |
| `Dropped : IReadOnlyDictionary<String, Int32> get` | Things the client gave up on, by reason: `too_large` ( `413` , or past a limit), `invalid` ( `400` ), `encoding` ( `415` ), `unauthorized` ( `401` ), `queue_full` (the oldest envelope made room), `http_<status>` , `internal` , `closed` . |
| `EnvelopesSent : Int32 get` | Envelopes the ingest answered with a `2xx` . |
| `Queued : Int32 get` | Envelopes waiting to be sent, the one in flight included. |
| `ctor(Int32, Int32, IReadOnlyDictionary<String, Int32>)` | Builds a snapshot. |
