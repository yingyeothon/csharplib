# Yingyeothon.Tracer

The client of **tracer**, the yyt platform's debug-data collector: a breadcrumb ring,
buffered log lines, reports with a screenshot and other files, and player feedback, sent
as envelopes to tracer's Sentry-compatible ingest. It needs no Sentry package, never
throws into the game, and is off when the DSN is empty. In Unity, `TracerRunner` polls
it, turns Unity's log messages and scene loads into breadcrumbs and log lines, and
captures the screenshot.

The wire contract is the [`service`](https://github.com/yingyeothon/service)
repository's `docs/tracer.md`, _Wire contract for client libraries_; tracer's `tracer/internal/` there is the reference implementation.
It is debug tooling, not crash reporting: nothing here claims to catch a crash.

## Install

```
https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.tracer-client#v0.3.1
```

The fragment pins a release tag; keep the same one on every `com.yingyeothon.*` URL
([Unity § Installing](../../docs/unity.md#installing)).

## Usage

The package depends on `com.yingyeothon.codec` and `com.yingyeothon.logger`; a git-URL
package cannot resolve them, so install both first, on the same tag.

```csharp
using UnityEngine;
using Yingyeothon.Codec;
using Yingyeothon.Tracer;

public sealed class Diagnostics : MonoBehaviour
{
    private ITracerClient _tracer;
    private TracerRunner _runner;

    private void Awake()
    {
        // Written by the build (yyt tracer dsn <res> > Assets/Resources/tracer-dsn.txt,
        // git-ignored); never typed into the inspector, which a scene commits.
        var file = Resources.Load<TextAsset>("tracer-dsn");
        _tracer = TracerClient.Create(new TracerOptions
        {
            Dsn = file != null ? file.text.Trim() : "",  // empty → off
            Release = Application.version,
            Dist = null,                                  // your build's git sha, if you have one
            Environment = "debug",
            Contexts = TracerRunner.DeviceContexts(),     // OS and device model columns
            Transport = TracerUnityWebRequestTransport.Instance,
        });
        _runner = TracerRunner.CreatePersistent(_tracer, true); // Poll, Unity logs, scenes
    }

    public async void RejectedMove(string fen)
    {
        _tracer.Trace("ui", "tap square", Json.Object().Set("square", "e2").Build(), TracerLevel.Info);
        string? id = await _runner.ReportWithScreenshot("rejected a legal move", new TracerReportOptions
        {
            Data = Json.Object().Set("board", fen).Build(),
        });
        _tracer.Feedback("the knight vanished", id);
    }
}
```

Read reports back with `yyt tracer events <res> --since 10m`, log lines with
`yyt tracer logs <res>`, or the `tracer-debug` skill (`yyt tracer` ships in the CLI from
`cli/v0.20.0`; `yyt tracer dsn` needs a resource the console created; `--profile dev`
for a dev one). Release builds normally leave the
DSN empty; the team decides per build. **Leave it empty in a WebGL build** — see
[Unity](#unity).

## Poll, or nothing is sent

Every call only records: a breadcrumb goes into the ring, a log line into a buffer, a
report or feedback onto the send queue. **Sends start, answers are read and timers run
only inside `Poll()`.** `TracerRunner` calls it from `Update()`; without the runner, call
it yourself every frame. That is what lets the same client run on a WebGL player, which
has no timer thread, and it means `FlushAsync` and `CloseAsync` complete inside `Poll()`
too. Calls may come from any thread; a `Poll()` that overlaps another returns at once.

`TracerRunner` also queues the buffered lines and starts a send when the app is paused
(`OnApplicationPause(true)`) or quits (`OnApplicationQuit`); no `Update()` runs after
that, so it sends only what one `Poll()` can start — one envelope. `Dispose()` stops at
once and counts the rest as `closed`.

## Behaviour

- **Envelopes.** Header `{event_id?, sent_at, sdk: {name: "yyt.csharp", version}}`,
  never the DSN; every item has an explicit `length`; bodies are gzipped where the
  runtime has zlib (a runtime that throws sends them plain from then on).
  `X-Sentry-Auth: Sentry sentry_key=…, sentry_version=7, sentry_client=yyt-csharp/<version>`.
- **A report** is one envelope: the event (message, level, `release`, `dist`,
  `environment`, tags, `user.id`, `contexts` with `trace`, `extra`, the breadcrumb ring,
  and an exception with frames when you pass `Exception` or a `StackTrace` text), the
  buffered log lines that fit, then the attachments. Attachments ride the report's
  envelope because tracer links a file to the envelope header's `event_id`.
- **Frames** come from the stack-trace text of .NET, Mono, IL2CPP and Unity's log
  callback, oldest first. `filename` is the path from `Assets/` or `Packages/` on (else
  the file name), because the read API shows it and tracer groups by it; a frame whose
  file IL2CPP could not resolve keeps only its function; no `abs_path` is sent, since a
  build machine's path names its user. `System.*`, `UnityEngine.*`, `UnityEditor.*`,
  `Mono.*`, `Yingyeothon.Tracer.*` and Mono's `(wrapper …)` frames are not `in_app`.
- **Feedback** is its own envelope with its own id and `contexts.feedback {message,
  associated_event_id}`, without breadcrumbs (the report it links to has them). An
  `eventId` that is not 32 hex digits (dashes ignored) is left out.
- **Log lines** carry typed attributes — `boolean`, `integer` (a whole number within
  ±2^53), `double`, `string`; an array or object is sent as its JSON text — plus
  `sentry.release`, `sentry.environment`, `sentry.dist`, `user.id`, `device.model` (from
  `Contexts.device.model`) and the tags; a key of yours that repeats one of these is
  dropped, so a caller cannot rewrite the columns tracer correlates by. tracer keeps a
  line's attributes only up to 16 KiB of JSON as Go writes it (`<`, `>`, `&` take six
  bytes), so yours are added while they fit and the line is marked `tracer.truncated`; a breadcrumb's data or a report's `Data` over 16 KiB becomes
  `{"tracer.truncated": true}`, and tags stop at 64. A buffer is queued at
  `LogBatchSize` (100) lines, half a mebibyte, after `FlushInterval` (5 s), or with the
  next report.
- **Limits** (`TracerLimits`, from tracer's ingest): ≤ 1000 lines per log container,
  ≤ 1 MiB per JSON item (an oversized event drops its oldest breadcrumbs, then `extra`,
  then the exception; one that still does not fit is not sent, so its attachments are
  not stored alone), ≤ 2 MiB of JSON per envelope, ≤ 20 attachments and 20 MiB less a
  64 KiB gzip margin per envelope; what does not fit is dropped and counted.
- **Responses.** `2xx` sent; `401` stops the client (`TracerState.Stopped`) until the
  game builds a new one with a new DSN; `429` waits `Retry-After` (300 s without a
  usable one, `0` included), never less; `503`, other `5xx`, a network failure and a
  request past its deadline wait `Retry-After` or a jittered back-off (1 s doubling to
  5 min, reset by a success); `400`, `413` and other `4xx` drop and count; `415` resends
  once without gzip. One request is in flight at a time. The queue keeps
  `MaxQueuedEnvelopes` (32) and `MaxQueuedBytes` (48 MiB); the oldest envelope makes
  room, and the bound is soft (the newest stays). A request's deadline is `Timeout`
  (30 s) plus a second per 64 KiB of body. `FlushAsync` completes when the queue is
  empty or waiting out a back-off; `CloseAsync` gives it one `Timeout`.
- **DSN.** `https://{writeKey}@{host}/{trc_id}`; `http` only to `127.0.0.1`,
  `localhost`, `::1` or `10.0.2.2`, and only with `AllowHttpLoopback` (off by default —
  `10.0.2.2` is an emulator's host alias but an ordinary LAN address on a phone). A DSN
  that does not parse turns the client off and logs one fixed line; the key is never
  logged, and `TracerDsn.ToString()` prints only the resource id.

## Unity

- `TracerRunner.CreatePersistent(client, captureUnityLogs)` makes a `DontDestroyOnLoad`
  object that polls the client. With `captureUnityLogs` it forwards
  `Application.logMessageReceived` as a breadcrumb (category `unity.log`) and a log line
  (attribute `unity.log_type`; an exception's stack text as `stacktrace`), and each
  `SceneManager.sceneLoaded` as a breadcrumb (category `scene`), while the client is
  active. Unity raises that event for main-thread logs only. An exception Unity logs is
  a log line, never an event: send a report when the game notices something wrong.
  What the client's own `Logger` writes through `Debug.Log` on the main thread is
  ignored, so pointing that logger at the Unity console does not loop; a diagnostic
  written on another thread comes back once, as an ordinary line. The runner does not
  own the client: dispose it yourself, and destroy the runner's GameObject when you
  replace the client (after a `401`, say).
- `TracerRunner.DeviceContexts()` gives `os` and `device` contexts from `SystemInfo`
  for `TracerOptions.Contexts`; without them tracer's OS and device columns stay empty.
- `ReportWithScreenshot(message, options)` captures the screen at the end of the frame
  (`ScreenCapture.CaptureScreenshotAsTexture`, encoded as PNG — a frame's work on a
  phone, and it shows whatever is on screen, a "report" dialog included) and sends the
  report with it. A failed capture, or a runner disabled or destroyed first, still sends
  the report, without a screenshot. Your options object is not changed. Call it from
  the main thread.
- `TracerUnityWebRequestTransport.Instance` works on every Unity platform, so pass it
  unconditionally. Native players refuse redirects; a failure's message names neither
  the URL nor a header.
- **A browser player cannot reach tracer today**, so leave the DSN empty in a WebGL
  build: `X-Sentry-Auth` (and gzip, and the envelope content type) make the browser
  send a CORS preflight, which tracer does not answer, and with a DSN set the client
  retries with back-off and the browser console shows a CORS error each time. The
  package still compiles for WebGL.

## Testing

`TracerOptions` takes the transport, both clocks (`NowMillis`, `UtcNow`) and the
`Random` source of ids and jitter, so a test pins wire bytes and back-off times without
a socket or a real timer. The golden envelope this package writes is
`Tests/Golden/report.envelope`; the service repository keeps the same bytes as
`tracer/testdata/envelopes/yyt-csharp/report.envelope`, where tracer's own tests ingest
it.

## Public API

- `TracerClient.Create(TracerOptions)` → `ITracerClient`: `State`, `Enabled`, `TraceId`,
  `Stats`, `SetTag`, `Breadcrumb`, `Log`, `Trace`, `Report`, `Feedback`, `FlushAsync`,
  `CloseAsync`, `Poll`, `Dispose`. `TracerClient.SdkVersion`.
- `TracerOptions`: `Dsn`, `Release`, `Dist`, `Environment`, `Tags`, `UserId`,
  `Contexts`, `Transport`, `Logger`, `NowMillis`, `UtcNow`, `Random`,
  `BreadcrumbCapacity`, `LogBatchSize`, `FlushInterval`, `MaxQueuedEnvelopes`,
  `MaxQueuedBytes`, `Timeout`, `Compress`, `AllowHttpLoopback`.
- `TracerReportOptions` (`Data`, `Screenshot`, `Attachments`, `Exception`, `StackTrace`,
  `Level`), `TracerAttachment` (`Screenshot`, `ViewHierarchy`), `TracerLevel`,
  `TracerState`, `TracerStats`, `TracerLimits`.
- `TracerDsn` (`TryParse`, `WriteKey`, `ResourceId`, `EnvelopeUri`).
- `TracerLogWriter.Create(client, breadcrumbs)`: a `Yingyeothon.Logger` writer that
  forwards each record as a log line (and a breadcrumb in category `log`).
- Transport seam: `ITracerTransport`, `TracerHttpRequest`, `TracerHttpResponse`,
  `TracerHttpClientTransport` (`Default`, `Create`), and in Unity builds
  `TracerUnityWebRequestTransport` and `TracerRunner` (`CreatePersistent`, `Client`,
  `ReportWithScreenshot`, `DeviceContexts`).

## Differences from flutterlib's `yingyeothon_tracer_client`

The model is flutterlib's, whose API names (`init` →
`Create`, `breadcrumb`, `log`, `trace`, `report`, `feedback`, `flush`, `close`) this
keeps.

- **Pumped.** Dart runs timers and futures on its event loop; here sends, answers,
  the flush interval and back-off advance only in `Poll()`, because a WebGL player has
  no timer thread. `Report` and `Feedback` therefore return the id at once (the envelope
  is queued, and sent at the next `Poll()`), not a future.
- **No process-wide `Tracer.current`.** The repository has no ambient state; hold the
  client you created (the runner exposes it as `Client`).
- **Data is a `JsonValue`**, not a map of objects: no reflection, so IL2CPP keeps it.
- **`TryParse` only** for the DSN: nothing on the path from a build define throws.
- **Frames** are read from .NET, Mono, IL2CPP and Unity stack-trace texts, `platform`
  `csharp`; Dart's VM frame format does not occur.
- **The Unity glue** (`TracerRunner`, the `UnityWebRequest` transport) stands where
  `yingyeothon_tracer_flutter` stands; there is no view hierarchy capture or report
  widget.

## Samples

- `Tracer Quickstart` — `TracerSession` (engine-free: a client from a build-time DSN and
  the calls around a rejected move) and `TracerQuickstart`, a persistent `MonoBehaviour`
  that reads the DSN from a git-ignored `Resources/tracer-dsn.txt`, starts a runner and
  sends a report with a screenshot and a linked feedback.
