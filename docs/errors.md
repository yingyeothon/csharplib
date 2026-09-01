# Errors and close codes

Three things can go wrong, and they arrive by three different routes: the gateway
**refuses** a frame you sent (`Refused`), the gateway **closes** the socket (a close
code), or this SDK **throws** before anything reaches the wire.

## Refusals — the `Refused` event

```csharp
lobby.Refused += error => Log(error.Code);   // never log error.Message
```

`ErrorFrame.Code` is a string, and the set is open — `GatewayErrorCode` names the
documented ones but does not close them into an enum, so handle an unknown code
gracefully.

| Code | What it means | Usually because |
| --- | --- | --- |
| `bad_message` | the frame did not parse, a field had the wrong type, or a length the gateway checks separately | a numeric `dir`, a comma-decimal locale writing `1,5`, or an event `name` outside 1–64 bytes |
| `capability_off` | the channel disables that command | `pos`, `party`, `event` or a chat scope turned off in the console |
| `rate_limited` | over the channel's rate limit | the lobby default is 30/s; `q` is 20/s, burst 2× |
| `bad_scope` | the scope is not one this channel allows | `say` with a scope missing from `capabilities.say` |
| `bad_zone` | the zone name is malformed or over 64 bytes | a zone name built from player input |
| `move_too_far` | the position jumped further than `maxMoveDelta` | teleporting without a zone change, or a dropped frame |
| `unknown_user` | no such `userId` on this channel | a whisper or invite to someone who left |
| `no_party` | the command needs a party and there is none | `party.leave` without one |
| `already_in_party` | `party.create` while already in one | a create button that does not check `PartyId` |
| `party_full` | the party is at `partySizeMax` | an invite race: two accepts for the last slot |
| `not_invited` | `party.accept` without an invite | an invitation that was withdrawn |
| `unknown_party` | no such `partyId` | an invite that expired |
| `not_leader` | the command is the leader's to make | inviting when leadership already passed on |
| `too_long` | a field is over its byte cap | `text` > 1024 B, or `payload` > 8 KB |
| `reserved_type` | a `q` frame used `enter` or `leave` | those are the gateway's own |
| `unavailable` | the gateway could not serve it right now | transient; retry once. Not a client bug |

**Fifty refusals on one socket close it with `4003`.** One refusal is cheap — a single
oversized message costs exactly one, and nothing else. A stream of them is a client bug
the gateway stops paying for.

**Never log `error.Message`.** The gateway may quote what your client sent back into
it, which puts a payload — or a credential echo — into whatever writer a consumer
installed. Log the code.

## What the SDK does *not* check for you

Only `dir` is validated locally. The gateway also refuses:

| Field | Cap | Refusal |
| --- | --- | --- |
| `text` (`Say`) | non-empty, ≤ 1024 bytes | `too_long` |
| `payload` (`Event`) | ≤ 8 KB | `too_long` |
| `name` (`Event`) | 1–64 bytes | `bad_message` |
| `zone` (`Pos`) | ≤ 64 bytes | `bad_zone` |
| any move | ≤ `maxMoveDelta` tiles | `move_too_far` |

This is deliberate: a local guard that is *stricter* than the server throws inside your
`Update()` for a frame the gateway would have accepted, and that is the worse failure.
A guard that is looser costs one refusal you already handle.

## Exceptions this SDK throws

All of them are thrown **locally**, before anything reaches the wire.

| Exception | Thrown by | When |
| --- | --- | --- |
| `InvalidOperationException` | every sender — `Pos`, `Say`, `Event`, `Ping`, `Party.*`, `Send` | the client is not ready: before `ConnectAsync`, during a reconnect, or after it stopped. The message names the state. **This is the one a game hits most**, because a reconnect is invisible unless you watch `State` |
| `InvalidOperationException` | `ConnectAsync` | called twice, or after the client closed |
| `InvalidOperationException` | `Poll` | called re-entrantly, or while another thread is inside it |
| `ArgumentNullException` | `Create`, `IGatewayGameClient.Send` | a null options object or frame |
| `ArgumentException` | `Pos` | `dir` is over 16 bytes |
| `InvalidOperationException` | `Pos`, `Say`, `Event`, `Party.*` | the channel disables that capability, or that chat scope — message begins `capability_off:` |
| `InvalidOperationException` | `IGatewayGameClient.Send` | the frame's `type` is `enter` or `leave` — message begins `reserved_type:` |
| `InvalidOperationException` | `MapAsync` | called before `hello` arrived |
| `GatewayStoppedException` | `await ConnectAsync()` | the connection ended before it became usable |
| `MapFetchException` | `await MapAsync()` | the map URL answered a non-2xx status, which it carries |
| `JsonKindException` | `JsonValue.AsString`, `AsNumber`, `AsArray`, … | the value is a different kind — a map body that was not JSON reaches you this way |
| `JsonNumberException` | `JsonValue.AsInt32` | the number does not fit an `int` |
| `JsonParseException` | `Json.Parse` | see [`Yingyeothon.Codec`](../packages/com.yingyeothon.codec/README.md) |

