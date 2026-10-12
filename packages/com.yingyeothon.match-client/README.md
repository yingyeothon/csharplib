# Yingyeothon.Match

Live socket matchmaking and deferred ticket HTTP clients with one matched-result shape.

## Install

```
https://github.com/yingyeothon/csharplib.git?path=/packages/com.yingyeothon.match-client#v0.3.0
```

Dependencies: `com.yingyeothon.platform-client`, `com.yingyeothon.gamebase-client`, `com.yingyeothon.codec`, `com.yingyeothon.logger`, `com.yingyeothon.kvstore-client`. Add dependencies before this package on the same release tag.

## Usage

```csharp
var client = DeferredMatchClient.Create(new DeferredMatchClientOptions
{ ChannelId = matchChannelId, Http = new PlatformClientOptions { BaseUrl = matchApiBase, Token = channelJwt } });
var ticket = client.SubmitAsync();
// Poll each frame while awaiting ticket.
```

## Poll and lifetime

Call `Poll()` from one serialized owner, usually Unity Update. I/O answers are queued;
parsing, state changes and task settlement run on the pump. Never block on a pending
task. Continue pumping after disposal until outstanding tasks have settled.
Use the KV `UnityWebRequestTransport.Instance` for HTTP on WebGL and inject a
WebGL socket factory for live sockets. No engine or Firebase dependency enters the core.

## Public API

- `DeferredMatchClient`.
- `DeferredMatchClientOptions`.
- `IDeferredMatchClient`.
- `ILiveMatchClient`.
- `LiveMatchClient`.
- `LiveMatchClientOptions`.
- `MatchResult`.
- `MatchTicket`.
- `MatchTicketState`.

## Differences from the TypeScript model

The service contract is normative. JSON values are immutable `JsonValue` trees,
not reflection-based serialization. Stateful implementations are hidden behind
interfaces and factories. Async results require `Poll`, preserving Unity thread
and transition ordering.

See [the integration guide](../../docs/matchmaking.md) for module behavior.
