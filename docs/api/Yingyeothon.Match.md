# Yingyeothon.Match

<!-- Generated from the assembly by tests/Yingyeothon.PublicApi.Tests.
     Do not edit by hand: the test rewrites it and CI compares it. -->

Every public type and member, with its documentation comment — the same text
your IDE shows. For what the package is *for*, read
[the guide](../README.md) and
[`packages/com.yingyeothon.match-client/README.md`](../../packages/com.yingyeothon.match-client/README.md).

## Contents

- [`DeferredMatchClient`](#static-class-deferredmatchclient)
- [`DeferredMatchClientOptions`](#class-deferredmatchclientoptions)
- [`IDeferredMatchClient`](#interface-ideferredmatchclient)
- [`ILiveMatchClient`](#interface-ilivematchclient)
- [`LiveMatchClient`](#static-class-livematchclient)
- [`LiveMatchClientOptions`](#class-livematchclientoptions)
- [`MatchResult`](#class-matchresult)
- [`MatchTicket`](#class-matchticket)
- [`MatchTicketState`](#enum-matchticketstate)

## static class DeferredMatchClient

Deferred client factory.

| Member | Summary |
| --- | --- |
| `Create(DeferredMatchClientOptions) : IDeferredMatchClient` | Creates a client without automatic retry or polling timers. |

## class DeferredMatchClientOptions

Deferred HTTP options.

| Member | Summary |
| --- | --- |
| `ChannelId : String get set` | Match channel id. |
| `Http : PlatformClientOptions get set` | Match HTTP API base and player token. |
| `ctor()` |  |

## interface IDeferredMatchClient

Ticket wire API. Polling and retries belong to the kit.

| Member | Summary |
| --- | --- |
| `AcceptAsync(CancellationToken?) : Task<MatchTicket>` | Accepts a proposal; confirmation follows asynchronously. |
| `CancelAsync(CancellationToken?) : Task` | Deletes a ticket, declining an open proposal. |
| `DeclineAsync(CancellationToken?) : Task<MatchTicket>` | Declines a proposal. |
| `StatusAsync(CancellationToken?) : Task<MatchTicket>` | Reads the caller's ticket, null on 404. |
| `SubmitAsync(CancellationToken?) : Task<MatchTicket>` | Submits a ticket. |

## interface ILiveMatchClient

Live matchmaking socket, pumped on the owner thread.

| Member | Summary |
| --- | --- |
| `Cancel() : Void` | Queues cancellation; the task settles at the next Poll. |
| `FindAsync(CancellationToken?) : Task<MatchResult>` | Starts or observes the current find. |

## static class LiveMatchClient

Live match socket factory.

| Member | Summary |
| --- | --- |
| `Create(LiveMatchClientOptions) : ILiveMatchClient` | Creates a client; no socket starts until FindAsync. |

## class LiveMatchClientOptions

Live socket configuration.

| Member | Summary |
| --- | --- |
| `ChannelId : String get set` | Match channel id. |
| `Clock : IClock get set` | Monotonic clock. |
| `TimeoutMillis : Double get set` | Maximum find duration in milliseconds, default 120000. |
| `Token : String get set` | Player JWT, passed only in bearer subprotocols. |
| `Url : String get set` | Match WebSocket origin without query, fragment or userinfo. |
| `WebSocketFactory : IWebSocketFactory get set` | Socket factory; required for WebGL. |
| `ctor()` |  |

## class MatchResult

Members and callback result shared by live and deferred confirmation.

| Member | Summary |
| --- | --- |
| `MatchId : String get` | Match identifier. |
| `Members : IReadOnlyList<String> get` | Authenticated member user ids in ticket order. |
| `Partial : Boolean get` | Whether timeout formed a short group. |
| `Raw : JsonValue get` | Original matched or confirmed object. |
| `Result : JsonValue get` | Callback JSON, including JSON null. |
| `ctor(JsonValue)` | Parses required fields and retains the original JSON including future fields. |

## class MatchTicket

Validated deferred ticket, preserving future fields.

| Member | Summary |
| --- | --- |
| `Raw : JsonValue get` | Full ticket JSON. |
| `Result : MatchResult get` | Confirmed match only. |
| `State : MatchTicketState get` | Current ticket state. |
| `ctor(JsonValue)` | Reads a ticket without inventing a terminal result. |

## enum MatchTicketState

Deferred lifecycle states.

- `Confirmed`
- `Declined`
- `Expired`
- `Failed`
- `Proposed`
- `Waiting`
