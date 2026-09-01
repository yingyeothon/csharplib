# Lobby

The lobby channel is a **scope relay**: it routes to a zone, a party or a user, and it
never interprets a payload. Positions, chat, parties and your own game events all ride
on it, and the gateway synthesises the `enter` / `leave` / `snapshot` frames that stop
a player who walked away from freezing on everyone else's screen.

The wire spec is `gateway/README.md` in the
[`service`](https://github.com/yingyeothon/service) repository. This page is what the
C# surface does with it.

Every snippet here assumes:

```csharp
using Yingyeothon.Codec;            // JsonValue, Json
using Yingyeothon.Gamebase.Client;
```

## Connecting

```csharp
var lobby = GatewayLobbyClient.Create(new GatewayLobbyClientOptions
{
    Url = "wss://gw.yyt.life",
    ChannelId = "lobby_0123456789abcdef",
    Token = channelJwt,
});

Hello hello = await lobby.ConnectAsync();
```

`ConnectAsync` completes on the **`hello` frame**, not on the socket opening — nothing
is "connected" before it, because `hello` is the only delivery path for the channel's
capabilities and its map pointer.

`HelloTimeoutMillis` (10 s) bounds the wait for one socket, not the call: a socket that
does not say `hello` in time is closed and **retried with backoff**, and the task you
are awaiting stays pending until a later `hello` settles it or the session stops for
good, at which point it fails with `GatewayStoppedException`.

`await ConnectAsync()` resumes on the pump thread, so calling `Pos` or `Say` straight
after the `await` is legal. See [Connection lifecycle](connection-lifecycle.md).

The `Connected` event carries the same `Hello` and fires **again after every successful
reconnect** — with a new `ConnectionId`, and a peer map that has been reset. What
refills it is [the gateway's decision, not yours](#the-peer-map).

## `hello` and capabilities

```csharp
hello.UserId;        // this player's identity, the same value as the JWT's `sub`
hello.ConnectionId;  // this socket; a reconnect gets a new one
hello.Zone;          // the zone to start in — not a placement, see below
hello.Tick;          // the channel's flushIntervalMs: how often positions broadcast
hello.MapUrl;        // an immutable public asset, or empty
hello.PartyId;       // set when the gateway already knows this player's party,
                     // which is how a reconnect finds it again
hello.Capabilities;  // what this channel enables
hello.Raw;           // the frame as received, for a field this SDK does not model
```

Capabilities gate the senders, and **null means unrestricted, not disabled**:

```csharp
if (lobby.Capabilities?.Party == false) HidePartyUi();
if (lobby.Capabilities?.AllowsScope(SayScope.User) == false) HideWhisperUi();
```

The SDK's checks throw locally, before anything reaches the wire, only on an explicit
`false`. They are a courtesy that gives you a fast error; the gateway is the
enforcement, and this SDK deliberately never becomes stricter than it — a local guard
that is stricter throws inside your `Update()` for a frame the gateway would have
accepted, which is the worse failure.

A yyt channel fills every capability field in, so in practice you are reading `true` or
`false`. The permissive reading of null is for a shape the console cannot currently
produce, and `lobby.Capabilities` being null at all just means `hello` has not arrived.

## Positions and zones

```csharp
lobby.Pos(zone, x, y, dir);   // dir is optional
```

**A player has no zone until their first `Pos`.** `hello.Zone` is the channel's default,
so a client that never announces is invisible to everyone and sees nothing.

A zone change is your game's decision, not the gateway's: announce `Pos` with the new
zone name and the gateway emits `leave` to the old zone and a `snapshot` for the new
one. Zones are **not** private — any client may announce into any zone name — so
whatever gates a zone is a rule in your game, not in the platform.

Rate: the gateway coalesces positions and broadcasts one batch per `hello.Tick`
milliseconds, so sending faster than that buys nothing and spends your channel's rate
limit. Send when the player actually moves.

`dir` is **your game's own facing token, an opaque string of at most 16 bytes**. It is a
string on the wire, and a numeric `dir` makes the gateway refuse the whole frame as
`bad_message` — the position never lands, and the only sign of it is a `Refused` event
you have to be listening for. This SDK offers no numeric overload anywhere and throws
`ArgumentException` on a longer one. A Hangul syllable spends three of the sixteen bytes.

Omitting `dir` **clears** the peer's facing rather than leaving it unchanged: the
gateway rebuilds the whole peer from each inbound frame and marshals it with
`omitempty`, so an absent `dir` is a statement.

The gateway also refuses a `zone` over 64 bytes and a move larger than the channel's
`maxMoveDelta`. Neither is checked locally — see [Errors](errors.md).

## The peer map

`lobby.Peers` is a reduction of `snapshot` / `enter` / `leave` / `pos` into the players
visible in the current zone. The client owns it, rebuilds it on each `hello` with your
own `userId`, and resets it on every disconnect.

```csharp
lobby.PeerEnter += peer => Spawn(peer.UserId, peer.X, peer.Y, peer.Dir);
lobby.PeerLeave += userId => Despawn(userId);
lobby.PeerMove  += peers => { foreach (var p in peers) Move(p); };
lobby.Snapshot  += frame => RebuildZone(frame.Zone, frame.Peers);

var someone = lobby.Peers.Get(userId);      // null when the map does not know them
var everyone = lobby.Peers.All();
var zone = lobby.Peers.Zone;                // null before the first snapshot
```

Four behaviours worth knowing, all of them deliberate:

- **Your own entry is filtered out** of every frame. The wire `pos` batch includes you;
  the map does not.
- **A snapshot replaces everything, whatever zone it names.** That is how a zone change
  starts, and it is checked before the zone filter below.
- **`enter`, `leave` and `pos` for another zone are ignored**, so a late `pos` from the
  zone you left cannot resurrect a peer that already left.
- **A `pos` for a peer the map does not know is dropped**, not treated as an arrival —
  a coalesced batch from before a `leave` must not bring the ghost back.

A reconnect resets the map, and what refills it is the gateway's decision, not yours.
Retained positions and party membership survive a disconnect for 30 minutes, and if the
player's is still there the gateway re-enters the zone itself and sends a `snapshot`
right after `hello` — no `Pos` from you required. Only when the retention has expired,
or the player never announced, is a fresh `Pos` what starts the zone.

`PeerMap.Create(new PeerMapOptions { SelfUserId = … })` builds one directly, and
`Apply` returns a `PeerChange` (`Kind`, `Zone`, `Peers`, `UserId`) for each frame it
accepted or `null` for one it ignored. That is for a *second* view of a zone — a minimap
driven off `Frame`, or a replay fed recorded frames — since the client's own map is
already committed to the player's current zone. `Reset()` empties it.

## Chat

```csharp
lobby.Say(SayScope.Zone, "hello");                 // everyone in this zone
lobby.Say(SayScope.Party, "on my way");            // the player's party
lobby.Say(SayScope.User, "hi", to: otherUserId);   // a whisper, across zones

lobby.Said += frame => Show(frame.From, frame.Scope, frame.To, frame.Text);
```

The receiving event is `Said` rather than `Say`, because C# forbids a method and an
event sharing a name — the same collision renames `EventReceived` and `PartyChanged`.
`Refused` collides with nothing; it is named for what it is.

`SayBroadcastFrame.Scope` is the **wire string** (`"zone"`, `"party"`, `"user"`), not a
`SayScope`. `SayScopes.TryParse(frame.Scope, out var scope)` converts it, answering
false for a scope this SDK does not know; `SayScopes.ToWire` goes the other way. The
same is true of `EventBroadcastFrame.Scope`.

`Say` throws locally when the channel disables that scope. It does **not** check the
text — [Errors](errors.md#what-the-sdk-does-not-check-for-you) says why, and why you
must.

## Game events

```csharp
lobby.Event(SayScope.Party, "invite-to-dungeon", Json.Object().Set("gameId", id).Build());
lobby.EventReceived += e => Handle(e.From, e.Name, e.Payload);
```

`event` is the opaque one: the gateway routes it by scope and never reads the payload.
Dungeon-entry negotiation, emotes and trades all ride on it. The payload is a
`JsonValue`, so it arrives exactly as sent — see
[`Yingyeothon.Codec`](../packages/com.yingyeothon.codec/README.md).

`Event` is gated on the `event` capability **alone**, never on the chat scope list. A
channel with `say: ["zone"]` and `event: true` still delivers a party event — gating it
on the say list refused frames the gateway would have sent.

## Parties

```csharp
lobby.Party.Create();
lobby.Party.Invite(userId);
lobby.Party.Accept(partyId);
lobby.Party.Decline(partyId);
lobby.Party.Leave();
lobby.Party.List();

lobby.PartyChanged  += roster => Redraw(roster);
lobby.PartyInvited  += invite => Prompt(invite.From, invite.PartyId);
lobby.PartyDeclined += d => Note(d.UserId + " declined");

string? id = lobby.PartyId;       // from hello, or the latest roster
PartyFrame? roster = lobby.Roster;
```

A roster is `{ PartyId, LeaderId, Members[{UserId, Online}], Invited[], Max }`. The
gateway marshals `leaderId`, `invited` and `max` with Go's `omitempty`, so they are
**missing on the wire** when empty; this SDK fills those three in as `""`, an empty list
and `0`, so `Roster.Invited.Count` needs no guard. `PartyId` is **not** normalised — it
stays null, because "no party" is a state the game has to render and an empty string
would hide it.

Party membership survives a disconnect — a dropped member shows as `Online = false` —
and leadership passes to the next member when a leader leaves. Sizes cap at the
channel's `partySizeMax`, with at most twice that many pending invites.

Party membership is also a relay scope, which is why it is the one primitive with
gateway-side meaning: `SayScope.Party` routes to exactly these members.

## The map

```csharp
JsonValue map = await lobby.MapAsync();
```

Fetches `hello.MapUrl`. A success is cached per URL for the life of the client; a
failure is evicted, so the next call retries. The asset is
public and immutable, so the request **carries no credentials** — a new map version is
always a new URL in a later `hello`, which is why publishing a map is a console edit
and never a client rebuild or a CDN invalidation.

The default fetcher bounds the request at 30 seconds, 16 MB and 5 redirects, because
the URL comes off the wire.

Three outcomes are worth knowing:

- A non-2xx answer throws `MapFetchException`, carrying the status.
- **A body over 16 MB of JSON also throws `MapFetchException`, carrying a 2xx status.**
  Too big to parse has to fail rather than degrade; handing back one enormous string is
  the silent breakage the limit exists to prevent.
- **A body that is not JSON is handed back as a JSON string, not refused.** The asset is
  the game's and this SDK only transports it. Reading a field off it then throws
  `JsonKindException`, so check `map.Kind == JsonKind.Object` if the map may be wrong.

`MapAsync` throws `InvalidOperationException` before `hello`. Its continuation is an
ordinary task continuation and **may resume on any thread** — unlike `ConnectAsync` —
so marshal back to the main thread before touching the client or a `Transform`. It also
takes a `CancellationToken`; cancelling yours does not disturb another caller awaiting
the same fetch.

The document's format ("format 2") is specified in `examples/sample-morpg/README.md`
§4.6 in the `service` repository. It is a platform contract shared by the client, the
game actor and any map editor.

## Escape hatches

```csharp
lobby.State;                                  // Idle | Connecting | Connected | Reconnecting | Closed
lobby.Hello;                                  // the latest hello, cached
lobby.Send(Json.Object().Set("type", "something-new").Build());
lobby.Frame += frame => Inspect(frame);       // every frame after hello, pre-handling
lobby.Refused += error => Log(error.Code);    // the gateway refused what you sent
lobby.ProtocolError += e => Log(e.Message);   // a frame this SDK could not read

lobby.Ping();                                 // application-level; the transport already
lobby.Pong += () => { /* answered */ };       // answers the gateway's own ping
```

`Send` puts an arbitrary frame on the wire, for a gateway feature newer than this SDK.
Sends are fire-and-forget: a failed send ends the socket and arrives as a close like any
other failure, because the caller is the game's main thread and must not block on the
network. A refusal comes back as `Refused`, never as a return value.

A socket that goes 75 seconds without a pong is closed `4002` and reconnected without
your involvement, so `Ping` is useful only for measuring round-trip time.

### Reading frames yourself

`Frame` sees everything after `hello`, already typed and with rosters normalised, and
the original JSON on `Raw`. Switch on the type:

```csharp
lobby.Frame += frame =>
{
    switch (frame)
    {
        case SnapshotFrame snapshot: Rebuild(snapshot.Zone, snapshot.Peers); break;
        case EnterFrame enter:       Spawn(enter.Peer); break;
        case LeaveFrame leave:       Despawn(leave.UserId); break;
        case PosBroadcastFrame pos:  Apply(pos.Peers); break;
        case SayBroadcastFrame say:  Show(say.From, say.Text); break;
        case EventBroadcastFrame ev: Handle(ev.Name, ev.Payload); break;
        case PartyFrame roster:      Redraw(roster); break;
        case ErrorFrame error:       Log(error.Code); break;
        case UnknownServerFrame u:   Log("unhandled " + u.Type); break;
    }
};
```

`PartyInviteFrame`, `PartyDeclinedFrame` and `PongFrame` complete the set; all of them
derive from `LobbyServerFrame`, and `FrameTypes` holds the wire names as constants.
`LobbyFrames.Read(JsonValue)` is the same parser, public so a test can build a frame
without a socket.

Everything on `Raw` — and every `q` frame — is a `JsonValue`:
`GetString`, `GetNumber`, `GetBool`, `GetArrayOrEmpty`, `TryGetMember`. The
[codec package](../packages/com.yingyeothon.codec/README.md) is the whole of it.

## Shutting down

`Close()` then `Dispose()`, the latter from `OnDestroy`. See
[Connection lifecycle](connection-lifecycle.md#shutting-down).
