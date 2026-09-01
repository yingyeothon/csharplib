# Yingyeothon.Gamebase.Client

<!-- Generated from the assembly by tests/Yingyeothon.PublicApi.Tests.
     Do not edit by hand: the test rewrites it and CI compares it. -->

Every public type and member, with its documentation comment — the same text
your IDE shows. For what the package is *for*, read
[the guide](../README.md) and
[`packages/com.yingyeothon.gamebase-client/README.md`](../../packages/com.yingyeothon.gamebase-client/README.md).

## Contents

- [`Backoff`](#static-class-backoff)
- [`BackoffOptions`](#class-backoffoptions)
- [`Capabilities`](#class-capabilities)
- [`CloseCodes`](#static-class-closecodes)
- [`CloseDisposition`](#struct-closedisposition)
- [`CloseDispositionKind`](#enum-closedispositionkind)
- [`DisconnectedEvent`](#struct-disconnectedevent)
- [`EnterFrame`](#class-enterframe)
- [`ErrorFrame`](#class-errorframe)
- [`EventBroadcastFrame`](#class-eventbroadcastframe)
- [`FrameTypes`](#static-class-frametypes)
- [`GameEndedEvent`](#struct-gameendedevent)
- [`GatewayChannelKind`](#enum-gatewaychannelkind)
- [`GatewayClientOptions`](#class-gatewayclientoptions)
- [`GatewayClientState`](#enum-gatewayclientstate)
- [`GatewayCloseCode`](#static-class-gatewayclosecode)
- [`GatewayErrorCode`](#static-class-gatewayerrorcode)
- [`GatewayGameClient`](#static-class-gatewaygameclient)
- [`GatewayGameClientOptions`](#class-gatewaygameclientoptions)
- [`GatewayLobbyClient`](#static-class-gatewaylobbyclient)
- [`GatewayLobbyClientOptions`](#class-gatewaylobbyclientoptions)
- [`GatewayStoppedException`](#class-gatewaystoppedexception)
- [`GatewayUrl`](#static-class-gatewayurl)
- [`Hello`](#class-hello)
- [`HttpFetchResult`](#struct-httpfetchresult)
- [`HttpFetcher`](#static-class-httpfetcher)
- [`IBackoff`](#interface-ibackoff)
- [`IClock`](#interface-iclock)
- [`IGatewayGameClient`](#interface-igatewaygameclient)
- [`IGatewayLobbyClient`](#interface-igatewaylobbyclient)
- [`IGatewayPollable`](#interface-igatewaypollable)
- [`IHttpFetcher`](#interface-ihttpfetcher)
- [`IPartyCommands`](#interface-ipartycommands)
- [`IPeerMap`](#interface-ipeermap)
- [`IWebSocket`](#interface-iwebsocket)
- [`IWebSocketEventSink`](#interface-iwebsocketeventsink)
- [`IWebSocketFactory`](#interface-iwebsocketfactory)
- [`LeaveFrame`](#class-leaveframe)
- [`LobbyFrames`](#static-class-lobbyframes)
- [`LobbyServerFrame`](#class-lobbyserverframe)
- [`MapFetchException`](#class-mapfetchexception)
- [`PartyDeclinedFrame`](#class-partydeclinedframe)
- [`PartyFrame`](#class-partyframe)
- [`PartyInviteFrame`](#class-partyinviteframe)
- [`PartyMember`](#class-partymember)
- [`Peer`](#class-peer)
- [`PeerChange`](#class-peerchange)
- [`PeerChangeKind`](#enum-peerchangekind)
- [`PeerMap`](#static-class-peermap)
- [`PeerMapOptions`](#class-peermapoptions)
- [`PongFrame`](#class-pongframe)
- [`PosBroadcastFrame`](#class-posbroadcastframe)
- [`ProtocolErrorEvent`](#struct-protocolerrorevent)
- [`ReconnectingEvent`](#struct-reconnectingevent)
- [`SayBroadcastFrame`](#class-saybroadcastframe)
- [`SayScope`](#enum-sayscope)
- [`SayScopes`](#static-class-sayscopes)
- [`SnapshotFrame`](#class-snapshotframe)
- [`SocketEvent`](#struct-socketevent)
- [`SocketEventKind`](#enum-socketeventkind)
- [`StoppedEvent`](#struct-stoppedevent)
- [`SystemClock`](#class-systemclock)
- [`UnknownServerFrame`](#class-unknownserverframe)
- [`WebSocketCreateContext`](#class-websocketcreatecontext)
- [`WebSocketTransport`](#static-class-websockettransport)

## static class Backoff

Creates `IBackoff` schedules.

| Member | Summary |
| --- | --- |
| `Create() : IBackoff` | A schedule with the default 500 ms, doubling, capped at 15 s, 20% jitter. |
| `Create(BackoffOptions) : IBackoff` | A schedule with the given parameters. |

## class BackoffOptions

Options for `Create` .

| Member | Summary |
| --- | --- |
| `Factor : Double get set` | Multiplier applied per attempt. |
| `InitialMs : Double get set` | Delay before the first retry. |
| `Jitter : Double get set` | Fraction of the delay randomised on both sides. |
| `MaxAttempts : Nullable<Int32> get set` | Give up after this many consecutive attempts; null is unbounded. |
| `MaxMs : Double get set` | Upper bound on any delay. |
| `Random : Func<Double> get set` | Random source in [0, 1). Injectable so a test can pin the jitter. |
| `ctor()` |  |

## class Capabilities

The channel's capability object, forwarded verbatim in `hello` .

| Member | Summary |
| --- | --- |
| `AllowsScope(SayScope) : Boolean` | Whether this scope may be used, given what the channel allows. |
| `Debug : Nullable<Boolean> get` | Reserved for admin commands. The gateway implements none yet. |
| `Event : Nullable<Boolean> get` | Whether game events may be sent. Gates `Event` alone, never the say list. |
| `Party : Nullable<Boolean> get` | Whether the party commands are available. Null is unrestricted. |
| `Pos : Nullable<Boolean> get` | Whether positions may be sent. Null is unrestricted. |
| `Say : IReadOnlyList<String> get` | Allowed say/event scopes, or null when the channel restricts none. |
| `ctor(Nullable<Boolean>, IReadOnlyList<String>, Nullable<Boolean>, Nullable<Boolean>, Nullable<Boolean>)` |  |

## static class CloseCodes

Maps a close code to what the client should do.

| Member | Summary |
| --- | --- |
| `Classify(Int32, GatewayChannelKind) : CloseDisposition` | Every code the gateway documents is listed; anything else is treated as a transient network failure and retried with backoff. |

## struct CloseDisposition

A close code's meaning for this channel kind.

| Member | Summary |
| --- | --- |
| `Kind : CloseDispositionKind get` | What the client should do about it. |
| `Reason : String get` | A fixed description of the code. Never text the peer chose. |
| `ctor(CloseDispositionKind, String)` |  |

## enum CloseDispositionKind

What a client should do about a close.

- `Aborted` — A `q` run died. A retry needs a new `gameId` .
- `ClientBug` — Terminal, and the client caused it. Retrying repeats it.
- `Finished` — A `q` run ended normally and the game dropped the connection.
- `Reconnect` — Transient. Retry with backoff.
- `Stop` — Terminal, and not the client's fault. Do not retry.

## struct DisconnectedEvent

The connection dropped.

| Member | Summary |
| --- | --- |
| `Code : Int32 get` | The WebSocket close code. See `GatewayCloseCode` . |
| `Reason : String get` | The close reason as the SDK classified it. Never the peer's own text. |
| `WillReconnect : Boolean get` | Whether a reconnect is already scheduled. |
| `ctor(Int32, String, Boolean)` |  |

## class EnterFrame

A peer entered the receiver's zone.

| Member | Summary |
| --- | --- |
| `Peer : Peer get` | The peer that became visible. |
| `Zone : String get` | The zone the peer entered. |

## class ErrorFrame

A typed refusal. Every refusal is a frame, never silence.

| Member | Summary |
| --- | --- |
| `Code : String get` | A documented refusal code; the set is open, so compare against `GatewayErrorCode` constants. |
| `Message : String get` | The gateway's explanation. Never log it: it may quote what the client sent. |

## class EventBroadcastFrame

The opaque game-defined relay; the payload is forwarded unread.

| Member | Summary |
| --- | --- |
| `From : String get` | The sender's `userId` . |
| `Name : String get` | The game-defined event name. |
| `Payload : JsonValue get` | The game's own payload, untouched. Null when the field was absent. |
| `Scope : String get` | The wire scope: `zone` , `party` or `user` . |
| `To : String get` | The addressee, when the event was sent to one user. |

## static class FrameTypes

Frame type names, matching the gateway's own constants.

| Member | Summary |
| --- | --- |
| `Enter : String` | A peer became visible in the zone. Synthesised by the gateway. |
| `Error : String` | A refusal of something the client sent. |
| `Event : String` | A game-defined event, routed by scope. The gateway never reads its payload. |
| `Hello : String` | The gateway's first frame on a lobby channel. Nothing is connected before it. |
| `Leave : String` | A peer stopped being visible in the zone. Synthesised by the gateway. |
| `Party : String` | A party roster snapshot. |
| `PartyAccept : String` | Accept an invitation. |
| `PartyCreate : String` | Create a party with the sender as its leader. |
| `PartyDecline : String` | Decline an invitation. |
| `PartyDeclined : String` | Someone declined an invitation to the sender's party. |
| `PartyInvite : String` | Outbound, invite a user; inbound, an invitation arrived. |
| `PartyLeave : String` | Leave the current party. |
| `PartyList : String` | Ask for the current roster. |
| `Ping : String` | An application-level liveness probe. |
| `Pong : String` | The gateway's answer to a ping. |
| `Pos : String` | A position: outbound one player's, inbound a batch coalesced per tick. |
| `ReservedGameFrameTypes : IReadOnlyList<String>` | Types the gateway synthesises itself and refuses from a client. It decides which member a connection speaks for, so a client must never send one. |
| `Say : String` | Chat, routed by scope. |
| `Snapshot : String` | Every retained peer in a zone, sent to a newcomer instead of one `enter` each. |

## struct GameEndedEvent

A dungeon run ended, either aborted or finished.

| Member | Summary |
| --- | --- |
| `Code : Int32 get` | The close code: 1000 for a normal finish, 4001 for an abort. |
| `Reason : String get` | How the run ended, as the SDK classified it. |
| `ctor(Int32, String)` |  |

## enum GatewayChannelKind

The two channel kinds the gateway terminates.

- `Lobby` — Positions, chat, parties and game events.
- `Q` — The dungeon bridge to a lambda-gamebase actor.

## class GatewayClientOptions

Options shared by both gateway clients.

| Member | Summary |
| --- | --- |
| `Backoff : BackoffOptions get set` | Reconnect schedule; defaults to 500 ms, doubling, capped at 15 s, with 20% jitter. |
| `ChannelId : String get set` | The channel to join, as the console shows it: `lobby_…` or `q_…` . |
| `Clock : IClock get set` | Defaults to a monotonic system clock; a test injects its own. |
| `Logger : ILogger get set` | Defaults to a logger that discards everything. This SDK never logs the token. |
| `MaxHandshakeFailures : Int32 get set` | Consecutive closes before open that end the session. |
| `Token : String get set` | The channel JWT. It rides in the subprotocol list, never in the URL, and is never logged. |
| `Url : String get set` | Gateway origin, e.g. `wss://gw.yyt.life` . The query string is added by the SDK. |
| `WebSocketFactory : IWebSocketFactory get set` | Defaults to `Default` ; required on Unity WebGL. |

## enum GatewayClientState

Where a client's connection currently is.

- `Closed` — Terminal. A client that reached this cannot be reissued.
- `Connected` — Usable. On a lobby channel that means `hello` has arrived.
- `Connecting` — A socket is opening, or the lobby is waiting for `hello` .
- `Idle` — Created, but `ConnectAsync` has not been called.
- `Reconnecting` — The connection dropped and a retry is scheduled.

## static class GatewayCloseCode

Application close codes the gateway uses.

| Member | Summary |
| --- | --- |
| `Aborted : Int32` | q only: the actor stopped consuming; the run is aborted, not finished. |
| `ChannelGone : Int32` | The channel expired or was disabled. |
| `Idle : Int32` | No pong within the idle window. |
| `Local : Int32` | The code this SDK closes with when it ends a socket itself. A client may only send 1000 or 3000-4999, and this one is not used by the gateway. |
| `Policy : Int32` | Too many refused messages on one socket; a client bug. |
| `Replaced : Int32` | A newer socket of the same user replaced this one. Do not reconnect. |

## static class GatewayErrorCode

Documented gateway refusal codes. The set is open; do not close it into an enum.

| Member | Summary |
| --- | --- |
| `AlreadyInParty : String` | The sender is already in a party. |
| `BadMessage : String` | The frame did not parse, a field had the wrong type, or an event `name` was outside 1..64 bytes. A numeric `dir` or a comma-decimal number reaches the gateway this way. |
| `BadScope : String` | The scope is not one this channel allows. |
| `BadZone : String` | The zone name is malformed or over 64 bytes. |
| `CapabilityOff : String` | The channel disables that command. |
| `MoveTooFar : String` | The position moved further in one frame than the channel's `maxMoveDelta` . |
| `NoParty : String` | The command needs a party and the sender is in none. |
| `NotInvited : String` | Accepting a party the sender was not invited to. |
| `NotLeader : String` | The command is the party leader's to make. |
| `PartyFull : String` | The party is at the channel's `partySizeMax` . |
| `RateLimited : String` | Over the channel's per-connection message rate. |
| `ReservedType : String` | A `q` frame used `enter` or `leave` , which are the gateway's own. |
| `TooLong : String` | A field is over its byte cap: `text` 1024 or `payload` 8192. An out-of-range `name` is `bad_message` instead. |
| `Unavailable : String` | The gateway could not serve the request right now. |
| `UnknownParty : String` | No such party. |
| `UnknownUser : String` | No such user on this channel. |

## static class GatewayGameClient

Creates dungeon clients.

| Member | Summary |
| --- | --- |
| `Create(GatewayGameClientOptions) : IGatewayGameClient` | Creates a dungeon client. Options are copied, so later edits to them change nothing. |

## class GatewayGameClientOptions

Options for `Create` .

| Member | Summary |
| --- | --- |
| `GameId : String get set` | The run to join; the caller must be in its start event's members. |
| `ctor()` |  |

## static class GatewayLobbyClient

Creates lobby clients.

| Member | Summary |
| --- | --- |
| `Create(GatewayLobbyClientOptions) : IGatewayLobbyClient` | Creates a lobby client. Options are copied, so later edits to them change nothing. |

## class GatewayLobbyClientOptions

Options for `Create` .

| Member | Summary |
| --- | --- |
| `HelloTimeoutMillis : Double get set` | How long to wait for `hello` after the socket opens. |
| `HttpFetcher : IHttpFetcher get set` | Used by `MapAsync` ; defaults to `Default` . |
| `ctor()` |  |

## class GatewayStoppedException

The connection ended before it became usable.

| Member | Summary |
| --- | --- |
| `ctor(String)` |  |

## static class GatewayUrl

Builds the gateway's WebSocket URL.

| Member | Summary |
| --- | --- |
| `Build(String, String, String?) : String` | Produces `{url}?channel={channelId}[&gameId={gameId}]` , keeping any query string already on `url` . |

## class Hello

The first frame on a lobby channel; nothing is "connected" before it. The client holds no configuration and learns everything here.

| Member | Summary |
| --- | --- |
| `Capabilities : Capabilities get` | What the channel enables. A null field means unrestricted, not disabled. |
| `ConnectionId : String get` | This socket. A reconnect gets a new one, and only the gateway may set it. |
| `MapUrl : String get` | Immutable, public map asset. A new map version is a new URL. |
| `PartyId : String get` | Set when the gateway already knows this player's party; null otherwise. |
| `Raw : JsonValue get` | The frame as received, so a field this SDK does not model is still reachable. |
| `Tick : Int32 get` | Position flush interval in milliseconds (the channel's `flushIntervalMs` ). |
| `UserId : String get` | This player's identity, the same value as the token's `sub` . |
| `Zone : String get` | The zone the game should start in; the player has no zone until the first `pos` . |
| `ctor(String, String, Int32, String, String, String, Capabilities, JsonValue)` |  |

## struct HttpFetchResult

The result of a map fetch.

| Member | Summary |
| --- | --- |
| `Ok : Boolean get` | Whether the status was a success. |
| `Status : Int32 get` | The HTTP status code. |
| `Text : String get` | The response body. Never log it: the URL came off the wire. |
| `ctor(Boolean, Int32, String)` |  |

## static class HttpFetcher

The default `IHttpFetcher` , over one shared `HttpClient` .

| Member | Summary |
| --- | --- |
| `Default : IHttpFetcher get` | An `HttpClient` bounded at 30 seconds, 16 MB and 5 redirects. Does not work on Unity WebGL. |

## interface IBackoff

An exponential backoff schedule with jitter.

| Member | Summary |
| --- | --- |
| `Attempts : Int32 get` | Consecutive attempts since the last `Reset` . |
| `Next() : Nullable<Double>` | The next delay in milliseconds, or null once the attempts are exhausted. |
| `Reset() : Void` |  |

## interface IClock

A monotonic millisecond clock. Injected so timeouts are testable.

| Member | Summary |
| --- | --- |
| `NowMillis : Double get` | A monotonic reading in milliseconds. Only differences between readings are meaningful. |

## interface IGatewayGameClient

A client for the gateway's dungeon ( `q` ) channel.

| Member | Summary |
| --- | --- |
| `Close() : Void` | Closes the connection. No reconnect follows. |
| `ConnectAsync() : Task` | Completes once the socket is open with the bearer subprotocol echoed. |
| `Send(JsonValue) : Void` | Sends a game frame. `enter` and `leave` are refused locally. |
| `State : GatewayClientState get` | Where this client's connection currently is. |
| `event Aborted : Action<GameEndedEvent>` | Close 4001: the actor died. Retry only with a new `GameId` . |
| `event Connected : Action` | The socket is open and the gateway has pushed `enter` to the actor. Fires again after a reconnect; the game answers with its own snapshot. |
| `event Disconnected : Action<DisconnectedEvent>` | The connection dropped. Fires before every reconnect and before every stop. |
| `event Finished : Action<GameEndedEvent>` | Close 1000: the game dropped this connection after ending normally. |
| `event Frame : Action<JsonValue>` | Every game-defined frame, verbatim. |
| `event ProtocolError : Action<ProtocolErrorEvent>` | A frame arrived that this SDK could not read. |
| `event Reconnecting : Action<ReconnectingEvent>` | A retry is scheduled, with its attempt number and delay. |
| `event Refused : Action<ErrorFrame>` | A gateway refusal. |
| `event Stopped : Action<StoppedEvent>` | Any other terminal close. |

## interface IGatewayLobbyClient

A client for the gateway's lobby channel.

| Member | Summary |
| --- | --- |
| `Capabilities : Capabilities get` | What the channel enables, from `hello` . Null on a field means unrestricted. |
| `Close() : Void` | Closes the connection. No reconnect follows. |
| `ConnectAsync() : Task<Hello>` | Completes with `hello` . Fails if the connection stops before that. |
| `Event(SayScope, String, JsonValue, String?) : Void` | Sends a game-defined event the gateway routes by scope but never reads. |
| `Hello : Hello get` | The latest `hello` , once connected. |
| `MapAsync(CancellationToken?) : Task<JsonValue>` | Fetches `hello.mapUrl` , cached per URL. |
| `Party : IPartyCommands get` | The party commands, when the channel enables them. |
| `PartyId : String get` | Current party, from `hello.partyId` or the latest roster. |
| `Peers : IPeerMap get` | The peers visible in the current zone, with the receiver's own entry filtered out. |
| `Ping() : Void` | Sends an application-level liveness probe; the answer arrives as `Pong` . |
| `Pos(String, Double, Double, String?) : Void` | Announces a position. Until the first call the player has no zone at all. |
| `Roster : PartyFrame get` | The latest roster frame, if any. |
| `Say(SayScope, String, String?) : Void` | Sends chat. The gateway refuses text that is empty or over 1024 bytes. |
| `Send(JsonValue) : Void` | Escape hatch for a frame the helpers do not cover. |
| `State : GatewayClientState get` | Where this client's connection currently is. |
| `event Connected : Action<Hello>` | `hello` arrived; fires again after every successful reconnect. |
| `event Disconnected : Action<DisconnectedEvent>` | The connection dropped. Fires before every reconnect and before every stop. |
| `event EventReceived : Action<EventBroadcastFrame>` | A game event arrived. Named for the same reason as `Said` . |
| `event Frame : Action<LobbyServerFrame>` | Every frame after `hello` , before any SDK handling. Rosters are already normalised. |
| `event PartyChanged : Action<PartyFrame>` | A roster snapshot arrived. |
| `event PartyDeclined : Action<PartyDeclinedFrame>` | Someone declined an invitation to this player's party. |
| `event PartyInvited : Action<PartyInviteFrame>` | Someone invited this player to a party. |
| `event PeerEnter : Action<Peer>` | A peer became visible in the current zone. |
| `event PeerLeave : Action<String>` | A peer stopped being visible; the argument is its `userId` . |
| `event PeerMove : Action<IReadOnlyList<Peer>>` | Known peers moved. The receiver's own entry is already filtered out. |
| `event Pong : Action` | The gateway answered a ping. |
| `event ProtocolError : Action<ProtocolErrorEvent>` | A frame arrived that this SDK could not read. |
| `event Reconnecting : Action<ReconnectingEvent>` | A retry is scheduled, with its attempt number and delay. |
| `event Refused : Action<ErrorFrame>` | The gateway refused something this client sent. |
| `event Said : Action<SayBroadcastFrame>` | Chat arrived. Named `Said` because `Say` is the sender. |
| `event Snapshot : Action<SnapshotFrame>` | The zone was replaced wholesale, which is how a zone change starts. |
| `event Stopped : Action<StoppedEvent>` | Terminal: no further attempt will be made. |

## interface IGatewayPollable

Something that must be pumped from the caller's own thread.

| Member | Summary |
| --- | --- |
| `Poll() : Void` | Drains what arrived, advances the timers, and raises the handlers — on the calling thread. |

## interface IHttpFetcher

A credential-free HTTP GET.

| Member | Summary |
| --- | --- |
| `GetAsync(String, CancellationToken) : Task<HttpFetchResult>` | Fetches a public URL. Bound it: a timeout, a size cap and a small redirect budget. |

## interface IPartyCommands

The party commands a lobby channel offers.

| Member | Summary |
| --- | --- |
| `Accept(String) : Void` | Accepts a pending invitation. |
| `Create() : Void` | Creates a party with the caller as its leader. |
| `Decline(String) : Void` | Declines a pending invitation. |
| `Invite(String) : Void` | Invites a user. The leader's command. |
| `Leave() : Void` | Leaves the current party. Leadership passes to the next member. |
| `List() : Void` | Asks for the current roster; it arrives as `PartyChanged` . |

## interface IPeerMap

The set of peers visible in the current zone.

| Member | Summary |
| --- | --- |
| `All() : IReadOnlyList<Peer>` | Every peer currently visible, in the order they arrived. |
| `Apply(LobbyServerFrame) : PeerChange` | Applies one frame; returns the change it produced, or null when it was ignored. |
| `Get(String) : Peer` | One peer, or null when the map does not know it. |
| `Reset() : Void` | Forgets every peer and the current zone. The client does this on every disconnect. |
| `Zone : String get` | The zone of the last snapshot, or null before one arrives. |

## interface IWebSocket

A WebSocket, reduced to what this SDK needs.

| Member | Summary |
| --- | --- |
| `Close(Int32, String) : Void` | Requests a close. Valid client codes are 1000 and 3000-4999. |
| `SendText(String) : Void` | Sends one text frame. |
| `Start() : Void` | Begins connecting. The outcome arrives on the sink, never as a throw. |

## interface IWebSocketEventSink

Where a socket posts what it observed.

| Member | Summary |
| --- | --- |
| `Post(SocketEvent) : Void` | Enqueues one observation. The only thing a transport may do from its own thread. |

## interface IWebSocketFactory

Builds sockets. Injectable because Unity WebGL has no usable `ClientWebSocket` and a test needs a socket it drives as the server.

| Member | Summary |
| --- | --- |
| `Create(WebSocketCreateContext) : IWebSocket` | Builds a socket. May throw for input it can reject up front; everything after that must arrive as a close. |

## class LeaveFrame

A peer left the receiver's zone.

| Member | Summary |
| --- | --- |
| `UserId : String get` | The peer that stopped being visible. |
| `Zone : String get` | The zone the peer left. |

## static class LobbyFrames

Turns a decoded gateway frame into a typed `LobbyServerFrame` .

| Member | Summary |
| --- | --- |
| `Read(JsonValue) : LobbyServerFrame` | Reads a decoded lobby frame, taking its kind from the `type` field. |

## class LobbyServerFrame

A frame the gateway sent on a lobby channel.

| Member | Summary |
| --- | --- |
| `Raw : JsonValue get` | The frame as received, after party normalisation. |
| `Type : String get` | The frame's `type` field, as it arrived. |

## class MapFetchException

A map fetch that did not return 2xx.

| Member | Summary |
| --- | --- |
| `Status : Int32 get` | The HTTP status the map URL answered with. |
| `ctor(Int32)` |  |

## class PartyDeclinedFrame

Tells the leader an invite was refused.

| Member | Summary |
| --- | --- |
| `PartyId : String get` | The party whose invitation was declined. |
| `UserId : String get` | Who declined it. |

## class PartyFrame

The party snapshot sent on every change and on reconnect.

| Member | Summary |
| --- | --- |
| `Invited : IReadOnlyList<String> get` | Pending invitations. Normalised to an empty list when the wire omitted it. |
| `LeaderId : String get` | The leader. Normalised to an empty string when the wire omitted it. |
| `Max : Int32 get` | The channel's party size cap. Normalised to zero when the wire omitted it. |
| `Members : IReadOnlyList<PartyMember> get` | The roster. |
| `PartyId : String get` | The party, or null when the frame says "you are in no party". |

## class PartyInviteFrame

An invite delivered to the invitee.

| Member | Summary |
| --- | --- |
| `From : String get` | Who sent the invitation. |
| `PartyId : String get` | The party being offered. |

## class PartyMember

A roster entry. `Online` lets a client grey out a member whose socket dropped.

| Member | Summary |
| --- | --- |
| `Online : Boolean get` | False while the member is disconnected; membership survives a drop. |
| `UserId : String get` | The member's identity. |

## class Peer

One retained position.

| Member | Summary |
| --- | --- |
| `Dir : String get` | The game's own facing token, an opaque string of at most 16 bytes ( `"n"` , `"left"` , ...), or null when the game has no facing. |
| `UserId : String get` | The peer's identity. |
| `X : Double get` | Position on the map's x axis. A `double` , because the wire is Go `float64` . |
| `Y : Double get` | Position on the map's y axis. |
| `ctor(String, Double, Double, String)` |  |

## class PeerChange

One change produced by `Apply` .

| Member | Summary |
| --- | --- |
| `Kind : PeerChangeKind get` | What kind of change the frame produced. |
| `Peers : IReadOnlyList<Peer> get` | The peers a snapshot, enter or move concerns; empty on a leave. |
| `UserId : String get` | Set on a leave. |
| `Zone : String get` | Set on a snapshot. |

## enum PeerChangeKind

What applying a frame did to the peer map.

- `Enter` — One peer became visible.
- `Leave` — One peer stopped being visible.
- `Move` — One or more known peers moved.
- `Snapshot` — The zone was replaced wholesale.

## static class PeerMap

Reduces the gateway's snapshot / enter / leave / pos frames into the peers visible in the current zone.

| Member | Summary |
| --- | --- |
| `Create(PeerMapOptions) : IPeerMap` | Creates a peer map that filters out the receiver's own entries. |

## class PeerMapOptions

Options for `Create` .

| Member | Summary |
| --- | --- |
| `SelfUserId : String get set` | The receiver's own userId; its entry in `pos` broadcasts is dropped. |
| `ctor()` |  |

## class PongFrame

The answer to an application-level ping.

No public members.

## class PosBroadcastFrame

Coalesced positions once per tick; includes the receiver's own entry.

| Member | Summary |
| --- | --- |
| `Peers : IReadOnlyList<Peer> get` | Only the peers that moved this tick, including the receiver's own entry. |
| `Zone : String get` | The zone these positions belong to. |

## struct ProtocolErrorEvent

A frame the SDK could not read.

| Member | Summary |
| --- | --- |
| `Message : String get` | A refusal code and an offset, or a capped frame type. Never quotes the frame. |
| `ctor(String)` |  |

## struct ReconnectingEvent

A reconnect is scheduled.

| Member | Summary |
| --- | --- |
| `Attempt : Int32 get` | Which consecutive retry this is, counting from one. |
| `DelayMillis : Double get` | How long the client will wait before opening the next socket. |
| `ctor(Int32, Double)` |  |

## class SayBroadcastFrame

Chat mirrored to its scope.

| Member | Summary |
| --- | --- |
| `From : String get` | The sender's `userId` . |
| `Scope : String get` | The wire scope. Unknown values stay readable rather than being dropped. |
| `Text : String get` | The message. Never log it: it is whatever the peer typed. |
| `To : String get` | The addressee, on a whisper. |

## enum SayScope

Where a `say` or `event` is routed.

- `Party` — The sender's party.
- `User` — One user, named by the `to` argument, across zones.
- `Zone` — Everyone in the sender's current zone.

## static class SayScopes

Wire spellings for `SayScope` .

| Member | Summary |
| --- | --- |
| `Party : String` | The sender's party. |
| `ToWire(SayScope) : String` | The wire spelling of a scope. |
| `TryParse(String, SayScope&) : Boolean` | Reads a wire scope back, answering false for one this SDK does not know. |
| `User : String` | One user, named by `to` , across zones. |
| `Zone : String` | Everyone in the sender's current zone. |

## class SnapshotFrame

Everyone already in the zone the client just entered.

| Member | Summary |
| --- | --- |
| `Peers : IReadOnlyList<Peer> get` | Every retained peer in the zone, including the receiver's own entry. |
| `Zone : String get` | The zone this snapshot describes. It replaces whatever the peer map held. |

## struct SocketEvent

One thing a socket reported, carried from the receive thread to the pump.

| Member | Summary |
| --- | --- |
| `BinaryMessage(IWebSocket) : SocketEvent` | A binary frame arrived, which is a protocol error on this gateway. |
| `Closed(IWebSocket, Int32, String) : SocketEvent` | The socket closed. Report this exactly once, with the locally requested code when the close was local. |
| `Code : Int32 get` | The close code, on a close. |
| `IsText : Boolean get` | Whether the message was a text frame. A binary one is a protocol error. |
| `Kind : SocketEventKind get` | What happened. |
| `Message(IWebSocket, String) : SocketEvent` | A text frame arrived. |
| `Opened(IWebSocket, String) : SocketEvent` | The handshake completed; `protocol` is the subprotocol the server selected. |
| `Protocol : String get` | The subprotocol the server selected. Empty when it selected none. |
| `Reason : String get` | The close reason, on a close. Never log it: the peer may have chosen it. |
| `Source : IWebSocket get` | The socket that reported this. The state machine compares it against the socket it currently holds and drops anything from one it has replaced. |
| `Text : String get` | The message, for a text frame. |

## enum SocketEventKind

What a socket reported.

- `Closed`
- `Message`
- `Opened`

## struct StoppedEvent

The connection ended for good; no reconnect will follow.

| Member | Summary |
| --- | --- |
| `Code : Int32 get` | The close code that ended it. |
| `Kind : CloseDispositionKind get` | Why the connection will not be retried. |
| `Reason : String get` | Why it stopped, as the SDK classified it. |
| `ctor(CloseDispositionKind, String, Int32)` |  |

## class SystemClock

The default clock, backed by a monotonic `Stopwatch` .

| Member | Summary |
| --- | --- |
| `Instance : IClock` | The shared monotonic clock every client uses unless a test injects its own. |
| `NowMillis : Double get` |  |

## class UnknownServerFrame

A frame this SDK does not model, delivered so a game can still read it.

No public members.

## class WebSocketCreateContext

Everything a factory needs to build a socket.

| Member | Summary |
| --- | --- |
| `Sink : IWebSocketEventSink get` | Where the socket posts what it observes. |
| `SubProtocols : IReadOnlyList<String> get` | Always `["bearer", token]` — so this carries the raw channel JWT . Hand it to the socket and nowhere else: never log it, never put it in a URL, and never persist it. The token appears in no other argument of this API. |
| `Url : String get` | The full handshake URL, query string included. |
| `ctor(String, IReadOnlyList<String>, IWebSocketEventSink)` |  |

## static class WebSocketTransport

The default `IWebSocketFactory` .

| Member | Summary |
| --- | --- |
| `Default : IWebSocketFactory get` | A factory over `ClientWebSocket` . |
