# Yingyeothon.Push

Android FCM registration core with injected device tokens; the game owns Firebase.

## Install

Not in a release yet: the release that ships this package adds its install URL here.

Dependencies: `com.yingyeothon.platform-client`, `com.yingyeothon.codec`, `com.yingyeothon.kvstore-client`. Add dependencies before this package on the same release tag.

## Usage

```csharp
var client = PushClient.Create(new PushClientOptions
{ ChannelId = pushChannelId, Http = new PlatformClientOptions { BaseUrl = stateBase, Token = channelJwt } });
var registered = client.RegisterAsync(new PushRegisterOptions { Token = deviceToken, Project = firebaseProjectId });
// Poll each frame while awaiting registered.
```

## Poll and lifetime

Call `Poll()` from one serialized owner, usually Unity Update. I/O answers are queued;
parsing, state changes and task settlement run on the pump. Never block on a pending
task. Continue pumping after disposal until outstanding tasks have settled.
Use the KV `UnityWebRequestTransport.Instance` for HTTP on WebGL and inject a
WebGL socket factory for live sockets. No engine or Firebase dependency enters the core.

## Public API

- `IPushClient`.
- `PushClient`.
- `PushClientOptions`.
- `PushMessageKind`.
- `PushRegisterOptions`.

## Differences from the TypeScript model

The service contract is normative. JSON values are immutable `JsonValue` trees,
not reflection-based serialization. Stateful implementations are hidden behind
interfaces and factories. Async results require `Poll`, preserving Unity thread
and transition ordering.

See [the integration guide](../../docs/push.md) for module behavior.

Android/FCM only. iOS is unsupported. Topic broadcast is public, grants no rights,
and its exact name comes from kit-config. Device and player tokens must never
reach logs. Call register on startup and token refresh, delete on sign-out.
