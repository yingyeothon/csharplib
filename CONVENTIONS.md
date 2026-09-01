# API conventions

These rules apply to every package here. They are the C# translation of tslib's
`CONVENTIONS.md`; where the two differ, the reason is written down rather than left
to be rediscovered.

## Shape: interfaces and factories, not public classes

- A stateful resource is a public `interface I*`, an `internal sealed class`
  implementation, and a public `static class` of the same base name exposing
  `Create(...)`: `IGatewayLobbyClient`, `GatewayLobbyClientImpl`,
  `GatewayLobbyClient.Create(options)`.
- This is what tslib's "no exported classes, `create*` factories" rule buys in C#:
  callers bind to the contract, the implementation cannot be subclassed or
  constructed, and it can be replaced without a breaking change.
- Pure functions are static methods on a static class: `CloseCodes.Classify`,
  `GatewayUrl.Build`, `LobbyFrames.Read`.
- Stateless singletons are `public static readonly` fields or get-only properties:
  `JsonCodec.Instance`, `NullLogger.Instance`, `SystemClock.Instance`,
  `WebSocketTransport.Default`.

## Parameters

- More than two parameters, or any optional one, means a single options class named
  `<Product>Options` with mutable auto-properties and an object initializer.
- No `init` accessors and no `record`: both need `IsExternalInit`, which
  netstandard2.0 does not have and Unity's C# 9 profile does not reliably supply.
  Immutability is enforced by the factory copying options into readonly fields.
- Avoid optional parameters on public methods where a default could plausibly change:
  the value is baked into every already-compiled caller.
- `null` on an optional option means "use the default".

## Events

- Use C# `event Action<T>`, not a `Subscribe`/`IDisposable` pair. A multicast
  delegate already has the semantics tslib's emitter hand-rolls — the invocation list
  is snapshotted at raise time, so a handler that subscribes during a raise waits for
  the next one — and it costs no lookup per frame.
- C# forbids a method and an event sharing a name. When a sender and a receiver
  collide, rename the **receiver**: `Say` / `Said`, `Event` / `EventReceived`,
  `Party` / `PartyChanged`, `Refused` for an inbound `error`. Never a trailing
  underscore. Put the mapping in the package README.

## Async

- `Promise<T>` becomes `Task<T>` and the method takes an `Async` suffix.
- A task this library completes is completed **on the pump thread**, without
  `RunContinuationsAsynchronously`, so `await ConnectAsync()` resumes where `Send()`
  is legal. Settle it after the handlers for that pass have run, never in the middle
  of a state transition.
- Guard concurrency, not thread identity. A pumped object takes an interlocked claim
  for the duration of the pump and refuses any other entry point held by a different
  thread; it does not demand that the pump always be the *same* thread, because a
  host without a synchronization context resumes each `await` elsewhere while still
  being single-threaded in effect.

## Optional and absent

- An optional wire field is a nullable type, and serializing null **omits** the
  field. Go's `omitempty` means absent, and an absent field is not a JSON null.
- `JsonValue?` holding C# null is "not on the wire"; `JsonValue.Null` is "present and
  null". Do not fold them together.
- A capability that is null is *unrestricted*, not disabled. Only an explicit `false`
  disables.

## Logging

- The only contract is `ILogger` / `ILogWriter` from `Yingyeothon.Logger`. Every
  package that logs takes an optional `ILogger` in its options and defaults to
  `NullLogger.Instance`. No `Console.*` fallback, no package-local logger interface.
- Message first, structured context second.
- Log the routing facts, never the thing being routed: ids, codes, counts, lengths.
  A token, a frame body, a payload or a close reason in a log line is a leak, and
  `Debug` is not an exemption.

## Ambient state

- Runtime code never reads `Environment.GetEnvironmentVariable`, never calls
  `Console.*`, never reaches for `DateTime.Now`, and never references `UnityEngine`.
  Configuration and collaborators arrive through options.
- Time comes from an injected `IClock`; randomness from an injected `Func<double>`.
  Those seams are also the test seams.
- Every `double` conversion uses `CultureInfo.InvariantCulture`.

## No reflection

- No `Activator.CreateInstance`, no `GetType().GetProperty`, no `Reflection.Emit`, no
  attribute-driven serialization. IL2CPP's managed stripper removes what it cannot
  see being used, and it fails at runtime, not at build time.
- Wire types parse and build themselves by hand. That also makes them diffable
  against the gateway's `protocol.go` by eye.

## Layout

- Public API is whatever a package's Runtime assembly exposes. Test assemblies use
  that public surface; there is no `InternalsVisibleTo`.
- `packages/<upm-name>/Runtime/**` is the library, `Tests/**` the tests, and
  `Runtime/Unity/**` engine-facing glue behind `#if UNITY_5_3_OR_NEWER`, excluded
  from the dotnet build.
- Cross-package dependencies stay acyclic. Update the mermaid graph in the root
  README when an edge changes.
