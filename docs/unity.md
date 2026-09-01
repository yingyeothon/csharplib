# Unity

The floor is **Unity 2021.3**, which is what pins the language to C# 9. Both scripting
backends are verified against that floor and against Unity 6 before a release; the build
constraints that make it possible are in the [root README](../README.md).

## Installing

_Window → Package Manager → + → Add package from git URL_. A git-URL package cannot
resolve its own dependencies, so add each one:

```
https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.codec
https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.logger
https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.event-broker
https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.gamebase-client
```

Add a package's dependencies before the package itself, or Package Manager reports them
as missing until you do.

A URL with no fragment tracks `main`. **No release has been tagged yet**; once one is
cut, append `#<tag>` to every URL so a teammate's fresh import is the version you tested
against.

Every runtime asmdef here is `autoReferenced`, so a script in Unity's default
`Assembly-CSharp` needs no further step. **If your own scripts live in their own
asmdef**, reference the assemblies you use by name: `Yingyeothon.Gamebase.Client`,
`Yingyeothon.Codec` (needed for `JsonValue`, which is on the API), `Yingyeothon.Logger`
(needed to set `Logger`), `Yingyeothon.EventBroker`.

`.meta` files are not committed here; Unity generates them on import. If you vendor the
packages into `Packages/` instead of using a git URL, **copy** the folders rather than
symlinking them — Unity writes `.meta` files into whatever it imports.

## Samples

Each package ships importable samples: _Package Manager → the package → Samples →
Import_. They land in `Assets/Samples/…` and are yours to edit.

| Package | Sample | What it shows |
| --- | --- | --- |
| gamebase-client | `Lobby Quickstart` | the `MonoBehaviour` from [Getting started](getting-started.md) |
| gamebase-client | `Sign In` | exchanging a provider token for a channel JWT |
| gamebase-client | `Dungeon Run` | entry API → `q` socket → `Finished` / `Aborted` |
| gamebase-client | `WebGL Transport` | the `IWebSocketFactory` / `IHttpFetcher` adapters |
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

The [browser redirect flow](authentication.md#the-browser-redirect-flow) hands the token
back on a URL you nominate. A Unity build can be that destination two ways:

- **A loopback listener.** Start an `HttpListener` on `http://127.0.0.1:<port>/`, open
  the system browser at the start URL with that as `redirect`, and read the result. The
  token arrives in the URL **fragment**, which a browser does not send to a server, so
  the page you serve has to post it back with one line of script. Allowlist the exact
  loopback URL — it is the one case the service accepts over `http`.
- **A custom-scheme deep link.** Register `mygame://auth` for your build and allowlist
  it. Simpler on mobile, and the only option where no local port is available.

Whichever you use, put a nonce of your own in the redirect and check it comes back;
without it a link someone else built can sign a player in as themselves.

## IL2CPP

No reflection anywhere in a runtime assembly — no `Activator.CreateInstance`, no
`GetType().GetProperty`, no attribute-driven serialization — because IL2CPP's managed
stripper removes what it cannot see being used and fails at runtime, in a shipped
player, rather than at build time. Wire types parse and build themselves by hand.

`Runtime/link.xml` in the gamebase-client package preserves the three runtime
assemblies wholesale, since they are reached through interfaces and generic factories.
It is picked up automatically. Managed stripping at **High** is verified before each
release with a player that actually runs and touches every package.

If you add your own reflection over these types, add your own `link.xml` entries.

## WebGL

`ClientWebSocket` throws `PlatformNotSupportedException` on WebGL, `HttpClient` does not
work, and there is no thread to run a receive loop on. `WebSocketTransport.Default`
therefore throws there **on purpose**, rather than failing quietly at some later point.

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
