# Yingyeothon.Logger

A minimal structured logger: a severity threshold you can change at runtime, writers
you can compose, and a context rendered without reflection.

Pass a logger to a gateway client through `GatewayClientOptions.Logger`; [docs/unity.md](../../docs/unity.md#logging-to-the-editor-console) has the Unity wiring.

## Install

```
https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.logger
```

**No release has been tagged yet**, so this URL tracks `main`; append `#<tag>` to
pin one as soon as there is one.

Depends on `com.yingyeothon.codec`.

## Usage

```csharp
using Yingyeothon.Codec;
using Yingyeothon.Logger;

ILogger logger = ConsoleLogger.Create(LogSeverity.Info);

logger.Info("lobby connected", Json.Object()
    .Set("channelId", channelId)
    .Set("userId", userId)
    .Build());

logger.Severity = LogSeverity.Debug;   // takes effect on the next call
```

In Unity, route it to the editor console without the package referencing the engine:

```csharp
ILogger logger = FilteredLogger.Create(new FilteredLoggerOptions
{
    Severity = LogSeverity.Info,
    Writer = LogWriters.FromAction((severity, message, context) =>
        UnityEngine.Debug.Log(LogWriters.Format(severity, message, context))),
});
```

## Public API

- `LogSeverity` — `Debug`, `Info`, `Warn`, `Error`, `None`.
- `ILogWriter` — `Debug` / `Info` / `Warn` / `Error`, each `(string message, JsonValue? context = null)`.
- `ILogger : ILogWriter` — a mutable `Severity` and `IsEnabled`.
- `FilteredLogger.Create(FilteredLoggerOptions)`.
- `LogWriters` — `Console`, `Null`, `Combine`, `FromAction`, `Format`.
- `NullLogger.Instance` — what every package defaults to.
- `ConsoleLogger.Create`.

## Notes

- The threshold is read on **every** call, so a game can raise it from an inspector
  mid-session. `None` ranks above every level and suppresses everything.
- `Combine` skips exactly the `NullLogger.Instance` singleton, by reference. A
  different writer that happens to discard everything is still called — the same rule
  tslib's `combine` follows.
- The context is a `JsonValue` rather than an arbitrary object so a writer can render
  it deterministically without reflection. Guard an expensive context with
  `IsEnabled`.
- Message first, context second: `logger.Info("actor started", context)`. Log the
  routing facts — ids, codes, counts — never the thing being routed. A payload, a
  frame body or a token in a log line is a leak, and `Debug` is not an exemption.

## Differences from `@yingyeothon/logger`

- The variadic `...args: unknown[]` context becomes one optional `JsonValue?`.
- `consoleWriter` writes to `System.Console`; Unity wiring goes through
  `LogWriters.FromAction`, because a Runtime assembly here declares
  `noEngineReferences`.

## Samples

One importable sample ships with this package: _Package Manager → the package →
Samples → Import_.
