# Yingyeothon.Logger

<!-- Generated from the assembly by tests/Yingyeothon.PublicApi.Tests.
     Do not edit by hand: the test rewrites it and CI compares it. -->

Every public type and member, with its documentation comment — the same text
your IDE shows. For what the package is *for*, read
[the guide](../README.md) and
[`packages/com.yingyeothon.logger/README.md`](../../packages/com.yingyeothon.logger/README.md).

## Contents

- [`ConsoleLogger`](#static-class-consolelogger)
- [`FilteredLogger`](#static-class-filteredlogger)
- [`FilteredLoggerOptions`](#class-filteredloggeroptions)
- [`ILogWriter`](#interface-ilogwriter)
- [`ILogger`](#interface-ilogger)
- [`LogSeverity`](#enum-logseverity)
- [`LogWriters`](#static-class-logwriters)
- [`NullLogger`](#static-class-nulllogger)

## static class ConsoleLogger

A filtered logger over `Console` .

| Member | Summary |
| --- | --- |
| `Create() : ILogger` | A console logger filtered at `Info` . |
| `Create(LogSeverity) : ILogger` | A console logger filtered at `severity` . |

## static class FilteredLogger

A logger that forwards to a writer only at or above its severity.

| Member | Summary |
| --- | --- |
| `Create(FilteredLoggerOptions) : ILogger` | Creates a logger that forwards to the option's writer above its threshold. |

## class FilteredLoggerOptions

Options for `Create` .

| Member | Summary |
| --- | --- |
| `Severity : LogSeverity get set` | The initial threshold. It stays mutable on the created logger. |
| `Writer : ILogWriter get set` | Where records that pass the threshold go. |
| `ctor()` |  |

## interface ILogWriter

Where log records go. The call style is message first, structured context after: `writer.Info("lobby connected", Json.Object().Set("userId", id).Build())` .

| Member | Summary |
| --- | --- |
| `Debug(String, JsonValue?) : Void` | Writes at `Debug` . Log routing facts — ids, codes, counts — never the thing being routed. |
| `Error(String, JsonValue?) : Void` | Writes at `Error` . |
| `Info(String, JsonValue?) : Void` | Writes at `Info` . |
| `Warn(String, JsonValue?) : Void` | Writes at `Warn` . |

## interface ILogger

A `ILogWriter` with a severity threshold of its own.

| Member | Summary |
| --- | --- |
| `IsEnabled(LogSeverity) : Boolean` | Whether a record at this severity would be written. Guard an expensive context with this rather than building it and having it dropped. |
| `Severity : LogSeverity get set` | The threshold. It is read on every call, so raising or lowering it at runtime takes effect immediately. |

## enum LogSeverity

A log level, and — as a logger's own severity — the threshold below which nothing is written. `None` ranks above every level, so a logger set to it writes nothing at all.

- `Debug` — Detail for diagnosis. Never a payload, a token or a frame body, even here.
- `Error` — Something failed.
- `Info` — Normal operation worth recording.
- `None` — Above every level, so a logger set to it writes nothing.
- `Warn` — Something recoverable went wrong.

## static class LogWriters

Ready-made `ILogWriter` implementations and combinators.

| Member | Summary |
| --- | --- |
| `Combine(ILogWriter[]) : ILogWriter` | Fans a record out to every writer, in order. `Instance` is skipped by reference, matching tslib's `combine` ; a different writer that happens to discard everything is still called. |
| `Console : ILogWriter` | Writes to `Console` . Unity callers want `FromAction` instead. |
| `Format(LogSeverity, String, JsonValue) : String` | Renders a record the way the console writer does. |
| `FromAction(Action<LogSeverity, String, JsonValue>) : ILogWriter` | Adapts a callback into a writer. This is the seam a Unity game uses to reach `UnityEngine.Debug` without the package referencing the engine. |
| `Null : ILogWriter` | A writer that discards everything. |

## static class NullLogger

The logger every package defaults to: it writes nothing.

| Member | Summary |
| --- | --- |
| `Instance : ILogger` | The shared instance. `Combine` recognises it by reference. |
