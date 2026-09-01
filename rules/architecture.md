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
  JSON `null`. `Capabilities.Say` treats null and absent alike as "unrestricted", but
  **not for the reason this file used to give**. The gateway's `capabilities()` always
  builds a non-nil slice, so `say` is always a JSON array and `null` never reaches the
  wire; and Go's `AllowsSay` returns false for a nil slice, so if it ever did, null
  would mean "nothing allowed". The SDK stays permissive for that unreachable shape
  deliberately, because of the asymmetry below. `"say": []` is the shape that is
  really sent for a chat-disabled channel, and it refuses every scope.
- **When in doubt, be no stricter than the server.** A local guard that is stricter
  throws inside the game's `Update()` for a frame the gateway would have delivered; a
  guard that is looser costs one refusal the game already handles. That asymmetry is
  what made the say list gating `Event()` a real bug: the gateway's
  `handleEventLocked` checks only `Capabilities.Event` and never calls `AllowsSay`, so
  a party event on a zone-only-chat channel was refused by the SDK alone.
- **A merge is not an update.** The gateway rebuilds a whole `Peer` from each inbound
  `pos` and marshals it with `dir,omitempty`, so an omitted `dir` states the peer has
  no facing. Carrying the previous value forward left a peer facing a direction it had
  cleared, permanently, and made its facing depend on which frame it arrived in.
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
- **A socket this machine has decided against must be retired, not merely asked to
  close.** `LocalClose` used to leave `_socket` assigned, so everything already queued
  behind the close was still delivered — and a `hello` in flight when the deadline
  fired, or one from a gateway that never echoed `bearer`, then ran `MarkReady` and
  handed `await ConnectAsync()` a connection the machine had already rejected. The
  guard now drops everything but the retired socket's own close, which is what still
  drives the reconnect.
- **Schedule the settlement before raising the events, not after.** The flush still
  happens at the end of the pass, but a handler that throws unwinds past whatever
  comes after it: `Stop()` raising `Disconnected` before `ScheduleFailure` left
  `await ConnectAsync()` pending forever, with `State` already `Closed` so it could
  never be reissued. A null reference on a destroyed `GameObject` is the ordinary way
  a game gets there.
- **Bound every loop the peer can feed, not just the outer one.** `MaxPollPasses`
  bounded the pass count while the inner drain over an unbounded queue had no bound at
  all, so a peer producing frames faster than the pump parses them could hold the
  caller's frame indefinitely. Both are bounded now, and exhausting the budget says so
  at `Debug` instead of deferring in silence.
- A pending `ConnectAsync` is settled at the end of the pass, never inside a
  transition. TypeScript gets this free from the microtask queue: `markReady`
  resolves before `emit("hello")` runs, but the continuation still sees the
  filled-in client. Settling inline in C# would hand an awaiter a null `Hello`.

## Bound what a peer can spend

- A transport that reassembles a message must cap it. The codec's own length limit
  cannot help: it is checked against a string the transport has already built, so an
  unbounded `MemoryStream` in the receive loop defeats it entirely. The cap is derived
  from the gateway's documented outbound frame size, and an over-size message arrives
  as a close (1009, which stops rather than reconnects — a reconnect would meet the
  same flood).
- The same applies to anything that fetches a URL the server named. `HttpFetcher`'s
  defaults were a 100-second timeout, a two-gigabyte buffer and fifty redirects on
  `hello.mapUrl`. Give it a timeout, a size cap that matches the parser's, and a small
  redirect budget.
- A body too large to parse must **fail**, not degrade. Falling through to
  "hand it back as one enormous string" is the silent breakage the limit exists to
  prevent; no caller notices until it reads a field.
- A helper that scans a caller-supplied string needs a bound derived from the limit,
  not from the input: the close-reason truncation started at `reason.Length` and was
  O(n^2) in time and allocation, on the game's main thread.

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

## Shared work, per-caller cancellation

- A cache that hands several callers one `Task` must not wire one caller's
  `CancellationToken` into the shared work. `MapFetcher` did, so a scene loader
  cancelling its own `MapAsync` threw at a HUD that had passed no token at all — and
  the entry was already evicted, so the failure was not even reproducible. Drive the
  shared fetch with no caller's token and give each caller its own observing task.
- Settle a `TaskCompletionSource` **outside** the `try` that catches the work, and use
  `TrySet*`. Settlement runs continuations inline, so a handler that threw back into
  the frame evicted a successful fetch and then completed the source twice.

## Ordering inside a cache

- Publish a cache entry **before** starting the work it stands for. `MapFetcher`
  originally assigned the task after calling the loader, and a fetcher that answered
  synchronously evicted the failure before the assignment put it back — so the retry
  got the failure it had just seen.
