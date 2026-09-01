# Yingyeothon.EventBroker

A type-safe event broker with asynchronous handlers, dispatched sequentially in
registration order.

Independent of the other three packages and of the gateway; see [the guide](../../docs/README.md) for what the rest of this repository is for.

## Install

```
https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.event-broker
```

No dependencies.

## Usage

```csharp
using Yingyeothon.EventBroker;

public sealed class PlayerDied { public string UserId; }

IEventBroker broker = EventBroker.Create();

broker.On<PlayerDied>(e => ShowTombstone(e.UserId));
broker.Once<PlayerDied>(async e => await SaveRunAsync(e));

bool anyHandler = await broker.FireAsync(new PlayerDied { UserId = "alice" });
```

## Public API

- `IEventListenable` — `On<T>`, `Once<T>`, `Off<T>`, each with an `Action<T>` and a
  `Func<T, Task>` overload. All return the broker, so they chain.
- `IEventBroker : IEventListenable` — `Task<bool> FireAsync<T>(T value)`.
- `EventBroker.Create()`.

## Semantics

- Handlers run in registration order, each awaited before the next.
- `FireAsync` iterates a **snapshot**, so a handler registered during dispatch runs
  from the next fire, not this one.
- A `Once` registration is removed **before** its handler runs, so it is gone even if
  the handler throws.
- A handler that throws or faults faults the returned task and the remaining handlers
  do not run.
- `FireAsync` returns `false` when nothing was registered for that type.
- `Off` removes only the first registration matching that delegate.
- Not thread-safe. Drive it from one thread, as tslib does.

## Differences from `@yingyeothon/event-broker`

tslib keys handlers by a name in a TypeScript event map. C# has no mapped types, and
a string-keyed broker would force `object` payloads and a cast per dispatch, so the
**payload type is the key**: `On<PlayerDied>` registers for the event that carries a
`PlayerDied`. Typos become compile errors instead of silently dead handlers.

## Samples

One importable sample ships with this package: _Package Manager → the package →
Samples → Import_.
