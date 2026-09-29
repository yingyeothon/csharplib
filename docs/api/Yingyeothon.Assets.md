# Yingyeothon.Assets

<!-- Generated from the assembly by tests/Yingyeothon.PublicApi.Tests.
     Do not edit by hand: the test rewrites it and CI compares it. -->

Every public type and member, with its documentation comment — the same text
your IDE shows. For what the package is *for*, read
[the guide](../README.md) and
[`packages/com.yingyeothon.asset-client/README.md`](../../packages/com.yingyeothon.asset-client/README.md).

## Contents

- [`AssetBundleClient`](#static-class-assetbundleclient)
- [`AssetBundleClientOptions`](#class-assetbundleclientoptions)
- [`AssetClientException`](#class-assetclientexception)
- [`AssetDownloadOptions`](#class-assetdownloadoptions)
- [`AssetDownloadProgress`](#class-assetdownloadprogress)
- [`AssetDownloadResult`](#class-assetdownloadresult)
- [`AssetErrorCodes`](#static-class-asseterrorcodes)
- [`AssetFiles`](#static-class-assetfiles)
- [`AssetHttpClientTransport`](#static-class-assethttpclienttransport)
- [`AssetHttpRequest`](#class-assethttprequest)
- [`AssetReadOptions`](#class-assetreadoptions)
- [`AssetResume`](#class-assetresume)
- [`IAssetBundleClient`](#interface-iassetbundleclient)
- [`IAssetResponse`](#interface-iassetresponse)
- [`IAssetSink`](#interface-iassetsink)
- [`IAssetTransport`](#interface-iassettransport)

## static class AssetBundleClient

Creates asset bundle clients.

| Member | Summary |
| --- | --- |
| `Create(AssetBundleClientOptions) : IAssetBundleClient` | Creates a client for one bundle. Throws `AssetClientException` ( `bad_key` ) for a key that is not canonical or given in both forms at once, and `ArgumentException` for a `BaseUrl` that is not an http(s) URL — or, with a key, not of the bundle or version shape — or for a timeout outside `(0, MaxTimeout]` . |
| `DefaultTimeout : TimeSpan` | The bound used when a timeout option is null. |
| `MaxTimeout : TimeSpan` | The longest timeout accepted: what a cancellation timer can count. |

## class AssetBundleClientOptions

Options for `Create` . The client copies them, and copies the key.

| Member | Summary |
| --- | --- |
| `BaseUrl : String get set` | `https://{cdn}/assets/{bundleId}/` for a live bundle, plus `{version}/` for one version of a versioned bundle. The CDN is `d.yyt.life` , `dev-d.yyt.life` on dev; there is no default. |
| `BodyIdleTimeout : Nullable<TimeSpan> get set` | How long a body may go without delivering its next piece before the read fails as `network` — a connection gone quiet, as when a phone changes networks. Null is 30 seconds. |
| `CorsSafe : Nullable<Boolean> get set` | Send only CORS-safelisted request headers ( `Range` , never `If-Range` or `Cache-Control` ) and read only the headers the yyt CDN exposes to scripts ( `ETag` , `Content-Length` ); a ranged read then costs a `HEAD` first. Null is true in a WebGL player and false everywhere else. |
| `Key : String get set` | The key of an encrypted bundle as `yyt asset key show` prints it ( `yak1.…` ). Set this or `KeyBytes` , or neither for a plain bundle. Never logged. |
| `KeyBytes : Byte[] get set` | The key's 32 raw bytes, instead of `Key` . Copied; the caller's array is untouched. |
| `Logger : ILogger get set` | Where `asset request` lines go. Null is `NullLogger.Instance` . |
| `ResponseTimeout : Nullable<TimeSpan> get set` | How long a request may wait for its response headers before it fails as `network` . Null is 30 seconds. |
| `Transport : IAssetTransport get set` | The HTTP seam. Null is `Default` ; a WebGL build passes `AssetUnityWebRequestTransport.Instance` . |
| `ctor()` |  |

## class AssetClientException

The one exception the client throws for a bad key, a refused or failed request, or bytes that do not verify.

| Member | Summary |
| --- | --- |
| `Code : String get` | One of `AssetErrorCodes` . |
| `Detail : String get` | A fixed SDK phrase that narrows `Code` , or null. |
| `Status : Int32 get` | The HTTP status, or 0 when there was no answer to report. |
| `ctor(String, Int32, String, Exception)` | Creates an exception. |

## class AssetDownloadOptions

Options for `DownloadAsync` .

| Member | Summary |
| --- | --- |
| `NoCache : Boolean get set` | As `NoCache` . |
| `OnProgress : Action<AssetDownloadProgress> get set` | Called after every piece, on the thread the download continues on. Whatever it throws ends the download and reaches the caller. |
| `Resume : AssetResume get set` | Where an earlier download stopped, or null for a fresh one. |
| `ctor()` |  |

## class AssetDownloadProgress

Reported after every piece a download writes, and once for a download that wrote nothing.

| Member | Summary |
| --- | --- |
| `ETag : String get` | The object's ETag; keep it with `Written` to resume later. |
| `Total : Nullable<Int64> get` | The file's plaintext length; null only for a plain file whose length the client cannot trust — a compressed or unsized body, and in CORS-safe mode any whole-file answer. |
| `Written : Int64 get` | Plaintext bytes the sink holds, a resumed offset included. |
| `ctor(Int64, Nullable<Int64>, String)` |  |

## class AssetDownloadResult

What a finished download wrote.

| Member | Summary |
| --- | --- |
| `Bytes : Int64 get` | The file's plaintext length, now all in the sink. |
| `ETag : String get` | The object's ETag, when the host named one. |
| `ctor(Int64, String)` |  |

## static class AssetErrorCodes

The `Code` values, in the vocabulary the tslib, csharplib and flutterlib asset clients share. String constants, not an enum.

| Member | Summary |
| --- | --- |
| `AssetCorrupt : String` | A length no ciphertext has, a failed segment tag, a wrong key, path or version, or `ReadJsonAsync` on bytes that are not UTF-8 JSON. Not retried: a wrong key and a wrong path fail the same way every time. (A mutable file replaced mid-read on a host that sends no ETag also lands here.) |
| `BadKey : String` | The key is not `yak1.` + 43 base64url characters of 32 bytes, or 32 raw bytes. Thrown by `Create` . |
| `Http : String` | Any other status, a host that ignores `Range` , a 206 that is not the range asked for, an object that kept changing, or a plain body larger than any asset. |
| `Network : String` | No answer (in time), or a body that failed, stalled, ended early or ran past its stated length. |
| `NotFound : String` | The CDN answered 403 or 404. A missing object answers 403 on the yyt CDN , so the two are one case. |

## static class AssetFiles

A resumable download of one asset straight to a file.

| Member | Summary |
| --- | --- |
| `DownloadToFileAsync(IAssetBundleClient, String, String, Action<AssetDownloadProgress>?, CancellationToken?) : Task<AssetDownloadResult>` | Downloads `path` into the file at `destination` , resuming an earlier call's unfinished download of the same object. |

## static class AssetHttpClientTransport

The default `IAssetTransport` , over `HttpClient` , streaming each body.

| Member | Summary |
| --- | --- |
| `Create(HttpClient) : IAssetTransport` | A transport over a client the caller owns: a proxy, a certificate policy. |
| `Default : IAssetTransport get` | One shared client for the process: no decompression (a stated length must be the bytes that arrive), no redirects, and no timeout of its own — the asset client bounds the wait for headers and for each piece of a body itself. |

## class AssetHttpRequest

One request the asset client wants sent: a `GET` or a `HEAD` .

| Member | Summary |
| --- | --- |
| `Headers : IReadOnlyList<KeyValuePair<String, String>> get` | The request headers, in order: `Range` , and outside CORS-safe mode `If-Range` and `Cache-Control` . |
| `Method : String get` | `GET` or `HEAD` . |
| `ResponseTimeout : TimeSpan get` | How long the client waits for the response headers. The token the transport receives fires at this bound too; this is for a transport whose own timer is the only one that can run — a WebGL build has no thread for the token's. |
| `Url : Uri get` | The absolute URL. Send `AbsoluteUri` , never `ToString()` , which unescapes. |
| `ctor(String, Uri, IReadOnlyList<KeyValuePair<String, String>>, TimeSpan)` |  |

## class AssetReadOptions

Options for one read.

| Member | Summary |
| --- | --- |
| `NoCache : Boolean get set` | Sends `Cache-Control: no-cache` — never in CORS-safe mode, where the header needs a preflight the CDN refuses. A mutable file is served `no-cache` already. |
| `ctor()` |  |

## class AssetResume

Where an earlier download stopped.

| Member | Summary |
| --- | --- |
| `ETag : String get` | The ETag that earlier download reported; another object starts over. |
| `Offset : Int64 get` | Plaintext bytes the sink already holds from an earlier download. |
| `ctor(Int64, String)` | Creates a resume point: a non-negative byte count and the non-empty ETag an earlier `AssetDownloadProgress` reported. |

## interface IAssetBundleClient

A reader for one asset bundle on the yyt CDN. With a key it decrypts `yyt-enc v1` ciphertext, verifying every 64 KiB segment before releasing a byte of it; without one it reads a plain bundle through the same calls.

| Member | Summary |
| --- | --- |
| `DownloadAsync(String, IAssetSink, AssetDownloadOptions?, CancellationToken?) : Task<AssetDownloadResult>` | Streams the file into `sink` , each encrypted segment verified before a byte of it is written, and continues from `Resume` while the object still has that ETag (otherwise the sink is reset and it starts from byte 0). Whatever the sink or the progress callback throws ends the download and reaches the caller unchanged. A keyed resume offset past the end of the file is an `ArgumentOutOfRangeException` , raised only once the last segment verified. |
| `ReadAsync(String, AssetReadOptions?, CancellationToken?) : Task<Byte[]>` | The whole file, verified before any byte is returned. |
| `ReadJsonAsync(String, AssetReadOptions?, CancellationToken?) : Task<JsonValue>` | The whole file as UTF-8 JSON (a leading byte-order mark dropped); anything else, or a document over the codec's limits (64 Mi characters, 64 levels), is `asset_corrupt` . |
| `ReadRangeAsync(String, Int64, Nullable<Int64>, AssetReadOptions?, CancellationToken?) : Task<Byte[]>` | Plaintext bytes `[start, end)` , clamped to the file; `end` null reads to the end, and `end <= start` returns empty without a request. An encrypted file fetches only the segments the window covers. |

## interface IAssetResponse

An answer whose body is read in pieces, at the caller's pace.

| Member | Summary |
| --- | --- |
| `GetHeader(String) : String` | A response header by name, matched case-insensitively, or null when it is absent or the platform cannot read it (in a browser only `ETag` and `Content-Length` are exposed by the yyt CDN). |
| `ReadAsync(Byte[], Int32, Int32, CancellationToken) : Task<Int32>` | Reads up to `count` body bytes; 0 means the body ended. Throw when the transfer failed — the message must not name the URL. |
| `Status : Int32 get` | The HTTP status code. |

## interface IAssetSink

Where `DownloadAsync` puts the plaintext, one verified piece at a time.

| Member | Summary |
| --- | --- |
| `ResetAsync(CancellationToken) : Task` | The object changed since the bytes already written (or since the resume): drop everything, the download starts again from byte 0. |
| `WriteAsync(ArraySegment<Byte>, CancellationToken) : Task` | The next plaintext bytes, in order; awaited before the next piece. The segment's array may be reused by the client after the task completes: copy it to keep it. |

## interface IAssetTransport

The HTTP seam the asset client reads through.

| Member | Summary |
| --- | --- |
| `SendAsync(AssetHttpRequest, CancellationToken) : Task<IAssetResponse>` | Sends one request and returns once its headers arrived. |
