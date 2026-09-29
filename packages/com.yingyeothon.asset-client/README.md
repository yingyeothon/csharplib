# Yingyeothon.Assets

A reader for yyt **asset bundles** on the CDN (`https://d.yyt.life/assets/{bundleId}/`,
`https://dev-d.yyt.life` on dev): a whole file, a JSON manifest, a byte range, or a
resumable download into a sink or straight to a file. With the bundle's key it reads an
encrypted bundle, whose files the CDN serves as `yyt-enc v1` ciphertext, and verifies
every 64 KiB segment before releasing a byte of it; without a key it reads a plain bundle
through the same calls. Nothing it logs, throws or returns contains the key, a URL or a
byte of plaintext.

The format is the `service` repository's `docs/asset-encryption.md`, and its
`docs/asset-encryption-vectors.json` is this package's conformance test.
[docs/assets.md](../../docs/assets.md) is the guide: bundle shapes and the manifest
pattern.

## Install

```
https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.asset-client
```

**No release has been tagged yet**, so this URL tracks `main`; append `#<tag>` to
pin one as soon as there is one.

Depends on `com.yingyeothon.codec` and `com.yingyeothon.logger`; a git-URL package
cannot resolve them, so add both first. It depends on no other client here.

## Usage

```csharp
using System.IO;
using UnityEngine;
using Yingyeothon.Assets;
using Yingyeothon.Codec;

// A field, kept for as long as the game reads the bundle — not a `using` in one method.
IAssetBundleClient bundle = AssetBundleClient.Create(new AssetBundleClientOptions
{
    BaseUrl = "https://d.yyt.life/assets/ab_0123456789abcdef/",   // a live bundle; add "v3/" for a version
    Key = bundleKey,          // "yak1.…" from `yyt asset key show <bundle>`; omit for a plain bundle
});

JsonValue manifest = await bundle.ReadJsonAsync("manifest.json");      // a mutable file
byte[] db = await bundle.ReadAsync(manifest.GetString("db")!);          // one file, whole
byte[] intro = await bundle.ReadRangeAsync("music/intro.ogg", 0, 65536);

await AssetFiles.DownloadToFileAsync(bundle, "music/album.ogg",
    Path.Combine(Application.persistentDataPath, "album.ogg"),
    p => progressBar.value = p.Total.HasValue ? (float)p.Written / p.Total.Value : 0);

try { await bundle.ReadAsync("data/missing.db"); }
catch (AssetClientException e) when (e.Code == AssetErrorCodes.NotFound) { AskForAnUpdate(); }
```

One client per bundle, kept for as long as the app reads it: `Dispose()` zeroes the key
and the derived keys, and a read in flight then stops at its next piece with an
`ObjectDisposedException` — which a `catch (AssetClientException)` does not catch.
Dispose it when the game is done with the bundle, not when a method returns.

## Base URL and paths

`BaseUrl` is `https://{cdn}/assets/{bundleId}/` for a live bundle and
`…/{bundleId}/{version}/` for one version of a versioned bundle; the trailing slash is
optional, and `yyt asset files <bundle>` prints every file's URL. A file's associated
data — what its ciphertext is bound to — is its object key below the bundle: `{path}` in
a live bundle and `{version}/{path}` in a versioned one. The client derives it, and a
keyed client refuses a `BaseUrl` of neither shape with an `ArgumentException`.

**`BaseUrl` is the bundle or the version, never a folder inside it.**
`…/assets/ab_1/music/` has the shape of version `music`, so every read of a live
bundle through it fails as `asset_corrupt`; put the folder in the path instead. The same
bytes served under another path, version or bundle fail the same way as a wrong key.

