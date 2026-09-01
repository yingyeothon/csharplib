# Yingyeothon.Gamebase.Client

Client SDK for the yyt realtime gateway. It speaks the gateway's two channel kinds —
the **lobby** (positions, chat, parties, game events) and the **dungeon `q` bridge**
to a `@yingyeothon/lambda-gamebase` actor — with typed frames, the bearer-subprotocol
handshake, reconnect with backoff, a ghost-free peer map, and the distinction between
a run that was _aborted_ and one that _finished_.

The normative wire spec is the gateway's own README in the service repository. This file
is the package's reference; **[the guide](../../docs/README.md) is where to start** —
[Getting started](../../docs/getting-started.md),
[Lobby](../../docs/lobby.md), [Dungeon](../../docs/dungeon.md) and
[Errors](../../docs/errors.md) cover every feature listed below in the order a game
needs them.

## Install

```
https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.gamebase-client
```

Add `com.yingyeothon.codec` and `com.yingyeothon.logger` as well; a git-URL package
cannot resolve its own dependencies. Four importable samples ship with it — see
[docs/unity.md](../../docs/unity.md#samples).

## Usage

Lobby:

```csharp
using Yingyeothon.Codec;
using Yingyeothon.Gamebase.Client;

var lobby = GatewayLobbyClient.Create(new GatewayLobbyClientOptions
{
    Url = "wss://gw.yyt.life",
    ChannelId = "ch_lobby",
    Token = channelJwt,          // rides in the subprotocol list, never logged
});

lobby.PeerEnter += Spawn;
lobby.PeerMove += peers => { foreach (var peer in peers) Move(peer); };
lobby.PeerLeave += Despawn;
lobby.Said += frame => ShowChat(frame.From, frame.Text);
lobby.Connected += hello => lobby.Pos(hello.Zone, x, y, "n");   // also after a reconnect

Hello hello = await lobby.ConnectAsync();     // completes on the gateway's `hello`
JsonValue map = await lobby.MapAsync();       // fetches hello.MapUrl once, no credentials
if (lobby.Capabilities?.Party == false) HidePartyUi();
lobby.Say(SayScope.Zone, "hi");
```

Dungeon:

```csharp
var game = GatewayGameClient.Create(new GatewayGameClientOptions
{
    Url = "wss://gw.yyt.life",
    ChannelId = "q_dungeon",
    GameId = gameId,             // from the game's entry API
    Token = channelJwt,
});

game.Frame += ApplySnapshot;                       // every game-defined frame, verbatim
game.Finished += _ => ShowResult();                // close 1000: the game dropped you
game.Aborted += _ => BackToLobby();                // close 4001: retry needs a new GameId

await game.ConnectAsync();
game.Send(Json.Object().Set("type", "attack").Set("power", 3d).Build());
```

## Poll, or nothing happens

Received frames, the `hello` timeout and every reconnect delay are all processed
inside `Poll()`, on the thread that calls it. That is what puts every handler on
Unity's main thread without a synchronization context — and it means a client that is
never polled never reconnects.

```csharp
void Update()
{
    lobby.Poll();
    game?.Poll();
}
```

`GamebaseRunner` (Unity only) does this for a set of clients:

```csharp
var runner = GamebaseRunner.CreatePersistent();
runner.Add(lobby);
```

Call `Poll()` unconditionally, before any pause or `timeScale` check. Use the client
from one thread at a time: `Poll()` and every other call refuse while another thread
is inside `Poll()`. Sending from inside a handler is fine — that is the normal way to
answer an event — and `ConnectAsync` resumes on the pump, so `Send` is legal straight
after `await`. The connect task is settled on the thread that called `Poll()`, and
your `await` then resumes on your own synchronization context: Unity's main thread in
a game, inline in a console host. A `MapAsync()` continuation is a normal task
continuation and may land anywhere — marshal back before touching the client.

## Reconnect policy

| Close code             | Lobby                    | Dungeon (`q`)                    |
| ---------------------- | ------------------------ | -------------------------------- |
| `4000` replaced        | `Stopped`                | `Stopped`                        |
| `4001` actor abort     | `Stopped`                | `Aborted` — new `GameId` needed  |
| `4002` idle            | reconnect                | reconnect                        |
| `4003` policy          | `Stopped` (client bug)   | `Stopped` (client bug)           |
| `4004` channel gone    | `Stopped`                | `Stopped`                        |
| `1000` normal          | `Stopped`                | `Finished`                       |
| `1001` gateway restart | reconnect                | reconnect                        |
| `1003` binary frame    | `Stopped` (client bug)   | `Stopped` (client bug)           |
| `1009` frame too large | `Stopped` (client bug)   | `Stopped` (client bug)           |
| `1011` enter failed    | reconnect                | reconnect                        |
| anything else          | reconnect                | reconnect                        |

Reconnects use exponential backoff (500 ms, ×2, cap 15 s, ±20 % jitter) until
`Backoff.MaxAttempts` is exhausted, which ends in `Stopped`. A refused handshake
(401/403/404/410) is invisible except as a close before open, so
`MaxHandshakeFailures` consecutive closes-before-open (default 5) also end the
session instead of retrying a dead token forever; the counter resets on every
successful open. `Disconnected` fires before every reconnect or stop with
`WillReconnect` set. On the lobby, `Connected` fires again with the new `Hello` and
the peer map is empty until the game re-sends `Pos`.

## Wire details worth knowing

- `dir` is the game's own facing token, an opaque **string** of at most 16 **bytes**
  (`"n"`, `"left"`, …). The gateway parses `pos` with a string field, so a numeric
  `dir` makes the whole frame a `bad_message` and the position is dropped. This SDK
  offers no numeric overload, and `Pos` throws locally on a longer one — a Hangul
  syllable spends three of the sixteen bytes.
- The `party` roster is marshalled with Go `omitempty`: `leaderId`, `invited` and
  `max` are missing on the wire when empty. The lobby client fills them in as `""`,
  an empty list and `0` before the frame reaches you, so `Roster.Invited.Count` needs
  no guard.
- `Capabilities` fields are nullable and `null` means **unrestricted**, not disabled.
  A channel that restricts no chat scopes marshals `"say": null`, and only an
  explicit `false` turns a capability off.

## Public API

- `GatewayLobbyClient.Create(GatewayLobbyClientOptions)` → `IGatewayLobbyClient`:
  `ConnectAsync`, `Close`, `Poll`, `State`, `Hello`, `Capabilities`, `PartyId`,
  `Roster`, `Peers`, `MapAsync`, senders `Pos` / `Say` / `Event` / `Ping` / `Send`,
  `Party.Create/Invite/Accept/Decline/Leave/List`, and the events `Connected`,
  `Disconnected`, `Reconnecting`, `Stopped`, `Snapshot`, `PeerEnter`, `PeerLeave`,
  `PeerMove`, `Said`, `EventReceived`, `PartyChanged`, `PartyInvited`,
  `PartyDeclined`, `Pong`, `Refused`, `ProtocolError`, `Frame`. Senders throw locally
  when `Capabilities` disables them or before `hello`.
- `GatewayGameClient.Create(GatewayGameClientOptions)` → `IGatewayGameClient`:
  `ConnectAsync`, `Close`, `Poll`, `State`, `Send` (refuses the reserved
  `enter` / `leave` types), and `Connected`, `Frame`, `Refused`, `Disconnected`,
  `Reconnecting`, `Aborted`, `Finished`, `Stopped`, `ProtocolError`.
- `PeerMap.Create(PeerMapOptions)` → `IPeerMap`: the reducer behind `Peers` —
  `Apply`, `Get`, `All`, `Zone`, `Reset` — returning a `PeerChange` (`PeerChangeKind`)
  for each frame it accepted, or null for one it ignored.
- `Backoff.Create(BackoffOptions)` → `IBackoff`: `Next`, `Reset`, `Attempts`.
- `CloseCodes.Classify(code, kind)`, `GatewayCloseCode`, `CloseDisposition`,
  `CloseDispositionKind`, `GatewayChannelKind`.
- `GatewayUrl.Build(url, channelId, gameId?)`, `FrameTypes`, `GatewayErrorCode`,
  `SayScope`, `SayScopes`.
- `LobbyFrames.Read(JsonValue)` and the frame types `Hello`, `Capabilities`, `Peer`,
  `SnapshotFrame`, `EnterFrame`, `LeaveFrame`, `PosBroadcastFrame`,
  `SayBroadcastFrame`, `EventBroadcastFrame`, `PartyFrame`, `PartyMember`,
  `PartyInviteFrame`, `PartyDeclinedFrame`, `PongFrame`, `ErrorFrame`,
  `UnknownServerFrame`, all deriving from `LobbyServerFrame`.
- Options: `GatewayClientOptions` is the base of `GatewayLobbyClientOptions` and
  `GatewayGameClientOptions`; `IPartyCommands` is the type of `Party`.
- Transport seams: `IWebSocket`, `IWebSocketFactory`, `IWebSocketEventSink`,
  `SocketEvent` (`SocketEventKind`), `WebSocketCreateContext`,
  `WebSocketTransport.Default`,
  `IHttpFetcher`, `HttpFetchResult`, `HttpFetcher.Default`, `MapFetchException`.
- Clock and pump: `IClock`, `SystemClock.Instance`, `IGatewayPollable`.
- Events: `GatewayClientState`, `DisconnectedEvent`, `ReconnectingEvent`,
  `StoppedEvent`, `ProtocolErrorEvent`, `GameEndedEvent`, `GatewayStoppedException`.
- Unity only: `GamebaseRunner`.

## Unity WebGL

`ClientWebSocket` and `HttpClient` do not work on WebGL, and there is no thread for a
receive loop. Pass your own `WebSocketFactory` (for example an adapter over a
`.jslib` socket) and `HttpFetcher` (over `UnityWebRequest`) through the client
options; the rest of the SDK is unchanged. On every other platform the defaults work
as they are.

## Differences from `@yingyeothon/gamebase-client`

- **`Poll()`.** tslib runs on the browser's event loop; here the receive side only
  enqueues and every transition happens in `Poll()`, so handlers land on Unity's main
  thread and every timeout is deterministic in tests.
- **C# events instead of `on(type, handler)`.** A multicast delegate already gives
  the "snapshot at raise time" behaviour tslib hand-rolls, with no dictionary lookup
  per frame. C# forbids a method and an event sharing a name, so the receiving side
  of a sender is renamed: `say` → `Said`, `event` → `EventReceived`, `party` →
  `PartyChanged`, `party.invite` → `PartyInvited`, `party.declined` →
  `PartyDeclined`, `error` → `Refused`.
- **A refused handshake is a close, not an exception.** The default transport turns
  every post-construction failure into close 1006, because the reconnect policy is
  written against what a browser can see.
- Frames are parsed by hand into typed classes, with the original `JsonValue` kept on
  `Raw`, so nothing depends on reflection under IL2CPP.

## Wire decisions checked against the gateway's Go source

These were re-derived field by field from `gateway/internal/lobby/protocol.go` and
`hub.go` in the `service` repository — the normative spec — rather than from tslib.

- **`Event` is gated on the `event` capability alone**, never on the `say` scope list.
  The consumer-facing statement is in [docs/lobby.md](../../docs/lobby.md#game-events).
  `handleEventLocked` checks `Capabilities.Event` and routes the scope; it never calls
  `AllowsSay`. A channel with `say: ["zone"]` and `event: true` still delivers a party
  event, so gating it here refused a frame the gateway would have sent.
- **A `pos` that omits `dir` clears the peer's facing.** The gateway rebuilds the
  whole peer from each inbound frame and marshals it with `dir,omitempty`, so an
  omitted `dir` is a statement, not a silence.
- **`say: null` is treated as unrestricted, and the gateway never sends it.**
  `capabilities()` always marshals a non-nil slice, so the real shape for a
  chat-disabled channel is `say: []`, which refuses every scope. The permissive
  reading of `null` is a deliberate choice for a shape that cannot occur: being
  stricter than the server throws inside `Update()` for a frame the server would have
  accepted, which is the worse failure.
- **A `q` frame has no required shape.** The bridge forwards the actor's message with
  `SendRaw`, verbatim, so an array, a number or a bare string is a legitimate game
  frame. Only the lobby has a vocabulary. A `q` refusal is recognised by a string
  `code` alone, because the gateway's `ErrorFrame` marks `message` `omitempty`.
- **`SendText` is fire-and-forget by design.** The caller is the game's main thread and
  must not block on the network; a send that fails ends the socket and arrives as a
  close event like every other failure. There is no per-send result.
- **What the SDK does not check locally.** The gateway also refuses `zone` over 64
  bytes, `text` empty or over 1024 bytes, `name` over 64 bytes and `payload` over
  8 KB. Only `dir` is checked here. Each refusal increments a per-socket counter and
  fifty of them close with 4003, so a chat box that lets a player paste 2 KB will end
  the session — validate the text before calling `Say`.
- **Inbound messages are capped at 64 KB.** A larger one arrives as close 1009 and stops
  rather than reconnects. The three different caps that produce 1009 are in
  [docs/errors.md](../../docs/errors.md#close-codes).
