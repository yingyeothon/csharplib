# Yingyeothon.KvStore

A client for the yyt **key-value store**: per-project collections of JSON values
addressed by key, served at `https://doc.yyt.life/kv/*` and read or written with the
channel JWT a game already holds. Two things it makes trivial — reading what the team
published (announcements) and keeping a player's own record (a profile) — and it does
nothing else: no collection admin, no cache, no retry.

The wire contract is the `service` repository's: `services/state/README.md` _KV routes_
and `docs/kvstore.md` there are normative. [docs/kvstore.md](../../docs/kvstore.md) here
is the integration guide.

## Install

```
https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.kvstore-client#v0.1.0
```

The fragment pins a release tag; keep the same one on every `com.yingyeothon.*` URL
([Unity § Installing](../../docs/unity.md#installing)).

Depends on `com.yingyeothon.codec` and `com.yingyeothon.logger`; a git-URL package
cannot resolve them, so add both first, on the same tag.

## Usage

```csharp
using Yingyeothon.Codec;
using Yingyeothon.KvStore;

var kv = KvStoreClient.Create(new KvStoreClientOptions
{
    BaseUrl = "https://doc.yyt.life",   // https://doc-dev.yyt.life on dev
    Token = channelJwt,                 // the same JWT the gateway client uses
});

// (1) announcements: a console collection with readScope=project, writeScope=team
KvPage notices = await kv.Collection("announcements")
    .ListAsync(new KvListOptions { Values = true, Order = KvOrder.Desc });

// (2) my record: a console collection with readScope=user, writeScope=user
IKvNamespace mine = kv.Collection("profile").Mine;
JsonValue? saved = await mine.GetAsync("settings");        // null the first time
await mine.PutAsync("settings", Json.Object().Set("volume", 0.5).Build());
```

A collection is addressed by its **name** or its `kv_` id, resolved within the
project the token belongs to. The collection itself is the shared namespace; a
collection whose write scope is `user` keeps one namespace per owner, reached through
`Mine` (the player the token names) or `Owner(id)` (a server key acting for a player
or a group such as `party:raid-7`).

## What the store answers

- `GetAsync` returns a C# `null` on a `404` and `JsonValue.Null` for a stored JSON
  `null`. The two are different facts and stay different. A missing **collection** is
  the same `404` as a missing key, and `DeleteAsync` treats both as done —
  [the guide](../../docs/kvstore.md#6-refusals) says how to tell them apart.
- `PutAsync` sends the value's compact JSON, byte for byte. `KvWriteResult` is all-null
  for a caller without the read right, and a lost `IfMatch` / `IfNoneMatch` is a `409`
  whose `CurrentVersion` is the live version — check `HasCurrentVersion` first.
- `ListAsync` pages by cursor and carries values only with `Values = true`. `Prefix`
  is checked with the key grammar, as the store checks it; `Cursor` is percent-encoded.
- `IncrAsync` is the store's own compare-and-set over an integer and takes no condition.

Every limit the client checks before sending is the server's own, named once in
`KvRules` and cited to `packages/console-db/src/kvstore.ts` in `service`; those are
`ArgumentException`s thrown before any request. Everything else is the store's answer,
surfaced as `KvStoreException` with `Status`, `Code` (`KvErrorCodes`), `Reason`
(`KvReasons`) and the predicates `IsConflict`, `IsForbidden`, `IsUnauthorized`,
`IsFull`. The tables are in [docs/kvstore.md](../../docs/kvstore.md#6-refusals).

## Nothing sensitive leaves the client

The token travels in the `Authorization` header and nowhere else. `KvStoreException.Message`
is `"kv {code} ({status})"` and nothing more; a log line is `"kv request"` with the
method, the route kind (`meta | entries | entry | incr`), the status and the byte
count — never a key, a value, a URL or the token. That includes the transport seam:
an `IHttpTransport` you write receives the credential in `HttpCall.Headers` and must
keep the same rule. Neither shipped transport follows a redirect, which would carry the
header — and a `PUT`'s body on a `307`/`308` — to whatever host it named. A `3xx` comes
back as `http` with its status from either transport on a native player; on WebGL, where
Unity fails the request on a redirect, it is `network` (seen in a Chrome WebGL
player: [the browser run](../../rules/manual-verification.md#the-webgl-browser-run)). The store sends none. (`UnityWebRequestTransport` followed redirects before
2026-09-30.)

## Public API

- `KvStoreClient.Create(KvStoreClientOptions)` → `IKvStoreClient`: `Collection(nameOrId)`.
  Options: `BaseUrl`, `Token`, `Transport`, `Logger`, `Timeout` (default
  `KvStoreClient.DefaultTimeout`, 15 s).
- `IKvCollection : IKvNamespace` — `Ref`, `InfoAsync` → `KvCollectionInfo` (`Id`,
  `Name`, `ReadScope`, `WriteScope` as `KvScope`, `Encrypted`, `MaxEntries`,
  `MaxEntriesPerOwner`), `Mine`, `Owner(ownerId)`.
- `IKvNamespace` — `GetAsync`, `GetEntryAsync` → `KvEntry`, `PutAsync(key, value,
  KvPutOptions?)` → `KvWriteResult`, `DeleteAsync(key, KvDeleteOptions?)`,
  `ListAsync(KvListOptions?)` → `KvPage` of `KvListEntry` (`KvOrder`),
  `IncrAsync(key, delta, KvIncrOptions?)` → `KvIncrResult`. Every method takes a
  `CancellationToken`.
- `KvStoreException` — `Status`, `Code`, `Reason`, `CurrentVersion`,
  `HasCurrentVersion`, `IsConflict`, `IsForbidden`, `IsUnauthorized`, `IsFull`; the
  constants in `KvErrorCodes` and `KvReasons`.
- `KvRules` — every limit and grammar, and `IsKey`, `IsCollectionRef`,
  `IsCollectionId`, `IsOwnerId`.
- Transport seam: `IHttpTransport`, `HttpCall`, `HttpReply`,
  `HttpClientTransport.Default` / `Create(HttpClient)`.
- Unity only, behind `#if UNITY_5_3_OR_NEWER`: `UnityWebRequestTransport.Instance`, the
  transport a WebGL build passes.

## Differences from `@yingyeothon/kvstore-client`

The shape — collections, `mine` / `owner`, the six operations, the local refusals, the
error's `status` / `code` / `reason` / `currentVersion`, the `"kv {code} ({status})"`
message and the `kv request` log line — is tslib's. What differs:

- **Values are `JsonValue`**, not `unknown` with a type parameter: the codec's value
  tree is what the rest of this SDK speaks, and it needs no reflection under IL2CPP.
  `PutAsync` sends `Json.Stringify(value)` where tslib sends `JSON.stringify(value)`;
  both are compact and byte-exact.
- **`undefined` becomes C# `null`**: `GetAsync` returns `null` on a `404` and
  `JsonValue.Null` for a stored null, where tslib returns `undefined` and `null`.
- **`fetch` becomes `IHttpTransport`**, with `HttpClientTransport.Default` behind it and
  `UnityWebRequestTransport` for WebGL, where `HttpClient` cannot send. `HttpCall`
  carries the call's `Timeout` so that transport can set `UnityWebRequest.timeout`.
- **A `Timeout` option** bounds every call (15 s) and fails it with
  `KvErrorCodes.Timeout`; tslib leaves that to the caller's `AbortSignal`. Every
  method takes a `CancellationToken` for the caller's own cancellation.
- **`KvStoreError` is `KvStoreException`** with the predicates as properties
  (`IsConflict` and so on) instead of free functions, and the client-minted codes are
  spelled `bad_body` (tslib `malformed_response`) and `http` (tslib `http_<status>`;
  the status is on `Status` here). A wire `code` or `reason` outside the store's own
  spelling is folded to `http` / null rather than reaching the message.
- **The log line counts `bytes`**, UTF-8, where tslib counts `chars`.
- **Local refusals are `ArgumentException`** (an `ArgumentOutOfRangeException` for
  `delta`), where tslib throws `RangeError` / `TypeError`.

## Samples

One importable sample ships with this package: _Package Manager → the package →
Samples → Import_. `KvStoreSession` is the engine-free half; `KvStoreQuickstart` is the
`MonoBehaviour`.
