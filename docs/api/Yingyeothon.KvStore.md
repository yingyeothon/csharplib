# Yingyeothon.KvStore

<!-- Generated from the assembly by tests/Yingyeothon.PublicApi.Tests.
     Do not edit by hand: the test rewrites it and CI compares it. -->

Every public type and member, with its documentation comment — the same text
your IDE shows. For what the package is *for*, read
[the guide](../README.md) and
[`packages/com.yingyeothon.kvstore-client/README.md`](../../packages/com.yingyeothon.kvstore-client/README.md).

## Contents

- [`HttpCall`](#class-httpcall)
- [`HttpClientTransport`](#static-class-httpclienttransport)
- [`HttpReply`](#class-httpreply)
- [`IHttpTransport`](#interface-ihttptransport)
- [`IKvCollection`](#interface-ikvcollection)
- [`IKvNamespace`](#interface-ikvnamespace)
- [`IKvStoreClient`](#interface-ikvstoreclient)
- [`KvCollectionInfo`](#class-kvcollectioninfo)
- [`KvDeleteOptions`](#class-kvdeleteoptions)
- [`KvEntry`](#class-kventry)
- [`KvErrorCodes`](#static-class-kverrorcodes)
- [`KvIncrOptions`](#class-kvincroptions)
- [`KvIncrResult`](#class-kvincrresult)
- [`KvListEntry`](#class-kvlistentry)
- [`KvListOptions`](#class-kvlistoptions)
- [`KvOrder`](#enum-kvorder)
- [`KvPage`](#class-kvpage)
- [`KvPutOptions`](#class-kvputoptions)
- [`KvReasons`](#static-class-kvreasons)
- [`KvRules`](#static-class-kvrules)
- [`KvScope`](#enum-kvscope)
- [`KvStoreClient`](#static-class-kvstoreclient)
- [`KvStoreClientOptions`](#class-kvstoreclientoptions)
- [`KvStoreException`](#class-kvstoreexception)
- [`KvWriteResult`](#class-kvwriteresult)

## class HttpCall

One HTTP request the store client wants sent.

| Member | Summary |
| --- | --- |
| `Body : String get` | The UTF-8 body to send, or null for a request without one. |
| `Headers : IReadOnlyList<KeyValuePair<String, String>> get` | The request headers, in order. One of them is the credential. |
| `Method : String get` | The HTTP method, upper-case: `GET` , `PUT` , `PATCH` or `DELETE` . |
| `Timeout : TimeSpan get` | How long the client will wait for the reply. The cancellation token the transport receives fires at this bound too; this is for a transport whose own timer is the only one that can run — a WebGL build has no thread for the token's. |
| `Url : Uri get` | The absolute URL. Send `AbsoluteUri` , never `ToString()` , which unescapes. |
| `ctor(String, Uri, IReadOnlyList<KeyValuePair<String, String>>, String, TimeSpan)` |  |

## static class HttpClientTransport

The default `IHttpTransport` , over `HttpClient` .

| Member | Summary |
| --- | --- |
| `Create(HttpClient) : IHttpTransport` | A transport over a client the caller owns and configures — a custom handler, a proxy, a certificate policy. The client must set no `Authorization` header of its own, should not follow redirects, and should set `MaxResponseContentBufferSize` : the default is two gigabytes, and the store never sends more than a few megabytes. |
| `Default : IHttpTransport get` | One shared client for the process, with no default headers and no redirect following: a redirect would carry the credential to whatever host it named. Its own timeout is 100 seconds; the per-call bound is `Timeout` . |

## class HttpReply

What the server answered: status, headers and the body as text.

| Member | Summary |
| --- | --- |
| `Body : String get` | The body decoded as UTF-8, or an empty string. Never log it: it is a stored value. |
| `Headers : IReadOnlyList<KeyValuePair<String, String>> get` | The response headers. Names are matched case-insensitively by the client. |
| `Status : Int32 get` | The HTTP status code. |
| `ctor(Int32, IReadOnlyList<KeyValuePair<String, String>>, String)` |  |

## interface IHttpTransport

The HTTP seam the store client sends through.

| Member | Summary |
| --- | --- |
| `SendAsync(HttpCall, CancellationToken) : Task<HttpReply>` | Sends one request and returns whatever status the server answered with. The credential is in `Headers` , and the URL carries a key: an exception thrown here becomes the `InnerException` of a `KvStoreException` and reaches whatever the game logs, so its message must name neither. Bound what is buffered: the largest reply the store sends is a page of a hundred 16 KiB values. |

## interface IKvCollection

One collection: its shape, its shared namespace, and its owner namespaces.

| Member | Summary |
| --- | --- |
| `InfoAsync(CancellationToken?) : Task<KvCollectionInfo>` | The collection's scopes, flag and caps. A 403 when both scopes are `team` . |
| `Mine : IKvNamespace get` | The namespace of the player the token names: `/kv/{col}/u/me/entries` . A player token only. |
| `Owner(String) : IKvNamespace` | One owner's namespace: `/kv/{col}/u/{ownerId}/entries` . Throws `ArgumentException` unless `IsOwnerId` . |
| `Ref : String get` | What was passed to `Collection` . |

## interface IKvNamespace

The six operations on one namespace of entries.

| Member | Summary |
| --- | --- |
| `DeleteAsync(String, KvDeleteOptions?, CancellationToken?) : Task` | Deletes an entry. A `404` — no such key, or no such collection — is treated as done. |
| `GetAsync(String, CancellationToken?) : Task<JsonValue>` | The stored value, or a C# null on a `404` — no such entry, and also no such collection, which this call does not tell apart. A stored JSON null is `Null` . |
| `GetEntryAsync(String, CancellationToken?) : Task<KvEntry>` | The stored value with its version and expiry, or null on a `404` , as for `GetAsync` . |
| `IncrAsync(String, Int64, KvIncrOptions?, CancellationToken?) : Task<KvIncrResult>` | Adds `delta` to a stored integer atomically, creating it from zero. Needs the read right as well as the write right; a stored value that is not an integer is a 409 with `NotANumber` . |
| `ListAsync(KvListOptions?, CancellationToken?) : Task<KvPage>` | One page of entries, in key order. |
| `PutAsync(String, JsonValue, KvPutOptions?, CancellationToken?) : Task<KvWriteResult>` | Stores `value` under `key` , as its compact JSON text, byte for byte. |

## interface IKvStoreClient

A key-value store client bound to one base URL and one token.

| Member | Summary |
| --- | --- |
| `Collection(String) : IKvCollection` | Addresses a collection by its name or its `kv_` id. Pure: nothing is sent until an operation is called. Throws `ArgumentException` for a segment that is neither an id nor a name the console could hold. |

## class KvCollectionInfo

A collection's shape, from `GET /kv/{col}` .

| Member | Summary |
| --- | --- |
| `Encrypted : Boolean get` | Whether values are stored encrypted. Invisible to this client: the store decrypts on read. |
| `Id : String get` | The collection id, `kv_…` , whichever way it was addressed. |
| `MaxEntries : Int32 get` | The cap on rows in the whole collection. |
| `MaxEntriesPerOwner : Int32 get` | The cap on rows one player may hold in a user namespace. |
| `Name : String get` | The collection name as the console holds it. |
| `ReadScope : KvScope get` | Who may read entries. |
| `WriteScope : KvScope get` | Who may write entries. `User` is what puts them under `Mine` . |
| `ctor(String, String, KvScope, KvScope, Boolean, Int32, Int32)` |  |

## class KvDeleteOptions

Options for `DeleteAsync` .

| Member | Summary |
| --- | --- |
| `IfMatch : Nullable<Int64> get set` | Delete only if the stored version is this one. Needs the read right. |
| `ctor()` |  |

## class KvEntry

One entry with its version, from `GetEntryAsync` .

| Member | Summary |
| --- | --- |
| `ExpiresAt : Nullable<Int64> get` | When the entry expires, in Unix seconds, or null when it does not. |
| `Value : JsonValue get` | The stored value. A stored JSON `null` is `Null` , never a C# null. |
| `Version : Int64 get` | The version, for `IfMatch` . It climbs on every write and never resets. |
| `ctor(JsonValue, Int64, Nullable<Int64>)` |  |

## static class KvErrorCodes

The `Code` values this client produces or passes through.

| Member | Summary |
| --- | --- |
| `BadBody : String` | A success reply whose body or headers were not what the store sends. |
| `BadRequest : String` | 400. A grammar or a header the store refused; often with a `KvReasons` . |
| `Conflict : String` | 409. A lost compare-and-set, a full collection, or a value that is not a counter. |
| `Forbidden : String` | 403. The collection's scope does not admit this caller for this operation. |
| `Http : String` | A failure reply that carried no `{error:{code}}` body — a gateway or proxy answered. |
| `Network : String` | No reply arrived: the transport threw. The cause is the inner exception. |
| `NotFound : String` | 404. No such collection in the caller's project. `GetAsync` , `GetEntryAsync` and `DeleteAsync` fold a 404 into null or done instead of throwing it. |
| `PayloadTooLarge : String` | 413. The value is over `MaxValueBytes` ; the client refuses this first. |
| `Timeout : String` | No reply arrived within `Timeout` . |
| `Unauthorized : String` | 401. The token did not verify, or its channel is gone. |
| `Unavailable : String` | 503. The store cannot serve values right now; see `KvReasons` . |

## class KvIncrOptions

Options for `IncrAsync` .

| Member | Summary |
| --- | --- |
| `Ttl : Nullable<Int32> get set` | Seconds until the counter expires, with the same meaning as `KvPutOptions.Ttl` . |
| `ctor()` |  |

## class KvIncrResult

What an `incr` answered.

| Member | Summary |
| --- | --- |
| `ExpiresAt : Nullable<Int64> get` | The expiry this call set, in Unix seconds; null when it set none. |
| `Value : Int64 get` | The counter after the increment. |
| `Version : Int64 get` | The version the increment produced. |
| `ctor(Int64, Int64, Nullable<Int64>)` |  |

## class KvListEntry

One row of a listing.

| Member | Summary |
| --- | --- |
| `Bytes : Int64 get` | The value's size in UTF-8 bytes, as stored. |
| `ExpiresAt : Nullable<Int64> get` | When the entry expires, in Unix seconds, or null. |
| `Key : String get` | The key. |
| `Owner : String get` | The owner, only where owners are a namespace; null on a shared collection. |
| `UpdatedAt : Int64 get` | When the entry was last written, in Unix seconds. |
| `Value : JsonValue get` | The value when `Values` was set, else a C# null. A stored JSON null is `Null` . |
| `Version : Int64 get` | The version. |
| `ctor(String, String, Int64, Int64, Nullable<Int64>, Int64, JsonValue)` |  |

## class KvListOptions

Options for `ListAsync` .

| Member | Summary |
| --- | --- |
| `Cursor : String get set` | The `NextCursor` of the previous page. |
| `Limit : Nullable<Int32> get set` | Rows per page, `MinListLimit` to `MaxListLimit` . Null lets the server choose (50). |
| `Order : Nullable<KvOrder> get set` | Key order. Null is the server's default, ascending. |
| `Prefix : String get set` | Only keys starting with this. A non-empty prefix must satisfy `IsKey` , as the store checks it with the key grammar. |
| `Values : Boolean get set` | Whether each row carries its value. Off, a row is metadata only. |
| `ctor()` |  |

## enum KvOrder

List order by key.

- `Asc` — Ascending key order, the server's default.
- `Desc` — Descending key order.

## class KvPage

One page of a listing.

| Member | Summary |
| --- | --- |
| `Entries : IReadOnlyList<KvListEntry> get` | The rows, in key order. |
| `NextCursor : String get` | Pass as `Cursor` for the next page; null on the last one. |
| `ctor(IReadOnlyList<KvListEntry>, String)` |  |

## class KvPutOptions

Options for `PutAsync` .

| Member | Summary |
| --- | --- |
| `IfMatch : Nullable<Int64> get set` | Write only if the stored version is this one; a mismatch is a 409 with `CurrentVersion` . Needs the read right. |
| `IfNoneMatch : Boolean get set` | Create only: refuse with a 409 when the key exists. Needs the read right. Cannot be combined with `IfMatch` . |
| `Ttl : Nullable<Int32> get set` | Seconds until the entry expires: `MinTtlSeconds` to `MaxTtlSeconds` , or `0` to clear an expiry. Null keeps whatever the entry has (no expiry, on a create). |
| `ctor()` |  |

## static class KvReasons

The `Reason` values the store attaches to a refusal.

| Member | Summary |
| --- | --- |
| `CollectionFull : String` | 409: the collection holds `maxEntries` rows already. |
| `Encrypted : String` | 409: the console cannot write an encrypted collection. Not reachable from this client. |
| `EncryptionNotConfigured : String` | 503: the stage has no usable key; every kv route answers this. |
| `NotANumber : String` | 409 on `incr` : the stored value is not a safe integer. |
| `Overflow : String` | 409 on `incr` : the result would leave the safe integer range. |
| `OwnerFull : String` | 409: this owner holds `maxEntriesPerOwner` rows already. |
| `ValueUnreadable : String` | 503: one stored value would not open. |
| `WrongNamespace : String` | 400: the shared path was used on a user-namespace collection, or the reverse. |

## static class KvRules

The store's own limits and grammars, so a caller can check before sending.

| Member | Summary |
| --- | --- |
| `IsCollectionId(String) : Boolean` | Whether `nameOrId` is a collection id ( `kv_` and 26 of `[0-9a-z]` ). |
| `IsCollectionRef(String) : Boolean` | Whether `nameOrId` is a collection id, or a name the store would look up: the name grammar, and not id-shaped once lower-cased, which the console refuses and the store answers without a lookup. |
| `IsKey(String) : Boolean` | Whether `key` matches the key grammar. |
| `IsOwnerId(String) : Boolean` | Whether `ownerId` is `Me` , a player id (32 lower-case hex, what a token's `sub` holds) or a group owner `{kind}:{id}` ( `KV_OWNER_ID` ). |
| `MaxCollectionNameLength : Int32` | The longest collection name ( `^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$` , the console's grammar). |
| `MaxKeyLength : Int32` | The longest key ( `KV_KEY_RE` : `^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$` ). |
| `MaxListLimit : Int32` | The largest list `limit` ( `KV_LIST_LIMIT_MAX` ). Omitted, the server uses 50. |
| `MaxSafeInteger : Int64` | The largest magnitude an `incr` delta or a counter may have: JavaScript's safe integer. |
| `MaxTtlSeconds : Int32` | The largest `ttl` in seconds: 366 days ( `KV_TTL_MAX_SECONDS` ). |
| `MaxValueBytes : Int32` | The largest value, in UTF-8 bytes of its JSON text ( `MAX_KV_VALUE_BYTES` ). |
| `Me : String` | The owner alias for the player the token names. |
| `MinListLimit : Int32` | The smallest list `limit` ( `kvPageLimit` ). |
| `MinTtlSeconds : Int32` | The smallest `ttl` in seconds ( `KV_TTL_MIN_SECONDS` ). `0` is allowed too: it clears the expiry. |

## enum KvScope

Who may read or write a collection. A closed set on the server.

- `Project` — Any credential of the collection's project: every player and the server key.
- `Team` — The console and the CLI only. The API refuses, so this client never has the right.
- `User` — Each player on its own namespace; the server key on anyone's. Entries live under `/u/{ownerId}` .

## static class KvStoreClient

Creates key-value store clients.

| Member | Summary |
| --- | --- |
| `Create(KvStoreClientOptions) : IKvStoreClient` | Creates a client. Throws `ArgumentException` when `BaseUrl` is not `http(s)://host[/prefix]` with no userinfo, query or fragment, when `Token` is empty or holds a character a header cannot carry, or when the timeout is outside `(0, MaxTimeout]` . The options are copied; changing them afterwards changes nothing. |
| `DefaultTimeout : TimeSpan` | The per-call bound used when `Timeout` is null. |
| `MaxTimeout : TimeSpan` | The longest `Timeout` accepted: what a cancellation timer can count, about 24 days. |

## class KvStoreClientOptions

Options for `Create` .

| Member | Summary |
| --- | --- |
| `BaseUrl : String get set` | The store's origin: `https://doc.yyt.life` , or `https://doc-dev.yyt.life` on dev. Required; no default. |
| `Logger : ILogger get set` | Where `kv request` lines go. Null is `NullLogger.Instance` . |
| `Timeout : Nullable<TimeSpan> get set` | How long one call may take before it fails with `Timeout` . Null is 15 seconds; the most is `MaxTimeout` . It bounds the whole call, so a transport with a longer timeout of its own is cut short here. |
| `Token : String get set` | The channel JWT a player holds, or the channel's doc apiKey on a server. Required. It travels in the `Authorization` header only and reaches no log line, no exception and no URL. |
| `Transport : IHttpTransport get set` | The HTTP seam. Null is `HttpClientTransport.Default` ; a WebGL build passes `UnityWebRequestTransport.Instance` . |
| `ctor()` |  |

## class KvStoreException

A store request that did not succeed.

| Member | Summary |
| --- | --- |
| `Code : String get` | The error code: the server's `error.code` , or one of the client's own. |
| `CurrentVersion : Nullable<Int64> get` | The live version a compare-and-set lost to, or null when the entry does not exist. Only sent to a caller with the read right; check `HasCurrentVersion` before reading a null as "absent". |
| `HasCurrentVersion : Boolean get` | Whether the reply carried `details.current` at all. |
| `IsConflict : Boolean get` | A 409: a lost compare-and-set, a cap, or a value that is not a counter. |
| `IsForbidden : Boolean get` | A 403: the collection's scope does not admit this caller, or a conditional write without the read right. |
| `IsFull : Boolean get` | A 409 whose reason is `CollectionFull` or `OwnerFull` . |
| `IsUnauthorized : Boolean get` | A 401: the token did not verify. |
| `Reason : String get` | The server's `details.reason` , when it named one. See `KvReasons` . |
| `Status : Int32 get` | The HTTP status, or 0 when no reply arrived ( `Network` , `Timeout` ). |
| `ctor(Int32, String)` | Creates an exception for a status and a code, with no reason and no version. |
| `ctor(Int32, String, String, Nullable<Int64>, Boolean, Exception)` | Creates an exception carrying everything the error body said. |

## class KvWriteResult

What a write answered. Every field is null for a caller without the read right.

| Member | Summary |
| --- | --- |
| `Created : Nullable<Boolean> get` | True for a create ( `201` ), false for an update ( `204` with an `ETag` ), null when the store said neither. |
| `ExpiresAt : Nullable<Int64> get` | The expiry this write set, in Unix seconds; null when the write set none. |
| `Version : Nullable<Int64> get` | The version this write produced, from the `ETag` ; null without the read right. |
| `ctor(Nullable<Boolean>, Nullable<Int64>, Nullable<Int64>)` |  |
