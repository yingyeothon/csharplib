# Architecture & API Design

`CONVENTIONS.md` at the repo root is canonical. This file records the operational
consequences and the decisions that are easy to undo by accident.

## Porting from tslib

- A faithful transliteration of the TypeScript is usually right and occasionally very
  wrong. The places it is wrong are written down in each package README under
  "Differences from ..." — read that section before changing behaviour to "match
  tslib", because the difference may be the fix.
- The known ones: reflection-free codecs, the type-keyed event broker, C# `event`
  instead of an emitter, the `Poll()` pump, deferred settlement of `ConnectAsync`,
  and a refused handshake surfacing as a close rather than an exception.

## Mirroring a Go wire protocol

- `gamebase-client`'s types mirror the gateway's Go structs, **including the JSON
  tags**. Two things a README-only reading gets wrong:
  - a Go `string` field refuses a JSON number for the whole frame (`dir` was typed as
    a number in tslib 2.0.0 and every `pos` carrying it was dropped as
    `bad_message`), so `Dir` is `string?` and there is no numeric overload anywhere;
  - `omitempty` means the field is simply absent, so a required field in the SDK type
    is a lie unless the client fills it in — that is what the roster normalisation in
    `LobbyFrames` is for.
- A field **without** `omitempty` has the opposite trap: a nil Go slice marshals as
  JSON `null`, which is why `Capabilities.Say` treats null and absent alike as
  "unrestricted". Folding null into an empty list refuses every scope the channel
  actually allows.
- When checking a wire type, open `gateway/internal/lobby/protocol.go` in the service
  repo, not only its README.

## Absent, null, and default

- `JsonValue?` holding C# null is "not on the wire"; `JsonValue.Null` is "present and
  null". `JsonObjectBuilder.Set(key, null)` omits the key; `SetNull` writes a null.
  Collapsing the two is how a client sends `"dir": null` for a position with no
  facing.
- A nullable capability that is null is *unrestricted*. Only an explicit `false`
  disables. Test both.

## The pump is the concurrency model

- Every field of `GatewaySocket` is written only from `Poll()`, `ConnectAsync()`,
  `Close()` or `Send()` — all called by the owner thread. The receive side does
  exactly one thing: enqueue a `SocketEvent`. That is why the transport takes an
  `IWebSocketEventSink` instead of raising events: there is no callback an adapter
  could invoke on the wrong thread.
- `Poll()` drains the queue **before** checking deadlines, so a `hello` that arrives
  in the same tick as its timeout wins. It loops until quiescent because a local
  close enqueues its own close event, and it is bounded so a cascade cannot hang a
  frame. It refuses re-entry.
- A pending `ConnectAsync` is settled at the end of the pass, never inside a
  transition. TypeScript gets this free from the microtask queue: `markReady`
  resolves before `emit("hello")` runs, but the continuation still sees the
  filled-in client. Settling inline in C# would hand an awaiter a null `Hello`.

## Transport seams

- Anything platform-specific lives behind an interface with the vendor call in a
  factory next to it: `IWebSocketFactory` / `WebSocketTransport.Default`,
  `IHttpFetcher` / `HttpFetcher.Default`. That is what makes Unity WebGL a
  configuration choice rather than a fork.
- A factory may throw for input it can reject up front — a malformed URL, a
  subprotocol with non-token characters — and the SDK reports that as a stop.
  Everything after construction, **including a refused handshake**, must arrive as a
  close event, or the handshake-failure policy never sees it.
- Report a close exactly once per socket, and report the locally requested code when
  the close was local. The state machine keyed its decision on that code; a peer's
  echo would erase it.
- Answer a close frame the peer sent. Not doing so leaves the peer's own close
  waiting until its idle timer — found by an integration test that hung, not by a
  unit test.

## Lifetime

- A replaced or closed `IWebSocket` is disposed exactly once. TypeScript leaves this
  to the collector; here an undisposed socket keeps a receive task and a
  `CancellationTokenSource` alive for the rest of the session, and a reconnect storm
  leaks one per attempt.

## A hot-path parser is a non-throwing core with a throwing wrapper

- The socket parses every inbound frame, so the failing path is reached by whatever a
  peer chooses to send. A core that returns a `JsonParseFailure` — a code and an
  offset, in a struct — lets a hostile peer's garbage cost nothing but a return, and
  `Json.Parse` stays available as a thin wrapper that throws for callers who want it.
- Report the reason as a **code**, never as text quoted from the input; see
  [security.md](security.md). The code names reach logs, so they are public API.
- When a recursive descent stops throwing, every loop needs its own way out. The
  throw used to be it; now `continue` may only run after a separator was consumed,
  and every helper that fails returns the sentinel its caller checks. First failure
  wins, or an outer, vaguer reason overwrites the real one.
- Bound the input as well as the depth, and check the length **before** scanning —
  a cap that costs a walk is not a cap. Give a genuinely larger document its own
  entry point (`Json.ParseBig`) rather than raising the default that every frame pays.
- The writer needs the same depth bound as the parser. A value tree a caller built by
  hand has no bound, and recursing over it overflows the stack — which no catch block
  can save — while emitting it would produce a frame this SDK cannot read back.

## Ordering inside a cache

- Publish a cache entry **before** starting the work it stands for. `MapFetcher`
  originally assigned the task after calling the loader, and a fetcher that answered
  synchronously evicted the failure before the assignment put it back — so the retry
  got the failure it had just seen.