A path is segments separated by `/`, with no leading slash, no empty, `.` or `..`
segment, no backslash, no control character and no lone surrogate. Each segment is
percent-encoded into the URL and bound as raw UTF-8. What a bundle can actually hold is
narrower — the console's own path rule, in
[Asset bundles § The manifest pattern](../../docs/assets.md#the-manifest-pattern).

## The key

`Key` is the text `yak1.` + 43 base64url characters, exactly as `yyt asset key show`
prints it; `KeyBytes` takes its 32 raw bytes instead. A text that is not canonical —
another prefix, padding, `+` or `/`, or a last character outside `AEIMQUYcgkosw048` that
decodes to the same bytes — is `bad_key`, so a lenient and a strict decoder never
disagree on what a key is. The client copies the key; `Dispose()` zeroes its copy and
every derived key it holds, and each read zeroes its own segment keys on the way out.
The .NET runtime gives no guarantee beyond that: the text form is a `string` nothing
can zero, and the garbage collector may have moved bytes before they were cleared.

**The key is inside every copy of the app.** Encryption keeps the bundle from someone
who finds a CDN URL; it does not keep it from your players.

**Omitting the key for an encrypted bundle is not an error.** The client then reads a
plain bundle and returns the ciphertext as served; only `ReadJsonAsync` notices, as
`asset_corrupt`.

## Reads

What each call of a keyed client puts on the wire:

| Call | `CorsSafe = false` | `CorsSafe = true` | Holds in memory |
| --- | --- | --- | --- |
| `ReadAsync` / `ReadJsonAsync` | one `GET` | one `GET` | the file twice: cipher and plain |
| `ReadRangeAsync` starting in segment 0 | one ranged `GET` | `HEAD`, one ranged `GET` | the covered segments |
| `ReadRangeAsync` starting after it | header `GET`, one ranged `GET` | `HEAD`, header `GET`, one ranged `GET` | the covered segments |
| `DownloadAsync` from the start | one ranged `GET` (`bytes=0-`) | `HEAD`, one ranged `GET` | one 64 KiB segment |
| `DownloadAsync` resumed past segment 0 | header `GET`, one ranged `GET` | `HEAD`, header `GET`, one ranged `GET` | one 64 KiB segment |

A plain bundle makes one request per call: a `GET` for a read and a fresh download, one
ranged `GET` for a range and a resumed download.

`ReadRangeAsync(path, start, end)` takes plaintext offsets, `end` exclusive or null (to
the end), and clamps to the file. An empty window (`end <= start`) returns at once
without a request. A window that is empty only after clamping still fetches and verifies
the last segment, because the length the host states is not authenticated until that
segment's tag holds.

`AssetReadOptions.NoCache` sends `Cache-Control: no-cache` — never in CORS-safe mode,
where the header needs a preflight the CDN refuses. A mutable file (a manifest) is
served `no-cache` by the CDN, so a client with an HTTP cache revalidates it anyway.

`ResponseTimeout` (30 s) bounds the wait for each response's headers and
`BodyIdleTimeout` (30 s) each wait for the next piece of a body, so a connection that
went quiet ends as `network` rather than hanging. Both are enforced by the client itself,
not only through the token it hands the transport. **With a transport that buffers the
whole body before answering** — `AssetUnityWebRequestTransport` does — `ResponseTimeout`
bounds the whole transfer: raise it to cover the largest file you read or download there. A plain body larger than any asset the
platform accepts (the ciphertext ceiling, 268,566,664 bytes) is refused as `http`; a
keyed one fails its length rules first, as `asset_corrupt`.

## Downloads and resume

`DownloadAsync(path, sink, options)` writes plaintext into an `IAssetSink`: `WriteAsync`
gets the next bytes in order and is awaited before the next piece, and `ResetAsync`
means everything written so far must go, because the object is not the one those bytes
came from. With a key, each chunk is one verified segment, so nothing reaches the sink
before its tag verified.

`AssetDownloadOptions.Resume = new AssetResume(offset, etag)` continues from the segment
holding `offset`, so it re-fetches at most one segment, and only while the object still
has that ETag; any other object resets the sink and starts from byte 0. A keyed resume
that was already complete still verifies the last segment and writes nothing; a keyed
offset past the end of the file is an `ArgumentOutOfRangeException`, raised only once
the last segment verified. `OnProgress` fires after every piece with `Written`, `Total`
and `ETag`, and once for a download that wrote nothing, on the thread the download
continues on — Unity's main thread when you started it there, since the client never
leaves your synchronization context. Whatever the sink or `OnProgress`
throws ends the download and reaches you unchanged — that is also how to stop one from
the UI.

`AssetFiles.DownloadToFileAsync(bundle, path, destination, onProgress)` is that recipe
for a file: the plaintext goes to `{destination}.part` and the ETag to
`{destination}.part.etag`, a later call resumes from them, and `destination` appears
only after the last segment verified. **The file is plaintext on disk**: put it in the
app's private storage. One call per destination at a time.

## Browsers and `CorsSafe`

The yyt CDN answers `access-control-allow-origin: *`, exposes only `ETag` and
`Content-Length` to scripts, and answers a CORS preflight with `403`. With
`CorsSafe = true` the client sends no header but `Range` (safelisted for a single
`bytes=` range), learns the length and the ETag from a `HEAD`, and compares each `206`'s
ETag with it. A browser that still preflights `Range` makes the ranged request fail right
after its `HEAD` succeeded; the client then logs `asset ranged request refused; reading
the whole file` at `Warn` and reads that file with one plain `GET` (a request that timed
out is `network`, never taken for that refusal). `CorsSafe` defaults to true in a WebGL
player (`UNITY_WEBGL && !UNITY_EDITOR`) and false elsewhere — including Play mode in the
editor with a WebGL target, so the CORS-safe plan is only exercised in a browser.

With `CorsSafe = false` the client does what the `yyt` CLI does: the total length from
`Content-Range`, `If-Range` with the first answer's strong ETag, and a `206` without a
numeric total is `http`.

## When the object changes under a read

A mutable file can be replaced between two requests of one read. A `200` to a request
that carried `If-Range`, another ETag, another total length, or — without `If-Range` — a
`206` that names no ETag means exactly that, and the read starts over from the length and
the header, up to three restarts; a fourth is `http` ("the object kept changing"). **A
`200` to a ranged request without `If-Range` that names the same object means the host
ignores `Range`**, which an encrypted read cannot work around: `http`, at once. A host
that sends no ETag at all is still read, but only the tags then tell two objects apart,
so a change mid-read fails as `asset_corrupt` instead of starting over.

## Errors

Every failure the CDN, the network or the ciphertext causes is an
`AssetClientException` with a `Status` (HTTP, 0 when there was no answer), a `Code` from
`AssetErrorCodes`, and sometimes a `Detail`, a fixed SDK phrase. Its message is
`asset {code} ({status})`, with the detail after a colon, and never a key, a URL, a path
or a byte of plaintext.

| `Code` | When |
| --- | --- |
| `bad_key` | the key is not the canonical `yak1.` text or 32 bytes (thrown by `Create`) |
| `not_found` | `403` or `404`. **A missing object answers `403` on the yyt CDN**, so the two are one case |
| `asset_corrupt` | a length no ciphertext has, a failed tag, a wrong key, path or version, or `ReadJsonAsync` on bytes that are not UTF-8 JSON |
| `http` | any other status, a host that ignores `Range`, a `206` that is not the range asked for, an object that kept changing, a plain body larger than any asset |
| `network` | no answer, or none within `ResponseTimeout` (the transport's exception is the `InnerException`), or a body that failed, stalled past `BodyIdleTimeout`, ended early or ran past its stated length |

Local misuse — a malformed `BaseUrl` or path, a negative offset, an empty resume ETag —
is an `ArgumentException` before any request. A call on a disposed client is an
`ObjectDisposedException`, and so is a read in flight when it was disposed. A
cancellation of your own token is an `OperationCanceledException`.

## Security

Log lines are `asset request` at `Debug` with `{kind, path, status, range?}` — `kind` is
`whole`, `head`, `header`, `segments` or `range`, and `path` is capped at 64 characters
with control, format and bidi characters replaced, since a manifest may have chosen it —
`asset request failed` at `Warn` with `{kind, path}`, `asset ranged request refused;
reading the whole file` at `Warn` with `{path}`, and `asset changed during a read;
starting over` at `Info` with `{path, restart}`. None carries the key, a URL, a byte of a
body or a transport's error text. Each segment's tag is compared in constant time (every
byte XOR-accumulated) before a byte of it is decrypted. Every response the client does
not read to the end is disposed, on success and on failure.

## The crypto

netstandard2.0 has neither an `HKDF` class nor AES-CTR, so both are built here on what it
does have: HKDF-SHA256 on `HMACSHA256`, and AES-256-CTR as the ECB encryption of a counter
block whose last four bytes count big-endian from zero (a segment is at most 4,096
blocks, so they never carry). The service's conformance vectors — every positive case
whole and by ranges across every segment boundary, every negative case `asset_corrupt`,
in both request modes — are the test, together with an independent encryptor in the
tests.

## WebGL

`AssetHttpClientTransport.Default` cannot send there, and the transport does not default
per platform: pass `AssetUnityWebRequestTransport.Instance` (in `Runtime/Unity`, behind
`#if UNITY_5_3_OR_NEWER`) as `AssetBundleClientOptions.Transport`, and call it from the
main thread. It buffers each answer whole — a browser's fetch does too — so a fresh
download holds **the whole file** in memory, writing it to disk saves none, and
`ResponseTimeout` must cover the whole transfer. Elsewhere, prefer the default transport
for large downloads. **None of this has run in a Unity build yet**, WebGL or not; the
first editor run is owed ([manual-verification](../../rules/manual-verification.md)).

## What this does not do

No upload, no encryption, no listing: `yyt asset sync` is the only encryptor, and the
console owns the bundle's file list. No cache of its own, no retry of a `network`
failure (resume instead), no key rotation.

## Public API

- `AssetBundleClient.Create(AssetBundleClientOptions)` → `IAssetBundleClient`
  (`ReadAsync`, `ReadJsonAsync`, `ReadRangeAsync`, `DownloadAsync`, `Dispose`);
  `AssetBundleClient.DefaultTimeout`, `MaxTimeout`.
- `AssetBundleClientOptions`: `BaseUrl`, `Key`, `KeyBytes`, `CorsSafe`, `Transport`,
  `Logger`, `ResponseTimeout`, `BodyIdleTimeout`.
- `AssetReadOptions` (`NoCache`), `AssetDownloadOptions` (`Resume`, `OnProgress`,
  `NoCache`), `IAssetSink`, `AssetResume`, `AssetDownloadProgress`,
  `AssetDownloadResult`, and `AssetFiles.DownloadToFileAsync`.
- `AssetClientException` (`Code`, `Status`, `Detail`) and `AssetErrorCodes`.
- Transport seam: `IAssetTransport`, `AssetHttpRequest`, `IAssetResponse`,
  `AssetHttpClientTransport` (`Default`, `Create`), and `AssetUnityWebRequestTransport`
  in Unity builds.

## Differences from `@yingyeothon/asset-client`

One vocabulary with tslib and flutterlib — the same calls, codes, log lines and request
plans — adapted to C#:

- **`AssetBundleClient`, not `AssetBundle`**, which is a core `UnityEngine` type and would
  be ambiguous in every Unity script that imports both namespaces.
- `ReadJsonAsync` returns a `JsonValue` from `Yingyeothon.Codec`, not a caller-typed `T`:
  there is no reflection here. A document over 64 Mi characters or nested deeper than 64
  is `asset_corrupt`, and a byte-order mark is dropped.
- `close()` is `Dispose()`; a read of a disposed client is an `ObjectDisposedException`
  where tslib throws a plain `Error`.
- The key is `Key` (text) or `KeyBytes` (bytes), not one property of either type.
- The transport is `IAssetTransport` with a pull-based `IAssetResponse` rather than an
  injected `fetch`, so a `UnityWebRequest` adapter fits; `ResponseTimeout` and
  `BodyIdleTimeout` are flutterlib's, which tslib does not have.
- `NoCache` instead of the fetch `cache` mode, as in flutterlib.
- A file download ships as `AssetFiles.DownloadToFileAsync`, flutterlib's recipe, where
  tslib leaves the file sink to a documented snippet.
- Crypto is `System.Security.Cryptography` built up to HKDF and CTR, not WebCrypto, so
  the key cannot be made non-extractable; the client zeroes its own copies instead.

## Samples

`Asset Quickstart` (_Package Manager → Yingyeothon Asset Client → Samples → Import_): an
engine-free `AssetSession` over the manifest pattern — the manifest, a whole file, a
range and a resumable download to disk.
