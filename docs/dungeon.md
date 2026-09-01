# Dungeon (`q`)

A `q` channel is a bridge to **your own game actor** — a `@yingyeothon/lambda-gamebase`
process running in your AWS account. The gateway carries frames between the player and
that actor and reads none of them.

That is the whole difference from the lobby: the lobby has a vocabulary, and `q` does
not. Everything you send and receive on a `q` socket is your game's schema.

## Where a `gameId` comes from

**Not from the platform.** A `q` channel is a pipe; a *run* through it is allocated by
your game, which also writes the start event listing who may join. The gateway refuses
a socket whose token's `sub` is not in that list, with the same status it uses for an
unknown run, so game ids cannot be probed.

Two shapes exist, both specified in the `service` repository:

- **Your own entry API.** The expected shape: the client posts with the same channel JWT
  and gets back a `gameId`. `examples/sample-morpg/src/entry.ts` is the reference
  implementation. The consequence for a client is the one rule that matters — **a retry
  after any failure allocates a new `gameId`, never the old one.**
- **The match service.** Connecting to `wss://match.yyt.life/?channel={matchChannelId}`
  with `["bearer", jwt]` *is* submitting the ticket; the answer carries your own
  callback's `{ wsUrl, gameId }`. `services/match/README.md` has the message vocabulary.
  The server never closes the socket after a terminal message — the client does.

Either way you end up with a `gameId` and the [token](authentication.md) you already
hold. If neither exists yet, you have no dungeon to join: a `q` channel is a pipe, and
something has to write the run's start event before anyone can connect to it.

## Connecting

```csharp
using Yingyeothon.Codec;            // JsonValue, Json
using Yingyeothon.Gamebase.Client;

var game = GatewayGameClient.Create(new GatewayGameClientOptions
{
    Url = "wss://gw.yyt.life",
    ChannelId = "q_0123456789abcdef",
    GameId = gameId,
    Token = channelJwt,
});

game.Frame    += ApplySnapshot;      // every game-defined frame, verbatim
game.Finished += _ => ShowResult();  // close 1000: the game dropped you, normally
game.Aborted  += _ => BackToLobby(); // close 4001: the actor died
game.Refused  += e => Log(e.Code);

await game.ConnectAsync();
game.Send(Json.Object().Set("type", "attack").Set("power", 3d).Build());
```

Note the difference from the lobby: `ConnectAsync` here returns a plain `Task` and
completes **when the socket opens**, because a `q` channel has no `hello` handshake.

The gateway pushes `enter` to your actor *after* the upgrade, not before — which is why
a failure there arrives as close `1011` rather than as a refused handshake. So a
connected socket is not yet a joined run: wait for the actor's first frame, usually a
snapshot, on `Frame`.

`Connected` fires again after a reconnect, and the game answers with a fresh snapshot.
Reconnecting to the same `gameId` is normal and expected; the actor rebinds the slot.

A lobby socket and a dungeon socket for the same player may be open at once. Two
sockets on the *same* channel for the same player may not — the second closes the first
with `4000`.

## Sending and receiving

`Frame` gives you a `JsonValue` exactly as the actor sent it — an array, a number or a
bare string is a legitimate game frame, so check before reading:

```csharp
game.Frame += frame =>
{
    if (frame.Kind != JsonKind.Object) return;

    switch (frame.GetString("type"))
    {
        case "snapshot": Apply(frame.GetArrayOrEmpty("entities")); break;
        case "damage":   Hit(frame.GetString("target"), frame.GetNumber("amount") ?? 0); break;
    }
};
```

`GetString` and friends answer null for a field that is absent or of another kind, so
they never throw on a frame you did not expect. The
[codec package](../packages/com.yingyeothon.codec/README.md) is the rest of that API.

Outbound, the gateway requires a JSON **object** with a string `type`, and refuses
`enter` and `leave` — those are its own bookkeeping, deciding which member a connection
speaks for. This SDK refuses them locally with `InvalidOperationException` before they
reach the wire; removing that check is a regression, not a simplification.

The gateway also overwrites `connectionId` with its own and strips any client-supplied
`memberId`. `connectionId` is the only field your actor may trust; a client must not
invent one.

A refusal arrives as `Refused` with an `ErrorFrame`. On a `q` channel it is recognised
by `type == "error"` plus a string `code` — the `message` is not required, because the
gateway marks it `omitempty`.

## Finished, aborted, and the difference

This is the distinction the whole client exists to make.

| Close | Event | What happened | What to do |
| --- | --- | --- | --- |
| `1000` | `Finished` | the game ended normally and dropped this connection | show the result |
| `4001` | `Aborted` | the actor stopped consuming its queue — it died or wedged | say so, return to the lobby, and **allocate a new `gameId`** |

Neither reconnects. A retry after an abort with the same `gameId` will be refused: the
gateway deleted the queue key.

Everything else follows the ordinary policy in
[Connection lifecycle](connection-lifecycle.md).

## Limits

- Text frames only; a binary one closes with `1003`. The bridge forwards the actor's
  message verbatim and never inspects it, so there is no framing a binary payload could
  survive.
- Three size caps produce `1009`; see [Errors § Close codes](errors.md#close-codes).
- 20 messages per second per connection, burst 2×; over that is `rate_limited`, and
  fifty refusals on one socket close it with `4003`.
- The actor's inbound queue is durable and bounded. Past a depth cap, or a depth that
  does not drop for five seconds, the gateway declares the actor dead and aborts every
  socket of that run with `4001`.
- A dungeon run is capped by your Lambda's own ceiling — 900 seconds is the hard limit,
  and the reference game sets its own budget well under it.

## Shutting down

Closing mid-run is normal — the gateway pushes `leave` to your actor. See
[Connection lifecycle](connection-lifecycle.md#shutting-down).
