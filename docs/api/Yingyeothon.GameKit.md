# Yingyeothon.GameKit

<!-- Generated from the assembly by tests/Yingyeothon.PublicApi.Tests.
     Do not edit by hand: the test rewrites it and CI compares it. -->

Every public type and member, with its documentation comment — the same text
your IDE shows. For what the package is *for*, read
[the guide](../README.md) and
[`packages/com.yingyeothon.game-kit/README.md`](../../packages/com.yingyeothon.game-kit/README.md).

## Contents

- [`GameKit`](#static-class-gamekit)
- [`GameKitOptions`](#class-gamekitoptions)
- [`IGameKit`](#interface-igamekit)
- [`IKitBoard`](#interface-ikitboard)
- [`IKitContent`](#interface-ikitcontent)
- [`IKitFriends`](#interface-ikitfriends)
- [`IKitLoginAdapter`](#interface-ikitloginadapter)
- [`IKitMail`](#interface-ikitmail)
- [`IKitMatchmaking`](#interface-ikitmatchmaking)
- [`IKitPush`](#interface-ikitpush)
- [`IKitPushTopics`](#interface-ikitpushtopics)
- [`IKitQueueStorage`](#interface-ikitqueuestorage)
- [`IKitRoom`](#interface-ikitroom)
- [`IKitSave`](#interface-ikitsave)
- [`IKitSession`](#interface-ikitsession)
- [`IKitTokenStorage`](#interface-ikittokenstorage)
- [`IKitTurnGame`](#interface-ikitturngame)
- [`IKitTurns`](#interface-ikitturns)
- [`KitException`](#class-kitexception)
- [`KitJson`](#static-class-kitjson)
- [`KitMailSendOptions`](#class-kitmailsendoptions)
- [`KitRoomEvent`](#class-kitroomevent)
- [`KitSaveOptions`](#class-kitsaveoptions)
- [`KitTurnsOptions`](#class-kitturnsoptions)

## static class GameKit

Factory for a kit with engine-free modules.

| Member | Summary |
| --- | --- |
| `Create(GameKitOptions) : IGameKit` | Creates a kit without opening a socket or storing a credential. |

## class GameKitOptions

Options copied by the kit.

| Member | Summary |
| --- | --- |
| `AuthTransport : IAuthTransport get set` | Auth transport. |
| `Clock : IClock get set` | Monotonic clock. |
| `Config : JsonValue get set` | Service kit-config JSON, retaining its service field names. |
| `ContentPollMillis : Double get set` | Content polling interval, minimum one second. |
| `LoginAdapter : IKitLoginAdapter get set` | System browser adapter. |
| `MatchCallback : Boolean get set` | Whether the live match channel has a callback; never inferred from result. |
| `MatchPollMillis : Double get set` | Deferred ticket polling interval, minimum one second. |
| `MergePolicies : IReadOnlyDictionary<String, Func<JsonValue, JsonValue, JsonValue>> get set` | Registered save-name policies used after queue restoration. |
| `OfflineQueue : IKitQueueStorage get set` | Durable conditional-save queue is opt-in. |
| `Provider : String get set` | Provider override; default kit-config auth.provider, then github. |
| `PushTopics : IKitPushTopics get set` | Optional Android topic SDK seam. |
| `RedirectUrl : Uri get set` | Allowed login return URL. |
| `SocialPollMillis : Double get set` | Mail and friends polling interval, minimum one second. |
| `TokenStorage : IKitTokenStorage get set` | Token storage is opt-in. |
| `Transport : IHttpTransport get set` | State HTTP transport; use UnityWebRequestTransport for WebGL. |
| `WebSocketFactory : IWebSocketFactory get set` | Gateway and match socket factory. |
| `ctor()` |  |

## interface IGameKit

Purpose-shaped game modules. The kit owns and pumps its lower-level clients.

| Member | Summary |
| --- | --- |
| `Board(String) : IKitBoard` | Board by configured alias. |
| `Content : IKitContent get` | Published content. |
| `Friends : IKitFriends get` | Relations and gateway presence. |
| `Mail : IKitMail get` | Player inbox. |
| `Matchmaking : IKitMatchmaking get` | Live and deferred matchmaking. |
| `Push : IKitPush get` | Push registration and messages. |
| `Room : IKitRoom get` | Host room and routed events. |
| `Save : IKitSave get` | Conditional player persistence. |
| `Session : IKitSession get` | Session and platform clock. |
| `SetForeground(Boolean) : Void` | Starts or stops background polling; resume schedules immediate refresh. |
| `ShutdownAsync() : Task` | Disposes and completes after queued work and cleanup settle; keep polling while awaiting. |
| `Turns : IKitTurns get` | Deterministic turn games. |

## interface IKitBoard

Purpose-shaped leaderboard.

| Member | Summary |
| --- | --- |
| `AroundAsync(String, Int32, CancellationToken?) : Task<JsonValue>` | Neighborhood around own rank; span must be zero to 49. |
| `InfoAsync(CancellationToken?) : Task<JsonValue>` | Board metadata. |
| `MeAsync(String, CancellationToken?) : Task<JsonValue>` | Own rank or null. |
| `SubmitAsync(PlatformScoreOptions, CancellationToken?) : Task<JsonValue>` | Stored scores plus each own period rank. |
| `TopAsync(PlatformBoardPageOptions, CancellationToken?) : Task<JsonValue>` | Leaderboard page. |

## interface IKitContent

Published content cache with foreground polling.

| Member | Summary |
| --- | --- |
| `GetAsync(String, CancellationToken?) : Task<JsonValue>` | Cached read with background refresh, or an awaited cold read. |
| `ListAsync(String?, CancellationToken?) : Task<IReadOnlyList<KvListEntry>>` | Lists all pages of published entries. |
| `Subscribe(String, Action<JsonValue>) : IDisposable` | Observes version changes; completion and handlers run inside Poll. |

## interface IKitFriends

Social profiles, relations and presence.

| Member | Summary |
| --- | --- |
| `AcceptAsync(String, CancellationToken?) : Task` | Accepts a relation request. |
| `BlockAsync(String, CancellationToken?) : Task` | Blocks a player. |
| `DeclineAsync(String, CancellationToken?) : Task` | Declines a relation request. |
| `InviteAsync(String, CancellationToken?) : Task` | Invites an online friend to the current room. |
| `ListAsync(CancellationToken?) : Task<JsonValue>` | Friends joined with gateway online state. |
| `ProfileAsync(CancellationToken?) : Task<JsonValue>` | Own profile. |
| `ProfilesAsync(IReadOnlyList<String>, CancellationToken?) : Task<JsonValue>` | Batch profiles of at most fifty players. |
| `RemoveAsync(String, CancellationToken?) : Task` | Removes a relation. |
| `RequestAsync(String, CancellationToken?) : Task` | Requests a relation. |
| `RequestsAsync(CancellationToken?) : Task<JsonValue>` | Incoming and outgoing requests. |
| `SetProfileAsync(PlatformProfileOptions, CancellationToken?) : Task<JsonValue>` | Edits own profile. |
| `UnblockAsync(String, CancellationToken?) : Task` | Unblocks a player. |
| `event Requested : Action<JsonValue>` | A new incoming request observed by foreground polling. |

## interface IKitLoginAdapter

System browser/redirect adapter; storage and browser UI remain host-owned.

| Member | Summary |
| --- | --- |
| `OpenAsync(Uri, CancellationToken) : Task<Uri>` | Opens the URL and waits for a redirect; cancellation must close the flow. |

## interface IKitMail

Player inbox with authenticated writer stamps.

| Member | Summary |
| --- | --- |
| `ClaimAsync(String, CancellationToken?) : Task<KvEntry>` | Conditionally deletes and returns a letter; a lost claim reports already_claimed. |
| `ListAsync(CancellationToken?) : Task<IReadOnlyList<KvListEntry>>` | All letters, ordered by write timestamp descending. |
| `ReadAsync(String, CancellationToken?) : Task<KvEntry>` | One letter, or null. |
| `RemoveAsync(String, CancellationToken?) : Task` | Removes a letter. |
| `SendAsync(KitMailSendOptions, CancellationToken?) : Task` | Sends create-only player mail using an authenticated sender prefix. |
| `event Arrived : Action<KvListEntry>` | Raised once per observed letter id in this session. |

## interface IKitMatchmaking

Live and deferred matchmaking policy.

| Member | Summary |
| --- | --- |
| `AcceptAsync(CancellationToken?) : Task<MatchTicket>` | Accepts a proposal. |
| `CancelAsync(CancellationToken?) : Task` | Cancels live find or deletes a deferred ticket. |
| `DeclineAsync(CancellationToken?) : Task<MatchTicket>` | Declines a proposal. |
| `FindAsync(CancellationToken?) : Task<MatchResult>` | Live find plus callback-less room formation; deferred mode uses SubmitAsync instead. |
| `StatusAsync(CancellationToken?) : Task<MatchTicket>` | Reads authoritative deferred status. |
| `SubmitAsync(CancellationToken?) : Task<MatchTicket>` | Submits a deferred ticket. |
| `event StateChanged : Action<MatchTicket>` | Observed deferred state transition. |

## interface IKitPush

Push lifecycle and match wakeups. Topic messages grant no entitlement.

| Member | Summary |
| --- | --- |
| `Receive(IReadOnlyDictionary<String, String>) : Void` | Queues a Firebase data message or notification-tap payload for Poll. |
| `RegisterAsync(PushRegisterOptions, CancellationToken?) : Task` | Registers a device and subscribes after a successful registration. |
| `UnregisterAsync(CancellationToken?) : Task` | Deletes the last registered token and unsubscribes. |
| `event MessageReceived : Action<IReadOnlyDictionary<String, String>>` | Campaign, targeted or broadcast payload. |

## interface IKitPushTopics

Topic SDK seam owned by an Android host.

| Member | Summary |
| --- | --- |
| `SubscribeAsync(String, CancellationToken) : Task` | Subscribes to the exact public topic from kit config. |
| `UnsubscribeAsync(String, CancellationToken) : Task` | Unsubscribes on sign-out. |

## interface IKitQueueStorage

Optional durable queue, isolated by endpoint, channel, collection and player.

| Member | Summary |
| --- | --- |
| `LoadAsync(String, CancellationToken) : Task<IReadOnlyList<JsonValue>>` | Loads the ordered JSON records for one namespace. |
| `StoreAsync(String, IReadOnlyList<JsonValue>, CancellationToken) : Task` | Atomically and durably replaces the ordered records. |

## interface IKitRoom

Private room mirroring the gateway party.

| Member | Summary |
| --- | --- |
| `AcceptAsync(String, CancellationToken?) : Task` | Accepts an invitation. |
| `ConnectAsync(CancellationToken?) : Task` | Connects the lobby without creating a party. |
| `CreateAsync(CancellationToken?) : Task` | Connects the lobby and creates a room, completing on its roster. |
| `DeclineAsync(String, CancellationToken?) : Task` | Declines an invitation. |
| `Host : String get` | Current leader id. |
| `Id : String get` | Current room id. |
| `InviteAsync(String, CancellationToken?) : Task` | Invites a player. |
| `IsHost : Boolean get` | Whether this player is leader. |
| `LastKnown : JsonValue get` | Latest accepted host snapshot. |
| `LeaveAsync(CancellationToken?) : Task` | Leaves the current room. |
| `Members : IReadOnlyList<PartyMember> get` | Latest members. |
| `PublishAsync(JsonValue, CancellationToken?) : Task` | Publishes a host snapshot; larger states use game-defined deltas. |
| `SendAsync(String, JsonValue, CancellationToken?) : Task` | Routes a user event with a bounded kit envelope. |
| `WhisperAsync(String, String, JsonValue, CancellationToken?) : Task` | Routes a private user event. |
| `event BecomeHost : Action<JsonValue>` | New host hook with the previous host's last accepted snapshot. |
| `event EventReceived : Action<KitRoomEvent>` | User-defined events, excluding reserved kit names. |
| `event HostChanged : Action<String>` | Host changed on the pump. |
| `event Invited : Action<PartyInviteFrame>` | Incoming invite; accept or decline through this room. |
| `event MemberJoined : Action<PartyMember>` | A member joined. |
| `event MemberLeft : Action<PartyMember>` | A member left. |
| `event MemberOnline : Action<PartyMember>` | A member's online state changed. |
| `event Refused : Action<KitException>` | Asynchronous gateway command refusal, including send-only event operations. |
| `event StateReceived : Action<JsonValue>` | Accepted host state. |

## interface IKitSave

Player save operations; counters and claims are never queued offline.

| Member | Summary |
| --- | --- |
| `AddAsync(String, Int64, KvIncrOptions?, CancellationToken?) : Task<Int64>` | Server-atomic integer increment with per-call bounds. |
| `ClaimOnceAsync(String, Int32, CancellationToken?) : Task<Boolean>` | Claims once per server TTL window. |
| `FlushAsync(CancellationToken?) : Task` | Replays durable writes in order, stopping at unresolved conflict. |
| `LoadAsync(String, CancellationToken?) : Task<JsonValue>` | Loads and remembers the conditional-write version. |
| `PendingCount : Int32 get` | Loaded queue size. |
| `RemoveAsync(String, CancellationToken?) : Task` | Conditionally removes a known revision. |
| `StoreAsync(String, JsonValue, KitSaveOptions?, CancellationToken?) : Task` | Conditionally stores, merging on conflict only when requested. |
| `UpdateAsync(String, Func<JsonValue, JsonValue>, KitSaveOptions?, CancellationToken?) : Task` | Loads, applies a local function and conditionally stores. |

## interface IKitSession

Identity and platform time; token adoption reads claims, never verifies a signature.

| Member | Summary |
| --- | --- |
| `ExpiresAt : Nullable<Int64> get` | Token expiry in epoch seconds. |
| `FromTokenAsync(String, CancellationToken?) : Task` | Adopts a JWT and synchronizes the platform clock. |
| `Now() : Double` | Platform epoch milliseconds; unavailable before first successful synchronization. |
| `RestoreAsync(CancellationToken?) : Task` | Restores opted-in token storage. |
| `SignInAsync(CancellationToken?) : Task` | Opens the injected login flow and adopts its validated redirect. |
| `SignOutAsync(CancellationToken?) : Task` | Stops all modules and clears opted-in token storage. |
| `Token : String get` | Player JWT for wire clients. Never log it. |
| `UserId : String get` | Authenticated player identity claimed by the adopted token. |
| `event Expiring : Action` | Raised once per adopted token at expiry minus ten minutes. |
| `event SignedOut : Action` | Raised once when the active session ends. |

## interface IKitTokenStorage

Optional secure token persistence owned by the game.

| Member | Summary |
| --- | --- |
| `ClearAsync(CancellationToken) : Task` | Clears the opted-in storage. |
| `LoadAsync(CancellationToken) : Task<String>` | Loads a stored player token, or null. |
| `StoreAsync(String, CancellationToken) : Task` | Durably stores a token. Never log it. |

## interface IKitTurnGame

Deterministic game controlled by the room host.

| Member | Summary |
| --- | --- |
| `Elapsed : IReadOnlyDictionary<String, Double> get` | Informational per-player elapsed milliseconds. |
| `FinishAsync(JsonValue, CancellationToken?) : Task` | Host announces a result; each player saves its own result. |
| `Frozen : Boolean get` | Whether a mismatch froze this game. |
| `GameId : String get` | Random game id announced by the host. |
| `Index : Int32 get` | Number of applied turns. |
| `ResyncAsync(CancellationToken?) : Task` | Requests an atomic verified log resynchronization. |
| `State : JsonValue get` | Current state after validated turns. |
| `SubmitAsync(JsonValue, CancellationToken?) : Task` | Submits on this player's turn. |
| `event Mismatch : Action` | A mismatch froze the game. |
| `event PersistenceFailed : Action<KitException>` | Retryable persistence failure, reported separately from mismatch. |
| `event Turn : Action<JsonValue>` | A validated turn changed State. |

## interface IKitTurns

Turn module; every peer supplies identical initial state and reducer.

| Member | Summary |
| --- | --- |
| `StartAsync(KitTurnsOptions, CancellationToken?) : Task<IKitTurnGame>` | Prepares a game; the host announces it and peers wait for its announcement. |

## class KitException

Unified safe module refusal.

| Member | Summary |
| --- | --- |
| `Code : String get` | Shared cross-language error code. |
| `Module : String get` | Failing module. |
| `Retryable : Boolean get` | Whether retry may help. |
| `ctor(String, String)` | Creates a module error without retaining untrusted exception text. |

## static class KitJson

Canonical JSON and SHA-256 for the shared turns contract.

| Member | Summary |
| --- | --- |
| `Canonicalize(JsonValue) : String` | RFC 8785 JSON for immutable codec values; invalid Unicode is refused. |
| `Hash(JsonValue) : String` | Lowercase hex SHA-256 of UTF-8 canonical JSON. |

## class KitMailSendOptions

Player mail input.

| Member | Summary |
| --- | --- |
| `To : String get set` | Recipient owner id. |
| `Ttl : Nullable<Int32> get set` | Optional TTL in seconds. |
| `Value : JsonValue get set` | JSON letter. |
| `ctor()` |  |

## class KitRoomEvent

One routed game event with its authenticated gateway sender.

| Member | Summary |
| --- | --- |
| `Data : JsonValue get` | Event data. |
| `From : String get` | Authenticated user id. |
| `Name : String get` | User event name. |
| `ctor(String, String, JsonValue)` | Creates a routed event. |

## class KitSaveOptions

Conditional save options.

| Member | Summary |
| --- | --- |
| `Merge : Func<JsonValue, JsonValue, JsonValue> get set` | Local/remote merge for this request; registered policies are used after restoration. |
| `Ttl : Nullable<Int32> get set` | Optional TTL in seconds. |
| `ctor()` |  |

## class KitTurnsOptions

Deterministic turn game input.

| Member | Summary |
| --- | --- |
| `Apply : Func<JsonValue, JsonValue, String, JsonValue> get set` | Pure state reducer; throws to reject a move. |
| `Initial : JsonValue get set` | Initial JSON state. |
| `LogTtlSeconds : Int32 get set` | Log TTL seconds, default seven days. |
| `MaxLogBytes : Int32 get set` | Maximum assembled log bytes, default 1 MiB. |
| `Persist : Boolean get set` | Persist verified logs into the player's save namespace. |
| `Players : IReadOnlyList<String> get set` | Player order, default room members. |
| `ctor()` |  |
