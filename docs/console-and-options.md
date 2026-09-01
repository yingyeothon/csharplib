# Console and options

Everything this SDK needs is a value the yyt console already holds. This page is the
mapping, in both directions: what you copy into an options object, and what the channel
decides for you and announces in `hello`.

## The four values

| Option | Console field | Example |
| --- | --- | --- |
| `Url` | the lobby or `q` channel's `wsUrl`, **origin only** | `wss://gw.yyt.life` |
| `ChannelId` | that channel's `id` | `lobby_0123…` / `q_0123…` |
| `Token` | a channel JWT from the linked **auth** channel — see [Authentication](authentication.md) | a JWT |
| `GameId` | your own entry API or the match service — see [Dungeon](dungeon.md) | `g_0123…` |

Getting a `Token` needs two more values that are not options on any client: the auth
service's base URL and the **auth channel id** the lobby channel is linked to. Both are
on the auth channel in the console.

The console prints `wsUrl` as `wss://gw.yyt.life/?channel=lobby_…`. Pass the origin and
let `GatewayUrl.Build` add the query: a `channel` already on the URL is **replaced**
rather than duplicated, because the gateway reads only one and a duplicate would
silently pick the wrong channel.

Both socket kinds use the **same** token; see
[Authentication § Lifetime](authentication.md#lifetime-expiry-and-reconnect).

## What the channel decides, not your build

A lobby channel's configuration reaches you in `hello`. Change it in the console and
every client picks it up on its next connect — no rebuild, and for the map, no CDN
invalidation.

| Console setting (`yyt channels create --kind lobby …`) | Reaches you as | Default |
| --- | --- | --- |
| `--zone` | `Hello.Zone` — the zone to start in | `lobby` |
| `--map-url` | `Hello.MapUrl`, fetched by `MapAsync()` | none |
| `--flush-interval-ms` | `Hello.Tick` — how often positions are broadcast | `200` (range 50–2000) |
| `--cap-pos` | `Capabilities.Pos` | `true` |
| `--cap-say <scope>` (repeatable) | `Capabilities.Say`, a subset of `zone`, `party`, `user` | `["zone"]` |
| `--cap-party` | `Capabilities.Party` | `true` |
| `--cap-event` | `Capabilities.Event` | `true` |
| `--cap-debug` | `Capabilities.Debug` | `false` |
| `--party-size-max` | not in `hello`, but on every roster as `PartyFrame.Max` | `4` |
| `--max-move-delta` | not in `hello`; surfaces as the `move_too_far` refusal | `4` |
| `--rate-limit` | not in `hello`; surfaces as the `rate_limited` refusal | `30`/s |

What `hello` carries beyond the table above is in [Lobby](lobby.md#hello-and-capabilities).

**A null capability means unrestricted, not disabled** — only an explicit `false` turns
one off. [Lobby](lobby.md#hello-and-capabilities) has why the SDK refuses to be stricter
than the gateway.

`Capabilities.Debug` has no gateway commands behind it yet. It is a flag to read, not
one to act on: nothing this SDK sends is gated on it, and it is modelled only so a
channel that starts using it does not arrive as an unknown field.

## Every option

### `GatewayClientOptions` — shared by both clients

| Property | Type | Default | What it does |
| --- | --- | --- | --- |
| `Url` | `string` | **required** | Gateway origin. The SDK appends `?channel=…[&gameId=…]`. |
| `ChannelId` | `string` | **required** | The channel to join. |
| `Token` | `string` | **required** | The channel JWT. Travels in the WebSocket subprotocol list as `["bearer", token]`, never in the URL where it would land in access logs, and never in a log line. |
| `WebSocketFactory` | `IWebSocketFactory?` | `WebSocketTransport.Default` | Required on Unity WebGL, where `ClientWebSocket` does not exist. See [Unity](unity.md). |
| `Backoff` | `BackoffOptions?` | the defaults below | Reconnect schedule. |
| `MaxHandshakeFailures` | `int` | `5` | Consecutive closes-before-open that end the session instead of retrying a dead token forever. See [Connection lifecycle](connection-lifecycle.md#handshake-failures). |
| `Logger` | `ILogger?` | `NullLogger.Instance` | See [`Yingyeothon.Logger`](../packages/com.yingyeothon.logger/README.md). |
| `Clock` | `IClock?` | `SystemClock.Instance` | A monotonic clock. Injected by tests; a game never sets it. |

### `GatewayLobbyClientOptions` adds

| Property | Type | Default | What it does |
| --- | --- | --- | --- |
| `HttpFetcher` | `IHttpFetcher?` | `HttpFetcher.Default` | Used by `MapAsync()`. Required on WebGL. The default bounds the request at 30 s, 16 MB and 5 redirects, because `mapUrl` comes off the wire. |
| `HelloTimeoutMillis` | `double` | `10000` | How long one socket may take to say `hello`. Exceeding it closes that socket and reconnects; it does not fail `ConnectAsync`. |

### `GatewayGameClientOptions` adds

| Property | Type | Default | What it does |
| --- | --- | --- | --- |
| `GameId` | `string` | `""` | The run to join. The caller must be listed in that run's start event, or the handshake is refused. |

### `BackoffOptions`

| Property | Type | Default | What it does |
| --- | --- | --- | --- |
| `InitialMs` | `double` | `500` | Delay before the first retry. |
| `MaxMs` | `double` | `15000` | Upper bound on any delay. |
| `Factor` | `double` | `2` | Multiplier per attempt. |
| `Jitter` | `double` | `0.2` | Fraction randomised on both sides, so a gateway restart does not stampede. |
| `MaxAttempts` | `int?` | `null` | Unbounded by default; exhausting it ends in `Stopped`. |
| `Random` | `Func<double>?` | system random | Source in `[0, 1)`. A test pins the jitter with it. |

### `PeerMapOptions`

| Property | Type | Default | What it does |
| --- | --- | --- | --- |
| `SelfUserId` | `string` | `""` | The receiver's own `userId`; its entries are dropped from every frame. |

The lobby client builds its own peer map and sets this from `hello.UserId`, so you only
construct one directly if you are reducing frames yourself. See [Lobby](lobby.md).

The factory snapshots the values a connection is built from — URL, channel, token,
backoff, logger, clock — so changing them on the options object afterwards has no
effect. To connect with a fresh token, create a new client. (`HttpFetcher` is the one
read lazily, on the first `MapAsync`; do not rely on that.)
