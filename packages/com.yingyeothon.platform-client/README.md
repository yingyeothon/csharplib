# Yingyeothon.Platform

Pumped wire clients for platform time, boards, social relations and gateway presence.

## Install

Not in a release yet: the release that ships this package adds its install URL here.

Dependencies: `com.yingyeothon.codec`, `com.yingyeothon.logger`, `com.yingyeothon.kvstore-client`. Add dependencies before this package on the same release tag.

## Usage

```csharp
var client = PlatformClient.Create(new PlatformClientOptions { BaseUrl = "https://doc.yyt.life", Token = channelJwt });
Task<long> time = client.TimeAsync();
// Poll each frame while awaiting time.
```

## Poll and lifetime

Call `Poll()` from one serialized owner, usually Unity Update. I/O answers are queued;
parsing, state changes and task settlement run on the pump. Never block on a pending
task. Continue pumping after disposal until outstanding tasks have settled.
Use the KV `UnityWebRequestTransport.Instance` for HTTP on WebGL and inject a
WebGL socket factory for live sockets. No engine or Firebase dependency enters the core.

## Public API

- `IPlatformBoard`.
- `IPlatformClient`.
- `IPlatformHttpClient`.
- `IPlatformSocial`.
- `IPollable`.
- `PlatformBoardPageOptions`.
- `PlatformClient`.
- `PlatformClientOptions`.
- `PlatformException`.
- `PlatformHttpClient`.
- `PlatformPresenceOptions`.
- `PlatformProfileOptions`.
- `PlatformRequestOptions`.
- `PlatformScoreOptions`.

## Differences from the TypeScript model

The service contract is normative. JSON values are immutable `JsonValue` trees,
not reflection-based serialization. Stateful implementations are hidden behind
interfaces and factories. Async results require `Poll`, preserving Unity thread
and transition ordering.

See [the integration guide](../../docs/platform.md) for module behavior.
