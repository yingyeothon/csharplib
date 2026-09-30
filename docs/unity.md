# Unity

The floor is **Unity 2021.3**, which is what pins the language to C# 9. Both scripting
backends are verified against that floor and against Unity 6 before a release; the build
constraints that make it possible are in the [root README](../README.md).

## Installing

_Window → Package Manager → + → Add package from git URL_. A git-URL package cannot
resolve its own dependencies, so add each one:

```
https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.codec#v0.1.0
https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.logger#v0.1.0
https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.event-broker#v0.1.0
https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.gamebase-client#v0.1.0
https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.kvstore-client#v0.1.0
https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.auth-client#v0.1.0
https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.asset-client#v0.1.0
```

Add a package's dependencies before the package itself, or Package Manager reports them
as missing until you do.

A URL with no fragment tracks `main`. Keep **one** release tag on every
`com.yingyeothon.*` URL, so a teammate's fresh import is the version you tested against:
each package's `package.json` pins its siblings to its own version, so mixed tags are
unsupported.

**Upgrading.** Releases are this repository's tags; each annotated tag's message says
what changed on the public surface and what you must change (`git tag -n99 -l` in a
clone). While the version is `0.x`, a minor bump (`0.1` → `0.2`) may break your code and
a patch does not. Change the fragment of every `com.yingyeothon.*` entry in
`Packages/manifest.json` in one edit; a changed URL makes the Package Manager resolve it
again and rewrite its `packages-lock.json` entry.

