# Yingyeothon.Platform

<!-- Generated from the assembly by tests/Yingyeothon.PublicApi.Tests.
     Do not edit by hand: the test rewrites it and CI compares it. -->

Every public type and member, with its documentation comment — the same text
your IDE shows. For what the package is *for*, read
[the guide](../README.md) and
[`packages/com.yingyeothon.platform-client/README.md`](../../packages/com.yingyeothon.platform-client/README.md).

## Contents

- [`IPlatformBoard`](#interface-iplatformboard)
- [`IPlatformClient`](#interface-iplatformclient)
- [`IPlatformHttpClient`](#interface-iplatformhttpclient)
- [`IPlatformSocial`](#interface-iplatformsocial)
- [`IPollable`](#interface-ipollable)
- [`PlatformBoardPageOptions`](#class-platformboardpageoptions)
- [`PlatformClient`](#static-class-platformclient)
- [`PlatformClientOptions`](#class-platformclientoptions)
- [`PlatformException`](#class-platformexception)
- [`PlatformHttpClient`](#static-class-platformhttpclient)
- [`PlatformPresenceOptions`](#class-platformpresenceoptions)
- [`PlatformProfileOptions`](#class-platformprofileoptions)
- [`PlatformRequestOptions`](#class-platformrequestoptions)
- [`PlatformScoreOptions`](#class-platformscoreoptions)

## interface IPlatformBoard

Leaderboard wire operations. JSON results preserve future fields.

| Member | Summary |
| --- | --- |
| `InfoAsync(CancellationToken?) : Task<JsonValue>` | Board configuration and configured periods. |
| `MeAsync(String, CancellationToken?) : Task<JsonValue>` | Own score and rank, or null when absent. |
| `SubmitAsync(PlatformScoreOptions, CancellationToken?) : Task<JsonValue>` | Submits an integer score; returns submitted and stored periods, not ranks. |
| `TopAsync(PlatformBoardPageOptions, CancellationToken?) : Task<JsonValue>` | Reads a page of ranked entries. |

## interface IPlatformClient

Time and authenticated platform routes. Every pending task requires Poll.

| Member | Summary |
| --- | --- |
| `Board(String) : IPlatformBoard` | Addresses a board without making a request. |
| `PresenceAsync(PlatformPresenceOptions, CancellationToken?) : Task<JsonValue>` | Gateway presence batch, at most fifty player ids. |
| `Social : IPlatformSocial get` | Player profiles and relations. |
| `TimeAsync(CancellationToken?) : Task<Int64>` | Platform UTC time, epoch milliseconds, without a bearer. |

## interface IPlatformHttpClient

Bounded JSON requests shared by platform, match and push clients.

| Member | Summary |
| --- | --- |
| `RequestAsync(PlatformRequestOptions, CancellationToken?) : Task<JsonValue>` | Returns parsed JSON or null for 204. Refusals are PlatformException; completion requires Poll. |
| `RequestAsync(PlatformRequestOptions, Func<JsonValue, T>, CancellationToken?) : Task<T>` | Transforms a response inside Poll before settling the returned task. |

## interface IPlatformSocial

Authenticated social routes; returned JSON uses the service field names.

| Member | Summary |
| --- | --- |
| `AcceptAsync(String, CancellationToken?) : Task` | Accepts an incoming request. |
| `BlockAsync(String, CancellationToken?) : Task` | Blocks a player. |
| `DeclineAsync(String, CancellationToken?) : Task` | Declines an incoming request. |
| `FriendsAsync(CancellationToken?) : Task<JsonValue>` | Friend relations. |
| `ProfileAsync(CancellationToken?) : Task<JsonValue>` | Own profile, or null when absent. |
| `ProfilesAsync(IReadOnlyList<String>, CancellationToken?) : Task<JsonValue>` | Profiles of one to fifty players. |
| `RemoveAsync(String, CancellationToken?) : Task` | Removes a relation. |
| `RequestAsync(String, CancellationToken?) : Task<JsonValue>` | Requests a relation. |
| `RequestsAsync(CancellationToken?) : Task<JsonValue>` | Incoming and outgoing requests. |
| `SetProfileAsync(PlatformProfileOptions, CancellationToken?) : Task<JsonValue>` | Sets the current player's profile. |
| `UnblockAsync(String, CancellationToken?) : Task` | Unblocks a player. |

## interface IPollable

A resource driven on its owner's thread. Poll is never reentrant.

| Member | Summary |
| --- | --- |
| `Poll() : Void` | Applies queued I/O results and settles tasks on this thread. |

## class PlatformBoardPageOptions

Leaderboard page input.

| Member | Summary |
| --- | --- |
| `Limit : Int32 get set` | One to one hundred entries. |
| `Offset : Int32 get set` | Zero to one thousand entries skipped. |
| `Period : String get set` | alltime, daily or weekly. |
| `ctor()` |  |

## static class PlatformClient

Platform client factory.

| Member | Summary |
| --- | --- |
| `Create(PlatformClientOptions) : IPlatformClient` | Creates time, board and social clients over a shared pumped requester. |

## class PlatformClientOptions

Transport and origin configuration copied at creation.

| Member | Summary |
| --- | --- |
| `BaseUrl : String get set` | HTTP origin or base path, without credentials, query or fragment. |
| `Logger : ILogger get set` | Safe structured logger, default null logger. |
| `NowMillis : Func<Double> get set` | Injected monotonic milliseconds, default Stopwatch. |
| `Timeout : Nullable<TimeSpan> get set` | Per-request deadline, default 15 seconds. |
| `Token : String get set` | Player JWT; required only for authenticated requests. Never log it. |
| `Transport : IHttpTransport get set` | HTTP transport. Use the KV Unity transport on WebGL; it receives credentials. |
| `ctor()` |  |

## class PlatformException

Safe wire refusal; no body, URL, token or raw transport exception is retained.

| Member | Summary |
| --- | --- |
| `Code : String get` | Bounded machine code. |
| `Reason : String get` | Bounded machine reason, when present. |
| `RetryAfterMillis : Nullable<Double> get` | Cooldown in milliseconds. |
| `Status : Int32 get` | HTTP status, or zero without a reply. |
| `ctor(Int32, String, String?, Nullable<Double>?)` | Creates a safe refusal. |

## static class PlatformHttpClient

Factory and escaping helpers for the shared wire seam.

| Member | Summary |
| --- | --- |
| `Create(PlatformClientOptions) : IPlatformHttpClient` | Creates a pumped requester. Calls and Poll belong to one serialized owner. |
| `Query(String) : String` | Escapes a query value. |
| `Segment(String) : String` | Escapes one nonempty route segment, refusing controls and path traversal. |

## class PlatformPresenceOptions

Presence route input; the client base must be the gateway HTTP base.

| Member | Summary |
| --- | --- |
| `ChannelId : String get set` | Lobby channel id. |
| `UserIds : IReadOnlyList<String> get set` | One to fifty player ids. |
| `ctor()` |  |

## class PlatformProfileOptions

Profile edit.

| Member | Summary |
| --- | --- |
| `Avatar : String get set` | Optional avatar; null clears it. |
| `DisplayName : String get set` | Nonempty display name. |
| `ctor()` |  |

## class PlatformRequestOptions

One escaped relative platform request.

| Member | Summary |
| --- | --- |
| `Authenticated : Boolean get set` | Whether to attach the player's credential; default true. |
| `Body : JsonValue get set` | Optional JSON body. |
| `Method : String get set` | Uppercase HTTP method. |
| `MissingAsNull : Boolean get set` | Whether a missing response is a null result rather than a refusal. |
| `Path : String get set` | Absolute-path reference within the configured base; no dot segments or fragment. |
| `ctor()` |  |

## class PlatformScoreOptions

Score input.

| Member | Summary |
| --- | --- |
| `Meta : String get set` | Optional metadata, at most 1024 UTF-8 bytes. |
| `Score : Int64 get set` | Safe integer score. |
| `ctor()` |  |