A capability check throws only on an explicit `false`. A `null` capability is
unrestricted, so the send goes out and the gateway decides.

## Close codes

```csharp
client.Stopped += e => Log($"{e.Kind}: {e.Reason} ({e.Code})");
CloseDisposition d = CloseCodes.Classify(code, GatewayChannelKind.Lobby);
```

| Code | Constant | Meaning | Lobby | Dungeon |
| --- | --- | --- | --- | --- |
| `4000` | `GatewayCloseCode.Replaced` | a newer socket of the same user on this channel replaced this one | `Stop` | `Stop` |
| `4001` | `GatewayCloseCode.Aborted` | the game actor stopped consuming its queue | `Stop` | **`Aborted`** |
| `4002` | `GatewayCloseCode.Idle` | no pong within 75 seconds | `Reconnect` | `Reconnect` |
| `4003` | `GatewayCloseCode.Policy` | fifty refused messages on one socket | `ClientBug` | `ClientBug` |
| `4004` | `GatewayCloseCode.ChannelGone` | the channel expired or was disabled | `Stop` | `Stop` |
| `4900` | `GatewayCloseCode.Local` | this SDK closed the socket itself | — | — |
| `1000` | — | closed normally | `Stop` | **`Finished`** |
| `1001` | — | the gateway is restarting | `Reconnect` | `Reconnect` |
| `1003` | — | a binary frame was sent; the gateway is text-only | `ClientBug` | `ClientBug` |
| `1009` | — | a frame was over a size cap — see below | `ClientBug` | `ClientBug` |
| `1011` | — | the gateway failed to push `enter` to the actor | `Reconnect` | `Reconnect` |
| anything else | — | treated as a transient network failure | `Reconnect` | `Reconnect` |

`4900` is what this SDK closes with when it ends a socket itself; a client may only send
`1000` or `3000`–`4999`, and the gateway does not use that one. Seeing it in a `Stopped`
event means the decision was local — a `hello` timeout, an exhausted backoff, or your
own `Close()` — not something the gateway sent.

**Three different size caps produce `1009`, and they are not the same number.** The
gateway refuses anything the client **sends** over 16 KB. What it **sends back** it caps
at 32 KB. This SDK reassembles at most 64 KB — double the gateway's outbound cap, so a
legitimate frame never reaches it — and an over-size message stops rather than
reconnects, because a reconnect would meet the same flood.

`4004` follows a channel expiring — channels live 7 days and `yyt channels extend` adds
seven more, capped at 28 days. Live sockets close within about a minute of expiry.

## Handshake refusals, which look like nothing

The gateway refuses a bad handshake with an HTTP status **before** the WebSocket
upgrade. A WebSocket client cannot observe a status at that point, so all of these
present identically: a close before the socket ever opened.

A bad token, an expired channel, a `q` run you are not in, a channel of the wrong kind —
all of them are an HTTP status the client never receives. `MaxHandshakeFailures`
(default 5) exists for exactly this.

Since the status is invisible, diagnose by elimination:
[Troubleshooting](troubleshooting.md#it-connects-then-immediately-stops) ranks the
causes and gives the one check for each. `gateway/README.md` in the `service` repository
lists the statuses themselves.

## Frames this SDK could not read

```csharp
client.ProtocolError += e => Log(e.Message);
```

A malformed frame is reported rather than thrown, because the failing path is reached
by whatever a peer chooses to send and an exception per frame is a cost a hostile peer
should not be able to impose.

The message never quotes the input — it is a `JsonParseError` code and a character
offset, or a capped and control-character-stripped frame type. That is deliberate: a
`ProtocolError` reaches whatever log writer you installed, and the input is a frame
body.
