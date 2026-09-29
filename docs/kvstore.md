# Key-value store

Per-project collections of JSON values, addressed by key, read and written with the
channel JWT your game already holds. Two things it is for: **announcements** the team
publishes from the console and every player reads, and **a player's own record** — a
profile, settings, a counter — that only that player and your server can touch.

The store is the `service` repository's: `services/state/README.md` _KV routes_ and
`docs/kvstore.md` there are the contract, and this page follows them. The package
reference is [`Yingyeothon.KvStore`](../packages/com.yingyeothon.kvstore-client/README.md).

## 1. Install

`com.yingyeothon.kvstore-client` depends on `codec` and `logger`; add those two by git
URL as in [Getting started § 1](getting-started.md#1-install-the-packages), then this:

```
https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.kvstore-client
```

**No release has been tagged yet**, so the URL tracks `main`; append `#<tag>` once one
exists. It is independent of `gamebase-client`: a game that only reads a notice board needs no
socket.

## 2. Create the collections in the console

A collection is a project resource, created on the project's page in the
[console](https://console.yyt.life/ui/) or with the `yyt` CLI — `cli/README.md` in
`service` is that recipe. Three shapes cover what a game does:

| Collection | `readScope` | `writeScope` | Who reads | Who writes |
| --- | --- | --- | --- | --- |
| `announcements` | `project` | `team` | every player and your server | the console and the CLI only |
| `profile` | `user` | `user` | each player, its own entries; your server, anyone's | the same |
| `feedback` | `team` | `project` | the console only | every player — a write-only inbox |

The scopes are `team` (console and CLI; the API refuses), `project` (any credential of
the project) and `user` (each player on its own namespace, the server key on
anyone's). A `writeScope` of `user` is what puts entries under `/u/{ownerId}`; the
matrix and the `encrypted` flag are in `service/docs/kvstore.md`. `InfoAsync` reads a
collection's shape back, which is how a client learns whether to use the shared
namespace or `Mine` before it guesses wrong.

## 3. The token

`KvStoreClientOptions.Token` is the channel JWT from [Authentication](authentication.md)
— the same one `GatewayLobbyClient` takes — and it names the player that `Mine` means.
`com.yingyeothon.auth-client` makes that request; it depends on neither this package
nor `gamebase-client`.
On a server, the channel's doc apiKey (`yds.…`) works in the same field and may name
any owner through `Owner(id)`. The client puts the token in the `Authorization` header
and nowhere else; a new token is a new client.

## 4. The two cases

```csharp
using System.Collections.Generic;
using Yingyeothon.Codec;
using Yingyeothon.KvStore;

var kv = KvStoreClient.Create(new KvStoreClientOptions
{
    BaseUrl = "https://doc.yyt.life",     // https://doc-dev.yyt.life on dev
    Token = channelJwt,
});

// Announcements, values included. A list is in KEY order, so name announcement
// keys by date (2026-09-06) and Desc reads the newest first.
KvPage notices = await kv.Collection("announcements")
    .ListAsync(new KvListOptions { Values = true, Order = KvOrder.Desc });
foreach (KvListEntry notice in notices.Entries)
{
    string title = notice.Value!.GetString("title") ?? notice.Key;
}

// A page holds at most 100 rows (50 unless Limit says otherwise); walk the cursor
// for the rest.
var all = new List<KvListEntry>();
var options = new KvListOptions { Values = true };
KvPage page;
do
{
    page = await kv.Collection("announcements").ListAsync(options);
    all.AddRange(page.Entries);
    options.Cursor = page.NextCursor;
}
while (page.NextCursor != null);

// My record: read, change, write back.
IKvNamespace mine = kv.Collection("profile").Mine;
JsonValue? saved = await mine.GetAsync("settings");          // null the first time
double volume = saved?.GetNumber("volume") ?? 1.0;
await mine.PutAsync("settings", Json.Object().Set("volume", volume).Build());

// A counter two devices cannot lose a count between.
KvIncrResult plays = await mine.IncrAsync("plays", 1);
```

The `KvStore Quickstart` sample is this page as a `MonoBehaviour`
([Unity § Samples](unity.md#samples)).

## 5. Versions, conditions and TTL

Every entry has a version that climbs on each write and never resets, even across an
expiry. `GetEntryAsync` returns it; `PutAsync` with `IfMatch` writes only if it still
holds, and `IfNoneMatch` creates only. A lost compare-and-set is a `KvStoreException`
with `IsConflict` and `CurrentVersion` — the live version, or null when the entry is
gone; `HasCurrentVersion` says whether the store said either. `IncrAsync` is its own
compare-and-set and takes no condition.

A condition, and `IncrAsync`, reveal what is stored, so they need the **read** right; a
caller with only the write right — a player writing the `feedback` inbox above — gets
`403` for either. The same rule empties `KvWriteResult` for that caller: `Created`,
`Version` and `ExpiresAt` are all null, because "did this key exist" is a fact about
stored data.

`Ttl` is seconds, 1 to 366 days, on `PutAsync` and `IncrAsync`. Omitted, an update
keeps the entry's expiry; `0` clears it. An expired entry is invisible to every read.

## 6. Refusals

Everything the client can settle locally is an `ArgumentException` thrown **before
any request** (an `ArgumentOutOfRangeException`, which derives from it, for `delta`),
and it mirrors the server's own rules — the constants are in `KvRules`:

| Refused locally | Rule |
| --- | --- |
| a key, a non-empty list `Prefix` | `^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$` |
| a collection ref | a `kv_` id, or a name `^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$` that is not id-shaped once lower-cased |
| an owner | `me`, 32 lower-case hex, or `{kind}:{id}` |
| a value | over 16 KiB of UTF-8 as JSON text |
| `Ttl`, `Limit`, `IfMatch`, `delta` | outside 1 s … 366 d (or 0), 1 … 100, below 1, and beyond ±2^53−1 |
| `IfMatch` with `IfNoneMatch` | together |
| `Token`, `BaseUrl` | a character a header cannot carry; userinfo, a query or a fragment in the URL |

Everything else arrives as a `KvStoreException`. Its message is `"kv {code}
({status})"` and safe to log; the rest is on the properties:

| `Status` | `Code` | `Reason` | Means |
| --- | --- | --- | --- |
| 400 | `bad_request` | `wrong_namespace` | the shared path on a `user`-scoped collection, or `Mine` on a shared one |
| 401 | `unauthorized` | | the token did not verify, or its channel is gone |
| 403 | `forbidden` | | the scope does not admit you, or a condition without the read right |
| 404 | `not_found` | | no such collection in your project — a name of another project's is the same 404. `GetAsync`, `GetEntryAsync` and `DeleteAsync` fold it (below); every other call throws it |
| 409 | `conflict` | | a lost `IfMatch` / `IfNoneMatch`; `CurrentVersion` says what won |
| 409 | `conflict` | `collection_full`, `owner_full` | a cap; `IsFull` |
| 409 | `conflict` | `not_a_number`, `overflow` | `IncrAsync` over a value that is not a safe integer, or would leave the range |
| 413 | `payload_too_large` | | the value; the client refuses this first |
| 503 | `unavailable` | `kv_encryption_not_configured`, `kv_value_unreadable` | the stage cannot serve values |
| 0 | `network` | | no reply; the cause is `InnerException` |
| 0 | `timeout` | | no reply within `Timeout` (15 s) |
| any | `bad_body` | | a success reply the client could not read |
| any | `http` | | a failure reply with no error envelope: the edge or a proxy answered |

Two 404s look alike on a read and on a delete: `GetAsync` returns null for a missing
**key**, `DeleteAsync` returns for one, and a missing **collection** is the same 404 to
both — so a null on the very first read of a collection you expected to exist is worth
one `InfoAsync` to tell them apart.

## 7. What this package does not do

No collection admin (the console and `yyt kv` own that), no cache (every reply is
`no-store`, and a ttl would outlive one), no retry (a `KvStoreException` is yours to
act on), and no persistence of the token.

**No client for the per-player document store either, and none is planned.** The same
host serves `/s/{ownerId}` — one versioned blob per player — but every write there takes
the auth channel's doc apiKey, a server credential a game must never ship; a player can
only `GET` its own row. For state a player writes itself, use a `user`-scoped
collection: its own entries, versioned, with `IfMatch` for a compare-and-set. **A
player-writable collection is not the place for state the server must vouch for**
(currency, inventory) — a game server writes that to `/s/*` directly.