Every runtime asmdef here is `autoReferenced`, so a script in Unity's default
`Assembly-CSharp` needs no further step. **If your own scripts live in their own
asmdef**, reference the assemblies you use by name: `Yingyeothon.Gamebase.Client`,
`Yingyeothon.KvStore`, `Yingyeothon.Auth`, `Yingyeothon.Assets`, `Yingyeothon.Codec` (needed for `JsonValue`,
which is on every client's API), `Yingyeothon.Logger` (needed to set `Logger`),
`Yingyeothon.EventBroker`.

Every asset ships with its `.meta`, so a given version has the same GUIDs in every
project that installs it. If you vendor the packages into `Packages/` instead of using a
git URL, **copy** the folders rather than symlinking them — Unity writes into whatever it
imports, and through a symlink that is someone's checkout.

These sources are written with nullable annotations, and Unity has no project-wide
setting for them, so each assembly folder carries a `csc.rsp` holding `-nullable:enable`
that Unity applies to that assembly alone. Two consequences worth knowing, because
nothing warns you about either:

- **The flag does not follow a file out of its folder.** A `Runtime` source you copy
  into `Assets/` to patch, and an imported sample you extend, compile under *your*
  project's settings. If the file uses `string?` and your assembly has no nullable
  context, you get `CS8632` on every annotation. Adding `#nullable enable` at the top of
  that file is the fix; the six samples that need it already have it.
- **Your own assembly is yours to configure.** Signatures copied out of the
  [API reference](README.md#reference) carry `?`, so an asmdef of your own that uses them
  wants the same one-line `csc.rsp` beside it, or `#nullable disable` and no
  annotations. Either is fine; this SDK does not care which you pick.

## Samples

Each package ships importable samples: _Package Manager → the package → Samples →
Import_. They land in `Assets/Samples/…` and are yours to edit.

| Package | Sample | What it shows |
| --- | --- | --- |
| gamebase-client | `Lobby Quickstart` | the `MonoBehaviour` from [Getting started](getting-started.md) |
| gamebase-client | `Dungeon Run` | entry API → `q` socket → `Finished` / `Aborted` |
| gamebase-client | `WebGL Transport` | the `IWebSocketFactory` / `IHttpFetcher` adapters |
| auth-client | `Sign In` | both ways to get a channel JWT, and checking a kept one |
| asset-client | `Asset Quickstart` | the manifest pattern: a manifest, a whole file, a range, a resumable download |
| kvstore-client | `KvStore Quickstart` | announcements and a player's own record, from [Key-value store](kvstore.md) |
| codec | `Json Basics` | building and reading frames |
| logger | `Unity Logging` | routing the logger to the editor console |
| event-broker | `Typed Events` | the type-keyed broker |

## Polling

Nothing happens without `Poll()`, and it must run on your main thread, every frame,
unconditionally. `GamebaseRunner.CreatePersistent(name)` is a `MonoBehaviour` that does
it for a set of clients across scene loads. The full contract is
[Connection lifecycle](connection-lifecycle.md).

`GamebaseRunner` lives behind `#if UNITY_5_3_OR_NEWER` and is therefore **absent from
the generated API reference**, which is produced from the `dotnet` build. It exists in
Unity and nowhere else; that is why the reference does not list it.

Because it survives scene loads, it also survives leaving play mode in the editor unless
you clean up: dispose your clients in `OnDestroy` (or on
`Application.quitting`) so a second Enter Play Mode does not find a live socket from the
first, still polling and still holding the player's session.

## Logging to the editor console

The logger package declares no engine reference, so wire it yourself:

```csharp
using Yingyeothon.Logger;
using YLogger = Yingyeothon.Logger.ILogger;   // UnityEngine.ILogger exists too

YLogger logger = FilteredLogger.Create(new FilteredLoggerOptions
{
    Severity = LogSeverity.Info,
    Writer = LogWriters.FromAction((severity, message, context) =>
        UnityEngine.Debug.Log(LogWriters.Format(severity, message, context))),
});

logger.Severity = LogSeverity.Debug;   // takes effect on the next call
```

The alias is not optional: `UnityEngine.ILogger` exists, and a script with
`using UnityEngine;` and `using Yingyeothon.Logger;` will not compile without one.

Then pass it as `GatewayClientOptions.Logger`. **Do not use `ConsoleLogger` in Unity** —
it writes to `System.Console`, which the editor console does not show.

This SDK logs ids, codes, counts and lengths — never a token, a frame body, a payload
or a close reason. Keep it that way in your own writers: a consumer's writer may
persist forever, and `Debug` is not an exemption.

## Signing in

`com.yingyeothon.auth-client` builds the sign-in URL and reads the URL the browser
returns to; [Authentication](authentication.md) is the guide. **Receiving that return is
your build's job**, and the token arrives in the URL's **fragment**, which a browser never
sends to a server — only script in the page, or the app a link opens, sees it. The
service accepts a `redirect` only over `https`, or `http` for `localhost`, `127.0.0.1`
and `[::1]`, so a custom scheme (`mygame://`) is refused.

| Build | How the URL comes back | `redirect` on the allowlist |
| --- | --- | --- |
| Android, iOS | a *verified* app link or universal link — an `https` URL your app claims — delivered by `Application.deepLinkActivated` and, after a cold start, `Application.absoluteURL` | that URL |
| WebGL | navigate the **same tab** to the start URL (a `.jslib` `window.location.assign`; `Application.OpenURL` opens a new window, which a browser blocks outside a click and which would boot a second copy of the game). The return reloads the build: read `Application.absoluteURL` at startup | your page |
| Windows, macOS, Linux | a loopback page: bind `HttpListener` to `http://127.0.0.1:<port>/`, serve a page whose script posts its whole `location.href` — query and fragment together — back to the listener, which refuses any `Origin` but its own, accepts once and closes; or have the player paste the address bar back | that loopback URL |

Whichever you use:

- **Keep the nonce across the trip.** WebGL reloads the build and a phone may kill the
  app while the browser is in front. Keep the nonce from `AuthClient.NewNonce()` until
  `ParseRedirect` has checked it, then delete it: on a phone or desktop in `PlayerPrefs`,
  calling `PlayerPrefs.Save()` before opening the browser (Unity otherwise writes them
  only at a clean quit); on WebGL in the browser's `sessionStorage` through a `.jslib`,
  because there `PlayerPrefs.Save()` reaches IndexedDB asynchronously and the navigation
  can outrun it.
- **Drop the returned URL** once `ParseRedirect` has read it. It is a credential for
  the channel's `tokenTtlSec`, and there is no revocation.
- **On WebGL, take the fragment before anything else sees it.** The build loads seconds
  after the page does, and an analytics or error tag in your page template records
  `location.href`, fragment and all. Have an inline script at the top of the template
  store the whole `location.href` — the query carries the nonce, the fragment the token —
  in `sessionStorage` and `history.replaceState` the page to its path alone; hand that
  stored URL to `ParseRedirect` instead of `Application.absoluteURL`.
- **Allowlist the narrowest path**, and serve nothing at it that redirects elsewhere: a
  browser carries the fragment across a redirect, so an open redirect under an allowed
  prefix hands the token to whatever page it names.
- **An app link reached through a redirect may not open the app.** Some mobile browsers
  hand a *tapped* link to the app that claims it but load a *redirect* to it as a page.
  Serve a page at that URL with no third-party script that strips the fragment and
  offers a button back into the app, and prefer the provider's own SDK plus
  `ExchangeAccessTokenAsync` / `ExchangeIdTokenAsync` on a phone when you can.
- A loopback listener must bind `127.0.0.1` only, on a **fixed** port registered on the
  allowlist — the service matches the exact origin, port included — that nothing else on
  the machine serves: whatever answers there receives the nonce and could read the
  fragment. The whole-URL
  post and the `Origin` check are what stop another page in the player's browser from
  racing a token of its own into the listener.
- **On WebGL the service calls cannot be made yet.** `FetchConfigAsync`, the exchanges
  and `VerifyAsync` are cross-origin, and the auth service sends no CORS headers, so the
  browser blocks them; the browser flow above needs none of them.

## IL2CPP

No reflection anywhere in a runtime assembly — no `Activator.CreateInstance`, no
`GetType().GetProperty`, no attribute-driven serialization — because IL2CPP's managed
stripper removes what it cannot see being used and fails at runtime, in a shipped
player, rather than at build time. Wire types parse and build themselves by hand.

`Runtime/link.xml` in each client package (gamebase-client, kvstore-client, auth-client,
asset-client) preserves
its own assembly plus `Yingyeothon.Codec` and `Yingyeothon.Logger` wholesale, since they
are reached through interfaces and generic factories. It is picked up automatically. Managed stripping at **High** is verified before each
release with a player that actually runs and touches every package.

If you add your own reflection over these types, add your own `link.xml` entries.

## WebGL

`ClientWebSocket` throws `PlatformNotSupportedException` on WebGL, `HttpClient` does not
work, and there is no thread to run a receive loop on. `WebSocketTransport.Default`
therefore throws there **on purpose**, rather than failing quietly at some later point;
`ConnectAsync()` fails with `GatewayStoppedException`.

**A WebGL player has no thread pool and no timer thread.** `Task.Run`, `Task.Delay` and a
`CancellationTokenSource` with a timeout (`CancelAfter`) never run or fire there, and
`Task.WhenAny` over a task completed with `RunContinuationsAsynchronously` never
completes; a plain `await` does resume, on the main thread. So on WebGL:

- A timeout is `UnityWebRequest.timeout`. The store, auth and asset transports set it
  from the client's own timeout; a token you cancel on a timer does nothing.
- Any task you hand the SDK — from an adapter you write (a socket, an `IHttpFetcher`) or
  from an `EventBroker` handler — completes **on the main thread and without
  `RunContinuationsAsynchronously`**. `MapAsync` and `EventBroker` continue from it with
  `ConfigureAwait(false)`, which sends an asynchronously completed task's continuation to
  the thread pool, and there is none. An `async` method's own task is fine; a raw
  `TaskCompletionSource` needs care.

A WebGL build supplies its own transport through the same options every other build
uses — this is configuration, not a fork:

```csharp
var lobby = GatewayLobbyClient.Create(new GatewayLobbyClientOptions
{
    Url = url,
    ChannelId = channelId,
    Token = channelJwt,
    WebSocketFactory = new WebGLWebSocketFactory(),   // over a .jslib socket
    HttpFetcher = new WebGLHttpFetcher(),             // over UnityWebRequest
});
```

What the seams require:

- **`IWebSocketFactory.Create(WebSocketCreateContext)`** returns an `IWebSocket`. The
  context carries the URL and the subprotocol list, which is always `["bearer", token]`.
  A factory **may** throw for input it can reject up front — a malformed URL, a
  subprotocol with non-token characters — and the SDK reports that as a stop.
  Everything after construction, **including a refused handshake**, must arrive as a
  close event on the sink, or the handshake-failure policy never sees it.
- **`IWebSocketEventSink`** is where a socket posts what it observed. It is a sink
  rather than events on the socket so the thread hand-off is structural: the only thing
  an adapter can do is enqueue, and `Poll()` drains it. There are exactly three kinds:
  post `SocketEvent.Opened` with the subprotocol the server selected,
  `SocketEvent.Message` (or `BinaryMessage`, which the gateway treats as an error), and
  `SocketEvent.Closed`. **There is no error event** — a failure is a close, which is
  what the next rule is about.
- Report a close **exactly once** per socket, and report the locally requested code
  when the close was local — the state machine keys its decision on that code and a
  peer's echo would erase it. Answer a close frame the peer sent, or the peer waits for
  its own idle timer.
- Cap what you reassemble. The default transport caps a message at 64 KB and surfaces
  an over-size one as close `1009`.
- **`IHttpFetcher.GetAsync`** is a credential-free GET returning
  `HttpFetchResult { Ok, Status, Text }`. Give it a timeout, a size cap and a small
  redirect budget: the URL comes off the wire.

The `WebGL Transport` sample is the skeleton for both.

The key-value store client has the same seam and, unlike the gateway, ships the WebGL
side of it — see [Key-value store](#key-value-store) below. So does the auth client,
`AuthUnityWebRequestTransport.Instance`, although on WebGL its requests are blocked for
now: see [Signing in](#signing-in). And so does the asset client,
`AssetUnityWebRequestTransport.Instance`, which also switches itself to CORS-safe
requests in a WebGL player ([Asset bundles § On Unity](assets.md#on-unity)).

## Key-value store

`com.yingyeothon.kvstore-client` is plain HTTP and needs no `Poll()`: every call is one
request, awaited, and the continuation lands back on Unity's main thread through its
synchronization context. [Key-value store](kvstore.md) is the guide; two things are
Unity's:

- **The token** is the channel JWT your sign-in produced
  ([Signing in](#signing-in)) — the same string `GatewayClientOptions.Token` takes.
  Hand it to `KvStoreClientOptions.Token` and keep it nowhere else; the client puts
  it in the `Authorization` header and never in a log line, an exception or a URL.
- **WebGL.** `HttpClientTransport.Default` cannot send there. Pass
  `UnityWebRequestTransport.Instance`, which lives in the package behind
  `#if UNITY_5_3_OR_NEWER` and drives `UnityWebRequest` from the main thread. It works
  on every Unity platform, so passing it unconditionally is fine too; the sample
  selects it for WebGL only and keeps `HttpClient` elsewhere:

  ```csharp
  var kv = KvStoreClient.Create(new KvStoreClientOptions
  {
      BaseUrl = "https://doc.yyt.life",
      Token = channelJwt,
  #if UNITY_WEBGL && !UNITY_EDITOR
      Transport = UnityWebRequestTransport.Instance,
  #endif
  });
  ```

  The store's CORS policy is already open to any origin with the `Authorization`,
  `If-Match` and `If-None-Match` headers allowed and `ETag` exposed, so a browser
  build needs nothing else.

## Numbers and culture

Positions are `double`, because the wire is Go `float64`. Every conversion in this SDK
uses `CultureInfo.InvariantCulture` — a German or Turkish locale would otherwise put
`1,5` on the wire, the gateway would drop the whole frame as `bad_message`, and nothing
would tell the client. Do the same in any frame you build by hand.

One Mono difference is handled inside the codec: `double.TryParse("-0")` drops the sign
there, and the parser restores it, so a value tree has the same shape on both runtimes.
Note that the writer normalises `-0` back to `0` on the way out — negative zero survives
a parse, not a round trip. Compare parsed doubles rather than rendered text; a
golden-file test over JSON output can differ between the editor and CI for no
behavioural reason.

## Allocation

Frames allocate per parse. The gateway coalesces positions into one batch per `tick`
(200 ms by default), so this is tick-rate work rather than frame-rate work — measure
before pooling. Avoid LINQ in handlers you expect to run every tick.
