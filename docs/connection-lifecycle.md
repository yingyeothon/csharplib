# Connection lifecycle

Both clients share one state machine, one pump, and one reconnect policy. This page is
all three.

## `Poll()`, or nothing happens

Received frames, the `hello` timeout, every reconnect delay and the settlement of
`ConnectAsync` are processed inside `Poll()`, **on the thread that calls it** — which is
what puts every handler on Unity's main thread without a synchronization context, and
what makes every timeout deterministic in a test. A client that is never polled never
connects, never fires a handler and never reconnects.

```csharp
void Update()
{
    lobby.Poll();
    game?.Poll();
}
```

Call it **unconditionally, before any pause or `timeScale` check**. A paused scene that
skips it stalls the connection.

In Unity — and only there, since it is a `MonoBehaviour` — `GamebaseRunner` does it for
a set of clients from its own `Update()`:

```csharp
var runner = GamebaseRunner.CreatePersistent();   // survives scene loads
runner.Add(lobby);
runner.Add(game);
runner.Remove(game);
```

It polls a copy of its list, so a handler may add or remove a client while it runs —
which is the normal case, since a dungeon client is usually created from a lobby event.

### Threading

Use a client from one thread at a time. `Poll()` takes a claim for its duration, and
while it is held every other entry point refuses — **including `Poll()` itself**, so it
is not re-entrant and must never be called from inside a handler. What is *not* enforced
is thread identity: any thread may pump, as long as only one does at a time.

- Sending from inside a handler is fine. That is the normal way to answer an event.
- `await ConnectAsync()` resumes on the pump thread by design, so `Send` is legal
  straight after it.
- `await MapAsync()` is an ordinary task continuation and **may resume anywhere**.
  Marshal back before touching the client or the engine.

Thread identity is deliberately not pinned: in Unity the synchronization context makes
these all the main thread anyway, and pinning it broke legitimate hosts twice.

## States

`client.State` moves `Idle` → `Connecting` → `Connected` → `Reconnecting` →
`Connected` … → `Closed`.

`Closed` is terminal. The factory copied the options into readonly fields at
construction, so a reissued client could not take a fresh token anyway — and an expired
token is the usual reason to reach `Closed`. Create a new client.

## Events, and the order you can rely on

| Event | Lobby | Dungeon | Carries |
| --- | --- | --- | --- |
| `Connected` | `Hello` | *(no argument)* | fires on the first connect **and after every reconnect** |
| `Disconnected` | ✓ | ✓ | `Code`, `Reason`, `WillReconnect` |
| `Reconnecting` | ✓ | ✓ | `Attempt`, `DelayMillis` |
| `Stopped` | ✓ | ✓ | `Kind`, `Reason`, `Code` — terminal |
| `Aborted` / `Finished` | — | ✓ | `Code`, `Reason` — terminal |
| `ProtocolError` | ✓ | ✓ | `Message` — a frame this SDK could not read |

`Disconnected` fires **before** every reconnect or stop, with `WillReconnect` telling
you which is coming. `Reconnecting` then names the attempt and the delay; `Stopped`
means no further attempt will be made.

On the lobby, a successful reconnect fires `Connected` again with a **new** `Hello` and
a new `ConnectionId`, and the peer map is reset. What refills it depends on whether the
gateway still holds the player's retained position —
[Lobby § The peer map](lobby.md#the-peer-map) has the two cases, and why a blind `Pos`
in the `Connected` handler can be refused as `move_too_far`.

## The reconnect policy

`CloseCodes.Classify(code, kind)` maps a close code to a `CloseDisposition` whose
`Kind` is `Reconnect`, `Stop`, `Aborted`, `Finished` or `ClientBug`. The full table,
with what each code means, is [Errors § Close codes](errors.md#close-codes).

The two rows that differ by channel kind are the ones this client exists to
distinguish: on a `q` channel `1000` is **`Finished`** and `4001` is **`Aborted`**; on
the lobby both simply stop.

### Backoff

Exponential with jitter: **500 ms, ×2, capped at 15 s, ±20 %**, unbounded attempts by
default. The jitter is why a gateway restart does not stampede. Set
`BackoffOptions.MaxAttempts` to stop after a fixed number; exhausting it ends in
`Stopped`.

The schedule resets on every successful connect, so a session that has been up for an
hour retries from 500 ms rather than from wherever it left off.

### Handshake failures

A refused handshake is an HTTP status **before** the WebSocket upgrade — `401` for a
bad token, `410` for an expired channel, `403` for a dungeon you are not in. A client
cannot see any of that; it sees a close before the socket ever opened.

So `MaxHandshakeFailures` (default **5**) counts consecutive closes-before-open and
ends the session instead of retrying a dead token forever. The counter resets on every
successful open. If your client stops after five quick attempts, the cause is almost
always the token or the channel, not the network — see
[Troubleshooting](troubleshooting.md).

## Shutting down

```csharp
client.Close();     // asks the gateway to close; no reconnect follows
client.Dispose();   // releases the socket and its receive loop
```

`Dispose` from `OnDestroy`. A replaced or closed socket is disposed exactly once
internally, but an undisposed *client* keeps a receive task and a cancellation source
alive for the rest of the session.

A handler that throws unwinds through the pump, skipping whatever came after it in your
own code. `ConnectAsync` is settled before events are raised so a throwing handler
cannot strand the await — but guard handlers that touch scene objects; a destroyed
`GameObject` is the common case.
